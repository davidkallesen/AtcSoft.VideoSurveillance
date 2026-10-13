using ConnectionState = Atc.Network.ConnectionState;
using IUsbCameraWatcher = AtcSoft.VideoSurveillance.Services.IUsbCameraWatcher;
using UsbCameraEventArgs = AtcSoft.VideoSurveillance.Events.UsbCameraEventArgs;

namespace AtcSoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// View model for the live camera grid view.
/// Manages camera tiles, grid layout, and stream lifecycle.
/// </summary>
public sealed partial class LiveViewViewModel : ViewModelBase, IDisposable
{
    private readonly GatewayService gatewayService;
    private readonly SurveillanceHubService hubService;
    private readonly IVideoPlayerFactory videoPlayerFactory;
    private readonly IUsbCameraWatcher usbCameraWatcher;
    private readonly string apiBaseAddress;
    private readonly AtcSoft.VideoSurveillance.Services.IApplicationSettingsService? settingsService;
    private bool disposed;
    private bool loadedOnce;
    private bool usbWatcherSubscribed;

    [ObservableProperty]
    private ObservableCollection<CameraTileViewModel> cameraTiles = [];

    [ObservableProperty]
    private int gridRows;

    [ObservableProperty]
    private int gridColumns;

    [ObservableProperty]
    private bool isLoading;

    // What is typed in the filter box: the cameras whose name or
    // description has every word of it are shown.
    [ObservableProperty(AfterChangedCallback = nameof(OnArrangementInputChanged))]
    private string filterText = string.Empty;

    // The grid's size, as its number of cameras along a side ("0" is automatic).
    [ObservableProperty(AfterChangedCallback = nameof(OnArrangementInputChanged))]
    private string selectedGridPreset = "0";

    [ObservableProperty]
    private string pageLabel = string.Empty;

    [ObservableProperty]
    private bool hasPages;

    [ObservableProperty]
    private bool hasNoMatches;

    private int pageIndex;

    /// <summary>
    /// Gets the grid sizes to choose from.
    /// </summary>
    public IDictionary<string, string> GridPresetItems { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["0"] = "Automatic (all cameras)",
        ["1"] = "1 \u00D7 1",
        ["2"] = "2 \u00D7 2",
        ["3"] = "3 \u00D7 3",
        ["4"] = "4 \u00D7 4",
    };

    [RelayCommand]
    private void ClearFilter()
        => FilterText = string.Empty;

    [RelayCommand(CanExecute = nameof(HasPages))]
    private void NextPage()
    {
        pageIndex++;
        ApplyArrangement(wrapPages: true);
    }

    [RelayCommand(CanExecute = nameof(HasPages))]
    private void PreviousPage()
    {
        pageIndex--;
        ApplyArrangement(wrapPages: true);
    }

    // A new filter or grid size starts at the first page again.
    private void OnArrangementInputChanged()
    {
        pageIndex = 0;
        ApplyArrangement(wrapPages: false);
        RememberGridSize();
    }

    private void RememberGridSize()
    {
        if (settingsService is null)
        {
            return;
        }

        var size = (int)GridPager.ParsePreset(SelectedGridPreset);
        if (settingsService.General.GridSize == size)
        {
            return;
        }

        var general = settingsService.General.Clone();
        general.GridSize = size;
        settingsService.SaveGeneral(general);
    }

    /// <summary>
    /// Shows the tiles the filter and the page leave in, and sizes the grid
    /// for them. Has to run on the UI thread.
    /// </summary>
    private void ApplyArrangement(bool wrapPages)
    {
        var cameras = CameraTiles.Select(tile => (tile.DisplayName, tile.Description)).ToList();
        var preset = GridPager.ParsePreset(SelectedGridPreset);
        var arrangement = LiveGridArrangement.Arrange(cameras, FilterText, preset, pageIndex);

        // Past the last page comes the first, and before the first the last.
        if (wrapPages && arrangement.PageCount > 1 && arrangement.PageIndex != pageIndex)
        {
            pageIndex = pageIndex < 0 ? arrangement.PageCount - 1 : 0;
            arrangement = LiveGridArrangement.Arrange(cameras, FilterText, preset, pageIndex);
        }

        pageIndex = arrangement.PageIndex;

        for (var i = 0; i < CameraTiles.Count; i++)
        {
            CameraTiles[i].IsShownInGrid = arrangement.Shown[i];
        }

        GridRows = arrangement.Rows;
        GridColumns = arrangement.Columns;
        PageLabel = arrangement.PageLabel;
        HasPages = arrangement.PageCount > 1;
        HasNoMatches = CameraTiles.Count > 0 && arrangement.MatchCount == 0;
        CommandManager.InvalidateRequerySuggested();
    }

