// ReSharper disable AsyncVoidEventHandlerMethod
namespace Linksoft.VideoSurveillance.Wpf.App;

[SuppressMessage(
    "Naming",
    "MA0049:Type name should not match containing namespace",
    Justification = "WPF convention: Application subclass is named App in the .App namespace.")]
[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "WPF convention: Application subclass is named App in the .App namespace.")]
public partial class App
{
    private const string AspireEnvVar = "services__api__https__0";
    private const string AspireEnvVarHttp = "services__api__http__0";
    private const string ChooseServerFlag = "--choose-server";

    private ILogger<App>? logger;
    private IHost? host;
    private bool isAspireManaged;

    public App()
    {
        // Configure paths first so the Serilog file sink can resolve the logs
        // directory. ApplicationStartup will call Configure again with the same
        // value — that's idempotent.
        ApplicationPaths.Configure("VideoSurveillance");

        // Drop framework Debug noise (Kestrel connection lifecycle, SignalR
        // protocol negotiation, request matching) but keep Linksoft.* at Debug
        // and keep Microsoft.* Information+ events (request finished, hosting
        // lifetime, etc) for ops visibility.
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture);

        try
        {
            var logsDir = ApplicationPaths.DefaultLogsPath;
            Directory.CreateDirectory(logsDir);

            var logFile = Path.Combine(logsDir, "video-surveillance-wpf-.log");
            loggerConfig.WriteTo.File(
                logFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                formatProvider: CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logs directory not writable — fall back to Debug sink only.
            // The app must still start.
        }

        Log.Logger = loggerConfig.CreateLogger();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        AppDomain.CurrentDomain.UnhandledException += CurrentDomainUnhandledException;
        Current.DispatcherUnhandledException += ApplicationOnDispatcherUnhandledException;
        base.OnStartup(e);

        if (Debugger.IsAttached)
        {
            BindingErrorTraceListener.StartTrace();
        }
    }

    private void CurrentDomainUnhandledException(
        object sender,
        UnhandledExceptionEventArgs args)
    {
        if (args is not { ExceptionObject: Exception ex })
        {
            return;
        }

        var exceptionMessage = ex.GetMessage(true);

        LogCurrentDomainUnhandledException(exceptionMessage);

        ShowFatalErrorDialog(exceptionMessage, Translations.CurrentDomainUnhandledException);
    }

