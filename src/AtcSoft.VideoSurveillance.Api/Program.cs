// Load advanced settings early to configure logging before Host is built
// ReSharper disable SeparateLocalFunctionsWithJumpStatement
var advancedSettings = LoadAdvancedSettingsForLogging();

// Drop framework Debug noise (Kestrel connection lifecycle, SignalR protocol
// negotiation, request matching, static file middleware) but keep Linksoft.*
// at Debug and keep Microsoft.* Information+ events (request finished,
// hosting lifetime, etc) for ops visibility.
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Information)
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture);

if (advancedSettings.EnableDebugLogging)
{
    Directory.CreateDirectory(advancedSettings.LogPath);

    var logFile = Path.Combine(advancedSettings.LogPath, "video-surveillance-api-.log");
    loggerConfig
        .MinimumLevel.Debug()
        .WriteTo.File(
            logFile,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
            formatProvider: System.Globalization.CultureInfo.InvariantCulture);
}

Log.Logger = loggerConfig.CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    // Configure OpenAPI document generation
    builder.Services.AddOpenApi();

    // Register API handlers from the Domain project (source-generated)
    builder.Services.AddApiHandlersFromDomain();

    // Register Core service implementations for the server
    builder.Services.AddSingleton<ICameraStorageService, JsonCameraStorageService>();
    builder.Services.AddSingleton<IApplicationSettingsService, JsonApplicationSettingsService>();

    // Register both the concrete type and the interface to share one
    // instance — the broadcaster subscribes to ServerRecordingService's
    // server-only CameraConnectionStateChanged event (not on IRecordingService
    // since WPF doesn't need it).
    builder.Services.AddSingleton<ServerRecordingService>();
    builder.Services.AddSingleton<IRecordingService>(sp =>
        sp.GetRequiredService<ServerRecordingService>());
    builder.Services.AddSingleton<IMotionDetectionService, ServerMotionDetectionService>();

    // Initialize VideoEngine (FFmpeg in-process) and register services.
    // DecoderThreads=1: headless server uses single-threaded decoding per camera
    // to avoid thread_count=0 (auto) hanging in avcodec_open2 with certain codecs.
    VideoEngineBootstrap.Initialize(new VideoEngineConfig { DecoderThreads = 1 });
    builder.Services.AddSingleton<IVideoPlayerFactory>(sp =>
        new VideoPlayerFactory(sp.GetRequiredService<ILoggerFactory>()));
    builder.Services.AddSingleton<IMediaPipelineFactory, VideoEngineMediaPipelineFactory>();
    builder.Services.AddSingleton<StreamingService>();

    // USB-camera support — Null fallback first so non-Windows hosts
    // still compose; Windows then replaces the binding via
    // AddWindowsUsbCameraSupport. The /devices/usb endpoint inspects
    // the bound implementation and returns 503 when it's still the
    // Null fallback.
    builder.Services.AddSingleton<IUsbCameraEnumerator>(NullUsbCameraEnumerator.Instance);
    builder.Services.AddSingleton<IUsbCameraWatcher, NullUsbCameraWatcher>();
    if (OperatingSystem.IsWindows())
    {
        Linksoft.VideoEngine.Windows.DependencyInjection.ServiceCollectionExtensions
            .AddWindowsUsbCameraSupport(builder.Services);
    }

    // Lifecycle coordinator translates raw watcher events into
    // per-camera Unplugged / Replugged transitions for
    // CameraConnectionService. Singleton so the unplugged-set
    // survives across DoWorkAsync ticks.
    builder.Services.AddSingleton<IUsbCameraLifecycleCoordinator, UsbCameraLifecycleCoordinator>();

    // Configure CORS for Blazor client (AllowCredentials required for SignalR WebSocket transport).
    // Origins are loaded from configuration (Cors:AllowedOrigins). With AllowCredentials,
    // wildcard origins are forbidden — populate the section explicitly when fronting the
    // API from a non-loopback host.
    string[] defaultAllowedOrigins =
    [
        "http://localhost:5000",
        "https://localhost:5001",
        "http://localhost:39576",   // Blazor.App default HTTP
        "https://localhost:39575",  // Blazor.App default HTTPS
    ];
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? defaultAllowedOrigins;

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });

    // Register SignalR for real-time events
    builder.Services.AddSignalR();

    // Shared health tracker for all BackgroundServiceBase-derived workers.
    // Each worker reports its running state around every DoWorkAsync tick and
    // sets its own max-staleness window; the /health endpoint reads this to
    // surface a wedged or stopped background service.
    builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
    builder.Services.AddSingleton<IBackgroundServiceHealthService, BackgroundServiceHealthService>();

    // Event broadcaster: forwards Core events to SignalR. Built on
    // BackgroundServiceBase for consistency with the other workers (it
    // subscribes/unsubscribes in Start/Stop; DoWorkAsync is a no-op and the
    // idle loop parks on a long delay).
    builder.Services.AddSingleton(new SurveillanceEventBroadcasterOptions());
    builder.Services.AddHostedService<SurveillanceEventBroadcaster>();

    // Each BackgroundServiceBase-derived worker gets its OWN typed options
    // instance so they can run on independent intervals — a single shared
    // IBackgroundServiceOptions singleton would force them all onto one cadence.

    // Camera connection manager: auto-records cameras on connect and reaps
    // dead sessions every 30s.
    builder.Services.AddSingleton(new CameraConnectionServiceOptions());
    builder.Services.AddHostedService<CameraConnectionService>();

    // Periodic disk-retention enforcement; without this, continuous server
    // recording fills the disk and crashes the host.
    builder.Services.AddSingleton(new ServerMediaCleanupServiceOptions());
    builder.Services.AddHostedService<ServerMediaCleanupService>();

    // Clock-aligned segment rollover so server recordings don't grow into
    // single multi-day files. Uses the shared RecordingSlotCalculator so
    // boundary detection is immune to NTP rollback, midnight, and DST.
    builder.Services.AddSingleton(new ServerRecordingSegmentationServiceOptions());
    builder.Services.AddHostedService<ServerRecordingSegmentationService>();

    // Idle HLS stream reaper — sweeps orphaned FFmpeg transcoders every 10s.
    // Extracted from StreamingService's former internal Timer.
    builder.Services.AddSingleton(new HlsStreamReaperServiceOptions());
    builder.Services.AddHostedService<HlsStreamReaperService>();

    // Periodic liveness beacon so a 4-day soak log shows steady process /
    // recording-state metrics rather than going silent between disruptions.
    builder.Services.AddSingleton(new ServerHeartbeatServiceOptions());
    builder.Services.AddHostedService<ServerHeartbeatService>();

    var app = builder.Build();

    app.UseCors();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    // Redirect root to Scalar API docs
    app
        .MapGet("/", () => Results.Redirect("/scalar/v1"))
        .ExcludeFromDescription();

    // Map all generated REST endpoints
    app.MapEndpoints();

    // Liveness probe and recording diagnostics. Kept out of the OpenAPI
    // surface (ExcludeFromDescription) because they're operational
    // endpoints, not part of the API contract. /health/recordings is the
    // soak-monitoring surface — gives a single round-trip view of which
    // sessions are alive and which are "stuck" (session present but the
    // pipeline died and the reaper hasn't swept yet).
    // Liveness gate over the always-on background workers. Cleanup and
    // segmentation are intentionally excluded: cleanup may legitimately stop
    // (OnStartup run-once mode) and both no-op when disabled, so their state
    // isn't a process-health signal. A stale/stopped always-on worker flips
    // this to 503 so an orchestrator restarts the host.
    string[] monitoredServices =
    [
        nameof(CameraConnectionService),
        nameof(HlsStreamReaperService),
        nameof(ServerHeartbeatService),
    ];

    app
        .MapGet("/health", (IBackgroundServiceHealthService health) =>
        {
            var services = monitoredServices.ToDictionary(
                name => name,
                health.IsServiceRunning,
                StringComparer.Ordinal);

            var healthy = services.Values.All(running => running);

            var payload = new { Status = healthy ? "ok" : "degraded", Services = services };

            return healthy
                ? Results.Ok(payload)
                : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .ExcludeFromDescription();

    app
        .MapGet("/health/recordings", (ServerRecordingService recordingService) =>
        {
            var diagnostics = recordingService.GetDiagnostics();
            var stuck = diagnostics.Count(d => !d.IsPipelineActive);
            var startTime = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
            var uptime = DateTime.UtcNow - startTime;

            return Results.Ok(new
            {
                UptimeSeconds = (long)uptime.TotalSeconds,
                ActiveRecordings = diagnostics.Count,
                StuckRecordings = stuck,
                DiskSpaceLow = diagnostics.Any(d => d.DiskSpaceLow),
                Sessions = diagnostics.Select(d => new
                {
                    d.CameraId,
                    d.CameraName,
                    d.FilePath,
                    StartedAtUtc = d.StartedAtUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                    DurationSeconds = (long)d.Duration.TotalSeconds,
                    d.IsPipelineActive,
                    d.DiskSpaceLow,
                }),
            });
        })
        .ExcludeFromDescription();

    // Serve HLS stream segments as static files
    var hlsContentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider
    {
        Mappings =
        {
            [".m3u8"] = "application/vnd.apple.mpegurl",
            [".ts"] = "video/mp2t",
        },
    };

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
            app.Services.GetRequiredService<StreamingService>().HlsOutputRoot),
        RequestPath = "/streams",
        ContentTypeProvider = hlsContentTypes,
    });

    // Serve recorded video files as static files
    var recordingPath = app.Services.GetRequiredService<IApplicationSettingsService>()
        .Recording.RecordingPath;

    Directory.CreateDirectory(recordingPath);

    var recordingContentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider
    {
        Mappings =
        {
            [".mp4"] = "video/mp4",
            [".mkv"] = "video/x-matroska",
        },
    };

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(recordingPath),
        RequestPath = "/recordings-files",
        ServeUnknownFileTypes = false,
        ContentTypeProvider = recordingContentTypes,
    });

    // Map SignalR hub for real-time surveillance events
    app.MapHub<SurveillanceHub>("/hubs/surveillance");

    Log.Information("Video Surveillance API starting");

    await app
        .RunAsync()
        .ConfigureAwait(false);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Video Surveillance API terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

static AdvancedSettings LoadAdvancedSettingsForLogging()
{
    var settingsPath = ApplicationPaths.DefaultSettingsPath;

    if (!File.Exists(settingsPath))
    {
        return new AdvancedSettings();
    }

    try
    {
        var json = File.ReadAllText(settingsPath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AdvancedSettings();
        }

        var settings = JsonSerializer.Deserialize<ApplicationSettings>(
            json,
            Atc.Serialization.JsonSerializerOptionsFactory.Create());
        return settings?.Advanced ?? new AdvancedSettings();
    }
    catch (JsonException)
    {
        return new AdvancedSettings();
    }
    catch (IOException)
    {
        return new AdvancedSettings();
    }
}