    public LiveViewViewModel(
        GatewayService gatewayService,
        SurveillanceHubService hubService,
        IVideoPlayerFactory videoPlayerFactory,
        IUsbCameraWatcher usbCameraWatcher,
        string apiBaseAddress,
        AtcSoft.VideoSurveillance.Services.IApplicationSettingsService? settingsService = null)
    {
        ArgumentNullException.ThrowIfNull(gatewayService);
        ArgumentNullException.ThrowIfNull(hubService);
        ArgumentNullException.ThrowIfNull(videoPlayerFactory);
        ArgumentNullException.ThrowIfNull(usbCameraWatcher);

        this.gatewayService = gatewayService;
        this.hubService = hubService;
        this.videoPlayerFactory = videoPlayerFactory;
        this.usbCameraWatcher = usbCameraWatcher;
        this.apiBaseAddress = apiBaseAddress;
        this.settingsService = settingsService;

        // The grid starts in the size it was last shown in.
        selectedGridPreset = ((int)GridPager.ParsePreset(
            settingsService?.General.GridSize.ToString(CultureInfo.InvariantCulture))).ToString(CultureInfo.InvariantCulture);

        // Subscribe immediately so unplug events that arrive before the
        // first LoadAsync (e.g. SignalR backlog replay) are still
        // applied once the matching tile materializes.
        SubscribeToUsbWatcher();

        hubService.OnReconnected += OnHubReconnected;
    }

    // The server stopped this client's streams when the connection went,
    // and cameras may have been added or removed meanwhile: the view is
    // loaded again. A view nobody has opened yet stays unloaded.
    private void OnHubReconnected()
    {
        if (disposed || !loadedOnce || IsLoading)
        {
            return;
        }

        _ = LoadAsync();
    }

    private void SubscribeToUsbWatcher()
    {
        if (usbWatcherSubscribed)
        {
            return;
        }

        usbCameraWatcher.DeviceArrived += OnUsbDeviceArrived;
        usbCameraWatcher.DeviceRemoved += OnUsbDeviceRemoved;
        usbCameraWatcher.Start();
        usbWatcherSubscribed = true;
    }

    private void OnUsbDeviceArrived(
        object? sender,
        UsbCameraEventArgs e)
        => UpdateTileUnpluggedState(e.Device.DeviceId, unplugged: false);

    private void OnUsbDeviceRemoved(
        object? sender,
        UsbCameraEventArgs e)
        => UpdateTileUnpluggedState(e.Device.DeviceId, unplugged: true);