    private void ApplicationOnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs args)
    {
        var exceptionMessage = args.Exception.GetMessage(true);
        if (exceptionMessage.Contains(
                "BindingExpression:Path=HorizontalContentAlignment; DataItem=null; target element is 'ComboBoxItem'",
                StringComparison.Ordinal))
        {
            args.Handled = true;
            return;
        }

        LogDispatcherUnhandledException(exceptionMessage);

        ShowFatalErrorDialog(exceptionMessage, Translations.DispatcherUnhandledException);

        args.Handled = true;
        Shutdown(-1);
    }

    /// <summary>
    /// Shows the fatal-error dialog using <see cref="InfoDialogBox"/>
    /// when an owner window is reachable. The handler runs while the
    /// app is collapsing — if no window is up yet (early-startup
    /// crash) we silently skip the popup; the original exception is
    /// already in the Serilog file sink.
    /// </summary>
    private static void ShowFatalErrorDialog(
        string message,
        string title)
    {
        var owner = Application.Current?.MainWindow
                    ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault();
        if (owner is null)
        {
            return;
        }

        var settings = new DialogBoxSettings(DialogBoxType.Ok, LogCategoryType.Error)
        {
            TitleBarText = title,
            Width = 500,
        };

        var dialog = new InfoDialogBox(owner, settings, message);
        dialog.ShowDialog();
    }

    private async void ApplicationStartup(
        object sender,
        StartupEventArgs args)
    {
        // Configure application paths before anything uses them. Points at
        // the same folder as the API server (Linksoft\VideoSurveillance\)
        // so the "default" path labels shown in the Settings dialog (e.g.
        // recordings / logs) match where the server actually keeps things.
        // The WPF client itself never writes to those directories — its
        // only on-disk file is the per-user prefs at %LocalAppData%
        // \Linksoft\VideoSurveillance.Client\client-prefs.json, written by
        // ApiApplicationSettingsService.
        ApplicationPaths.Configure("VideoSurveillance");

        // 1. Resolve API base address
        var apiBaseAddress = ResolveApiBaseAddress(args.Args);
        if (apiBaseAddress is null)
        {
            // Dialog cancelled — exit
            Shutdown();
            return;
        }

        // 2. Build DI host
        host = BuildHost(apiBaseAddress);
        logger = host.Services.GetService<ILoggerFactory>()!.CreateLogger<App>();

        LogAppStarting();

        // 3. Show splash screen
        var splashScreen = new SplashScreenWindow
        {
            Header = "Linksoft Video Surveillance",
            VersionText = $"Version {ApplicationHelper.GetVersion()}",
        };

        splashScreen.Show();

        // Loading configuration (0%)
        RenderSplashMessage("Loading configuration...", 0);

        // Initialize video engine (10%)
        RenderSplashMessage("Initializing video engine...", 10);
        VideoEngineBootstrap.Initialize();

        // Start host (20%)
        RenderSplashMessage("Starting services...", 20);
        await host.StartAsync();

        // Apply theme from settings (50%). Settings are pulled from the
        // API server (server-side fields) merged with local per-user
        // prefs (theme / language / window state) — the cache must be
        // populated before any consumer reads .General etc.
        RenderSplashMessage("Applying settings...", 50);
        var appSettingsService = host.Services.GetRequiredService<IApplicationSettingsService>();
        await appSettingsService.LoadAsync();
        var generalSettings = appSettingsService.General;
        ThemeManagerHelper.SetThemeAndAccent(Current, $"{generalSettings.ThemeBase}.{generalSettings.ThemeAccent}");

        // Connect SignalR hub (70%)
        RenderSplashMessage("Connecting to server...", 70);
        var hubService = host.Services.GetRequiredService<SurveillanceHubService>();
        try
        {
            await hubService.ConnectAsync();
        }
        catch (Exception ex)
        {
            LogSignalRConnectionFailed(ex);
        }

        // Load main window (80%)
        RenderSplashMessage("Loading main window...", 80);
        var mainWindow = host.Services.GetRequiredService<MainWindow>();

        // Complete (100%)
        RenderSplashMessage("Ready", 100);

        // Show main window and close splash
        mainWindow.Show();
        MainWindow = mainWindow;
        splashScreen.Close();

        // Start notification coordinator (after window is shown for toast visual tree)
        var coordinator = host.Services.GetRequiredService<NotificationCoordinator>();
        var prefsService = new NotificationPreferencesService();
        prefsService.Load();
        coordinator.Preferences = prefsService.Preferences;

        try
        {
            var gatewayService = host.Services.GetRequiredService<GatewayService>();
            var cameras = await gatewayService.GetCamerasAsync().ConfigureAwait(true);
            coordinator.UpdateCameraNameCache(cameras);
        }
        catch (HttpRequestException)
        {
            // Camera name cache will use fallback names
        }

        coordinator.Start();

        // Load initial stats and start latency monitoring
        var mainViewModel = host.Services.GetRequiredService<MainWindowViewModel>();
        await mainViewModel.LoadInitialStatsAsync().ConfigureAwait(true);
        mainViewModel.StartLatencyMonitoring();

        LogAppStarted();
    }

    private string? ResolveApiBaseAddress(string[] commandLineArgs)
    {
        // Check Aspire environment variables
        var aspireUrl = Environment.GetEnvironmentVariable(AspireEnvVar)
                     ?? Environment.GetEnvironmentVariable(AspireEnvVarHttp);
        if (!string.IsNullOrEmpty(aspireUrl))
        {
            isAspireManaged = true;
            return aspireUrl.TrimEnd('/');
        }

        // Load server profile service
        var profileService = new Services.ServerProfileService();
        profileService.Load();

        var forceChooseServer = Array.Exists(commandLineArgs, arg =>
            string.Equals(arg, ChooseServerFlag, StringComparison.OrdinalIgnoreCase));

        // If we have a last-used profile and no --choose-server flag, auto-connect
        if (!forceChooseServer)
        {
            var lastUsed = profileService.GetLastUsedProfile();
            if (lastUsed is not null)
            {
                return lastUsed.Url.TrimEnd('/');
            }
        }

        // Show server connection dialog
        var vm = new Dialogs.ServerConnectionDialogViewModel(profileService);
        var dialog = new Dialogs.ServerConnectionDialog(vm);

        var result = dialog.ShowDialog();
        if (result != true || string.IsNullOrEmpty(vm.ResolvedUrl))
        {
            return null;
        }

        return vm.ResolvedUrl;
    }

    private IHost BuildHost(string apiBaseAddress)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        return Host
            .CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<IConfiguration>(configuration);

                // Settings service: server-side fields round-trip through
                // the API; per-user-machine prefs (theme/language/window
                // state) live in a tiny local file. Initial load happens
                // explicitly via LoadAsync() during ApplicationStartup so
                // the in-memory cache is populated before any consumer
                // reads it.
                services.AddSingleton<IApplicationSettingsService, ApiApplicationSettingsService>();

                // Video engine services
                services.AddSingleton<IGpuAcceleratorFactory, D3D11AcceleratorFactory>();
                services.AddSingleton<IVideoPlayerFactory, VideoPlayerFactory>();

                // Named HttpClient (matches Blazor.App pattern)
                services
                    .AddHttpClient("VideoSurveillance-ApiClient", client =>
                    {
                        client.BaseAddress = new Uri(apiBaseAddress);
                    })
                    .AddStandardResilienceHandler();

                // Source-generated endpoint DI
                services.AddVideoSurveillanceEndpoints();

                // API client services
                services.AddSingleton(sp =>
                {
                    var svc = ActivatorUtilities.CreateInstance<GatewayService>(sp);
                    svc.ApiBaseUrl = apiBaseAddress;
                    return svc;
                });

                services.AddSingleton(new SurveillanceHubService(apiBaseAddress));

                // USB camera enumeration + hot-plug via the API server.
                // RemoteUsbCameraEnumerator delegates to GET /devices/usb
                // so the dialog dropdown shows devices attached to the
                // *server* host. RemoteUsbCameraWatcher subscribes to
                // the SignalR `UsbCameraLifecycleChanged` event the
                // SurveillanceEventBroadcaster pushes when the server's
                // IUsbCameraLifecycleCoordinator transitions a device
                // between Unplugged and Replugged.
                services.AddSingleton<IUsbCameraGateway, GatewayUsbCameraGateway>();
                services.AddSingleton<Linksoft.VideoSurveillance.Services.IUsbCameraEnumerator, RemoteUsbCameraEnumerator>();
                services.AddSingleton<IUsbLifecycleHubChannel, SurveillanceHubLifecycleChannel>();
                services.AddSingleton<Linksoft.VideoSurveillance.Services.IUsbCameraWatcher, RemoteUsbCameraWatcher>();

                // GitHub release service
                services.AddSingleton<IGitHubReleaseService, GitHubReleaseService>();

                // Windows per-user auto-start toggle (HKCU Run key).
                services.AddSingleton<IAutoStartService, AutoStartService>();

                // Support-flow diagnostics export — captures a client-side
                // snapshot (versions, server URL, hub state, camera list)
                // into a JSON file for issue triage.
                services.AddSingleton<IDiagnosticsExportService>(sp =>
                    new DiagnosticsExportService(
                        sp.GetRequiredService<GatewayService>(),
                        sp.GetRequiredService<SurveillanceHubService>(),
                        sp.GetRequiredService<IApplicationSettingsService>(),
                        sp.GetRequiredService<IAutoStartService>(),
                        apiBaseAddress,
                        sp.GetRequiredService<ILogger<DiagnosticsExportService>>()));

                // Toast notification + notification coordinator
                services.AddSingleton<IToastNotificationService, ToastNotificationService>();
                services.AddSingleton<NotificationCoordinator>();

                // ViewModels
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<CameraListViewModel>();
                services.AddSingleton<LayoutListViewModel>();
                services.AddSingleton(sp => new LiveViewViewModel(
                    sp.GetRequiredService<GatewayService>(),
                    sp.GetRequiredService<SurveillanceHubService>(),
                    sp.GetRequiredService<IVideoPlayerFactory>(),
                    sp.GetRequiredService<Linksoft.VideoSurveillance.Services.IUsbCameraWatcher>(),
                    apiBaseAddress));
                services.AddSingleton(sp => new RecordingsViewModel(
                    sp.GetRequiredService<GatewayService>(),
                    new Uri(apiBaseAddress)));
                services.AddSingleton<NotificationHistoryViewModel>();

                // App
                var aspireManaged = isAspireManaged;
                services.AddSingleton(sp => new MainWindowViewModel(
                    sp.GetRequiredService<GatewayService>(),
                    sp.GetRequiredService<SurveillanceHubService>(),
                    sp.GetRequiredService<IGitHubReleaseService>(),
                    sp.GetRequiredService<IApplicationSettingsService>(),
                    sp.GetRequiredService<IAutoStartService>(),
                    sp.GetRequiredService<IDiagnosticsExportService>(),
                    sp.GetRequiredService<LiveViewViewModel>(),
                    sp.GetRequiredService<DashboardViewModel>(),
                    sp.GetRequiredService<CameraListViewModel>(),
                    sp.GetRequiredService<LayoutListViewModel>(),
                    sp.GetRequiredService<RecordingsViewModel>(),
                    sp.GetRequiredService<NotificationHistoryViewModel>(),
                    apiBaseAddress,
                    aspireManaged));
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    private static void RenderSplashMessage(
        string message,
        int percentage)
    {
        MessageListener.Instance.ReceiveMessage(message);
        PercentListener.Instance.ReceivePercent(percentage);
    }

    private async void ApplicationExit(
        object sender,
        ExitEventArgs args)
    {
        if (host is null)
        {
            await Log.CloseAndFlushAsync().ConfigureAwait(false);
            return;
        }

        LogAppClosing();

        // Stop latency monitoring
        var mainViewModel = host.Services.GetService<MainWindowViewModel>();
        mainViewModel?.StopLatencyMonitoring();

        // Disconnect SignalR hub
        var hubService = host.Services.GetService<SurveillanceHubService>();
        if (hubService is not null)
        {
            await hubService.DisconnectAsync().ConfigureAwait(false);
        }

        await host
            .StopAsync()
            .ConfigureAwait(false);

        host.Dispose();

        LogAppClosed();

        await Log.CloseAndFlushAsync().ConfigureAwait(false);
    }
}