    private void UpdateTileUnpluggedState(
        string deviceId,
        bool unplugged)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return;
        }

        // RemoteUsbCameraWatcher forwards SignalR payloads on whatever
        // thread the hub fires them on — marshal to the UI dispatcher
        // before mutating observable properties bound to the WPF view.
        // When there's no Application.Current (unit-test scenario) run
        // synchronously on the current thread — there's no UI binding
        // to violate and the test setup is single-threaded anyway.
        var app = Application.Current;
        if (app is null)
        {
            ApplyTileUnpluggedState(deviceId, unplugged);
            return;
        }

        _ = app.Dispatcher.BeginInvoke(() => ApplyTileUnpluggedState(deviceId, unplugged));
    }

    private void ApplyTileUnpluggedState(
        string deviceId,
        bool unplugged)
    {
        foreach (var tile in CameraTiles)
        {
            if (string.Equals(tile.UsbDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                tile.IsDeviceUnplugged = unplugged;
            }
        }
    }

    /// <summary>
    /// Reads the connection-type icon toggle from the server settings. A failure
    /// only hides the icon; it must never block the live view.
    /// </summary>
    private async Task<bool> IsConnectionTypeIconEnabledAsync()
    {
        try
        {
            var settings = await gatewayService
                .GetSettingsAsync()
                .ConfigureAwait(false);
            return settings?.ShowOverlayConnectionTypeIcon ?? false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    [RelayCommand("Load")]
    private async Task LoadAsync()
    {
        loadedOnce = true;
        IsLoading = true;

        try
        {
            var cameras = await gatewayService
                .GetCamerasAsync()
                .ConfigureAwait(false);

            var showConnectionTypeIcon = await IsConnectionTypeIconEnabledAsync().ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Stop & dispose existing tiles
                StopAndDisposeTiles();

                if (cameras is null || cameras.Length == 0)
                {
                    GridRows = 0;
                    GridColumns = 0;
                    return;
                }

                // Create a tile per camera
                foreach (var camera in cameras)
                {
                    var tile = new CameraTileViewModel(videoPlayerFactory, hubService, apiBaseAddress)
                    {
                        CameraId = camera.Id,
                        DisplayName = camera.DisplayName,
                        Description = camera.Description ?? string.Empty,
                        ConnectionState = MapApiConnectionState(camera.ConnectionState),
                        IsRecording = camera.IsRecording,
                        UsbDeviceId = camera.UsbDeviceId ?? string.Empty,
                        ConnectionType = camera.ConnectionType?.ToCoreConnectionType() ?? AtcSoft.VideoSurveillance.Enums.CameraConnectionType.None,
                        ShowConnectionTypeIcon = showConnectionTypeIcon,
                        Rotation = camera.Rotation?.ToCoreRotation() ?? AtcSoft.VideoSurveillance.Enums.CameraRotation.None,
                    };

                    CameraTiles.Add(tile);
                }

                // Size the grid, and leave out what the filter and the page leave out
                ApplyArrangement(wrapPages: false);
            });

            // Start all streams (off UI thread)
            var tiles = await Application.Current.Dispatcher.InvokeAsync(() => CameraTiles.ToList());
            foreach (var tile in tiles)
            {
                try
                {
                    await tile.StartStreamAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or Microsoft.AspNetCore.SignalR.HubException)
                {
                    // Per-tile stream start failed — tile shows "No Stream".
                    // HubException covers SignalR-side failures (e.g. the
                    // server-side FFmpeg playlist-ready timeout we throw
                    // from SurveillanceHub.StartStream); HttpRequestException
                    // covers transport-level failures. A single tile failing
                    // must not abort the rest of the live-view load.
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or Microsoft.AspNetCore.SignalR.HubException)
        {
            var app = Application.Current;
            if (app is not null)
            {
                await app.Dispatcher.InvokeAsync(() =>
                {
                    StopAndDisposeTiles();
                    GridRows = 0;
                    GridColumns = 0;
                });
            }
        }
        finally
        {
            // Application.Current can be null if a fatal exception triggered
            // App.ApplicationOnDispatcherUnhandledException → Shutdown while
            // this finally is unwinding. The app is going away anyway, so a
            // missed IsLoading = false is harmless.
            var app = Application.Current;
            if (app is not null)
            {
                await app.Dispatcher.InvokeAsync(() => IsLoading = false);
            }
        }
    }

    [RelayCommand("StopAll")]
    private async Task StopAllAsync()
    {
        var tiles = await Application.Current.Dispatcher.InvokeAsync(() => CameraTiles.ToList());

        foreach (var tile in tiles)
        {
            try
            {
                await tile.StopStreamAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup
            }
        }

        await Application.Current.Dispatcher.InvokeAsync(StopAndDisposeTiles);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Window self-disposes on close via the Closed event handler.")]
    public void OpenFullScreen(CameraTileViewModel tile)
    {
        ArgumentNullException.ThrowIfNull(tile);

        var viewModel = new FullScreenCameraWindowViewModel(
            hubService,
            tile.CameraId,
            tile.Player,
            tile.DisplayName,
            tile.Description);

        var window = new FullScreenCameraWindow(viewModel);
        window.Show();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        hubService.OnReconnected -= OnHubReconnected;

        if (usbWatcherSubscribed)
        {
            usbCameraWatcher.DeviceArrived -= OnUsbDeviceArrived;
            usbCameraWatcher.DeviceRemoved -= OnUsbDeviceRemoved;
            usbCameraWatcher.Stop();
            usbWatcherSubscribed = false;
        }

        // DI scope teardown at app shutdown calls Dispose on a worker
        // thread, so mutating CameraTiles (which is bound to a WPF
        // CollectionView) would throw NotSupportedException —
        // CollectionViews require dispatcher-thread mutation. Tile
        // disposal itself is non-UI work and safe from any thread; the
        // bound collection is going away with the app, so we skip the
        // cosmetic emptying of it on this path.
        foreach (var tile in CameraTiles)
        {
            tile.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void StopAndDisposeTiles()
    {
        foreach (var tile in CameraTiles)
        {
            tile.Dispose();
        }

        CameraTiles.Clear();
    }

    private static ConnectionState MapApiConnectionState(
        CameraConnectionState? apiState)
        => apiState switch
        {
            CameraConnectionState.Connected => ConnectionState.Connected,
            CameraConnectionState.Connecting => ConnectionState.Connecting,
            CameraConnectionState.Reconnecting => ConnectionState.Reconnecting,
            CameraConnectionState.Error => ConnectionState.ConnectionFailed,
            _ => ConnectionState.Disconnected,
        };
}