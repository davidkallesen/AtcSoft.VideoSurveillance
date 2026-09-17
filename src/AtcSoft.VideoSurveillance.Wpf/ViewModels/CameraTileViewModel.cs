using ConnectionState = Atc.Network.ConnectionState;

namespace Linksoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// Per-camera view model for a single tile in the live view grid.
/// Manages HLS streaming via the API server and hub events.
/// </summary>
public sealed partial class CameraTileViewModel : ViewModelBase, IDisposable
{
    private const int MaxStreamRetries = 5;
    private static readonly TimeSpan StreamRetryDelay = TimeSpan.FromSeconds(5);

    private readonly SurveillanceHubService hubService;
    private readonly string apiBaseAddress;
    private bool disposed;

    [ObservableProperty]
    private Guid cameraId;

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private ConnectionState connectionState = ConnectionState.Disconnected;

    /// <summary>
    /// Symbolic-link / device-id of the underlying USB device when this
    /// tile is bound to a USB camera. Empty for network cameras. Used
    /// by <see cref="LiveViewViewModel"/> to match
    /// <see cref="Linksoft.VideoSurveillance.Services.IUsbCameraWatcher"/>
    /// events back to the right tile.
    /// </summary>
    [ObservableProperty(DependentPropertyNames = [nameof(IsUsbSource)])]
    private string usbDeviceId = string.Empty;

    /// <summary>True when this tile represents a USB camera source.</summary>
    public bool IsUsbSource => !string.IsNullOrEmpty(UsbDeviceId);

    /// <summary>
    /// True when the underlying USB device is currently unplugged.
    /// Orthogonal to <see cref="ConnectionState"/>: a USB camera can be
    /// <c>ConnectionFailed</c> *and* unplugged simultaneously, and the
    /// overlay surfaces both signals. Cleared automatically when the
    /// watcher reports the device returned.
    /// </summary>
    [ObservableProperty]
    private bool isDeviceUnplugged;

    private bool isRecording;
    private DispatcherTimer? recordingTimer;
    private DateTime recordingStartUtc;

    [ObservableProperty]
    private string recordingDurationText = "00:00:00";

    [ObservableProperty]
    private bool isMotionDetected;

    [ObservableProperty]
    private IReadOnlyList<Rect> currentBoundingBoxes = [];

    [ObservableProperty]
    private int analysisWidth = 320;

    [ObservableProperty]
    private int analysisHeight = 240;

    [ObservableProperty]
    private IVideoPlayer? player;

    private bool isStreaming;
    private DispatcherTimer? heartbeatTimer;

    // Auto-recover: when the HLS player drops (e.g. server reaped a stale
    // session, transient network blip, FFmpeg restart), retry StartStream
    // up to MaxStreamRetries times instead of leaving the tile stuck on
    // Disconnected. userStopInProgress suppresses the retry loop when the
    // stop was initiated by us (StopStreamAsync / Dispose).
    private bool userStopInProgress;
    private DispatcherTimer? retryTimer;
    private int retryAttempt;

    public CameraTileViewModel(
        IVideoPlayerFactory videoPlayerFactory,
        SurveillanceHubService hubService,
        string apiBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(videoPlayerFactory);
        ArgumentNullException.ThrowIfNull(hubService);

        this.hubService = hubService;
        this.apiBaseAddress = apiBaseAddress;

        Player = videoPlayerFactory.Create();
        Player.StateChanged += OnPlayerStateChanged;

        hubService.OnStreamStarted += OnStreamStarted;
        hubService.OnConnectionStateChanged += OnHubConnectionStateChanged;
        hubService.OnRecordingStateChanged += OnHubRecordingStateChanged;
        hubService.OnMotionDetected += OnHubMotionDetected;
    }

    public bool IsRecording
    {
        get => isRecording;
        set
        {
            if (isRecording == value)
            {
                return;
            }

            isRecording = value;
            RaisePropertyChanged(nameof(IsRecording));
            UpdateRecordingDurationTimer(value);
        }
    }

    public bool IsStreaming
    {
        get => isStreaming;
        set
        {
            if (isStreaming == value)
            {
                return;
            }

            isStreaming = value;
            RaisePropertyChanged(nameof(IsStreaming));
            UpdateHeartbeatTimer(value);
        }
    }

    public Task StartStreamAsync()
        => hubService.StartStreamAsync(CameraId);

    public Task StopStreamAsync()
    {
        userStopInProgress = true;
        CancelStreamRetry();
        Player?.Close();

        _ = Application.Current?.Dispatcher.InvokeAsync(() => IsStreaming = false);

        return hubService.StopStreamAsync(CameraId);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        hubService.OnStreamStarted -= OnStreamStarted;
        hubService.OnConnectionStateChanged -= OnHubConnectionStateChanged;
        hubService.OnRecordingStateChanged -= OnHubRecordingStateChanged;
        hubService.OnMotionDetected -= OnHubMotionDetected;

        if (recordingTimer is not null)
        {
            recordingTimer.Stop();
            recordingTimer.Tick -= OnRecordingTimerTick;
            recordingTimer = null;
        }

        if (heartbeatTimer is not null)
        {
            heartbeatTimer.Stop();
            heartbeatTimer.Tick -= OnHeartbeatTimerTick;
            heartbeatTimer = null;
        }

        if (retryTimer is not null)
        {
            retryTimer.Stop();
            retryTimer.Tick -= OnRetryTimerTick;
            retryTimer = null;
        }

        if (Player is not null)
        {
            Player.StateChanged -= OnPlayerStateChanged;
            Player.Dispose();
            Player = null;
        }
    }

    private void OnStreamStarted(SurveillanceHubService.StreamStartedEvent e)
    {
        if (e.CameraId != CameraId)
        {
            return;
        }

        var fullUrl = $"{apiBaseAddress.TrimEnd('/')}{e.PlaylistUrl}";
        var uri = new Uri(fullUrl);
        var options = new StreamOptions
        {
            UseLowLatencyMode = true,
            HardwareAcceleration = true,
        };

        _ = Task.Run(() =>
        {
            try
            {
                Player?.Open(uri, options);
            }
            catch
            {
                // State will be updated via StateChanged
            }
        });

        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            retryAttempt = 0;
            IsStreaming = true;
        });
    }

    private void OnPlayerStateChanged(
        object? sender,
        PlayerStateChangedEventArgs e)
    {
        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ConnectionState = e.NewState switch
            {
                PlayerState.Playing => ConnectionState.Connected,
                PlayerState.Opening => ConnectionState.Connecting,
                PlayerState.Stopped => ConnectionState.Disconnected,
                PlayerState.Error => ConnectionState.ConnectionFailed,
                _ => ConnectionState.Disconnected,
            };

            if (e.NewState is PlayerState.Stopped or PlayerState.Error)
            {
                var wasStreaming = IsStreaming;
                IsStreaming = false;

                if (wasStreaming && !userStopInProgress && !disposed)
                {
                    ScheduleStreamRetry();
                }

                userStopInProgress = false;
            }
        });
    }

    private void OnHubConnectionStateChanged(
        SurveillanceHubService.ConnectionStateEvent e)
    {
        if (e.CameraId != CameraId)
        {
            return;
        }

        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ConnectionState = ParseHubConnectionState(e.NewState);
        });
    }

    private static ConnectionState ParseHubConnectionState(string? wireValue)
        => wireValue switch
        {
            "Connected" => ConnectionState.Connected,
            "Connecting" => ConnectionState.Connecting,
            "Reconnecting" => ConnectionState.Reconnecting,
            "Error" => ConnectionState.ConnectionFailed,
            "Disconnected" => ConnectionState.Disconnected,
            _ => ConnectionState.Disconnected,
        };

    private void OnHubRecordingStateChanged(
        SurveillanceHubService.RecordingStateEvent e)
    {
        if (e.CameraId != CameraId)
        {
            return;
        }

        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsRecording = string.Equals(e.NewState, "recording", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(e.NewState, "recordingMotion", StringComparison.OrdinalIgnoreCase);
        });
    }

    private void UpdateRecordingDurationTimer(bool nowRecording)
    {
        if (nowRecording)
        {
            recordingStartUtc = DateTime.UtcNow;
            RecordingDurationText = TimeSpan.Zero.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

            if (recordingTimer is null)
            {
                recordingTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1),
                };
                recordingTimer.Tick += OnRecordingTimerTick;
            }

            recordingTimer.Start();
        }
        else
        {
            recordingTimer?.Stop();
            RecordingDurationText = TimeSpan.Zero.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }
    }

    private void OnRecordingTimerTick(
        object? sender,
        EventArgs e)
    {
        var elapsed = DateTime.UtcNow - recordingStartUtc;
        RecordingDurationText = elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    // Server-side StreamingService reaps an HLS session after 60s of inactivity
    // unless the hub method StreamHeartbeat is invoked. HLS playlist/segment HTTP
    // requests go through StaticFileMiddleware and do NOT touch the session
    // timestamp, so without this timer the FFmpeg transcoder is killed under us
    // and the tile flips to Disconnected. 30s heartbeat = 30s margin (safe across
    // a single dropped tick from a Wi-Fi blip or GC pause).
    private void UpdateHeartbeatTimer(bool nowStreaming)
    {
        if (nowStreaming)
        {
            if (heartbeatTimer is null)
            {
                heartbeatTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(30),
                };
                heartbeatTimer.Tick += OnHeartbeatTimerTick;
            }

            heartbeatTimer.Start();
        }
        else
        {
            heartbeatTimer?.Stop();
        }
    }

    private void OnHeartbeatTimerTick(
        object? sender,
        EventArgs e)
        => _ = SendHeartbeatAsync();

    private async Task SendHeartbeatAsync()
    {
        try
        {
            await hubService.StreamHeartbeatAsync(CameraId).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Microsoft.AspNetCore.SignalR.HubException or InvalidOperationException or HttpRequestException)
        {
            // Hub temporarily unavailable; the SignalR auto-reconnect will recover
            // and the next 30s tick will refresh activity. Reaper margin is 30s
            // so a single missed heartbeat is fine.
        }
    }

    private void ScheduleStreamRetry()
    {
        if (retryAttempt >= MaxStreamRetries)
        {
            return;
        }

        retryAttempt++;

        if (retryTimer is null)
        {
            retryTimer = new DispatcherTimer
            {
                Interval = StreamRetryDelay,
            };
            retryTimer.Tick += OnRetryTimerTick;
        }

        retryTimer.Stop();
        retryTimer.Start();
    }

    private void CancelStreamRetry()
    {
        retryTimer?.Stop();
        retryAttempt = 0;
    }

    private void OnRetryTimerTick(
        object? sender,
        EventArgs e)
    {
        retryTimer?.Stop();
        _ = TryStartStreamForRetryAsync();
    }

    private async Task TryStartStreamForRetryAsync()
    {
        if (disposed || userStopInProgress)
        {
            return;
        }

        try
        {
            await StartStreamAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Microsoft.AspNetCore.SignalR.HubException or InvalidOperationException or HttpRequestException)
        {
            // Hub or transport down — schedule another attempt directly,
            // since the player won't transition state without our doing.
            _ = Application.Current?.Dispatcher.InvokeAsync(ScheduleStreamRetry);
        }
    }

    private void OnHubMotionDetected(
        SurveillanceHubService.MotionDetectedEvent e)
    {
        if (e.CameraId != CameraId)
        {
            return;
        }

        _ = Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            IsMotionDetected = e.IsMotionActive;
            AnalysisWidth = e.AnalysisWidth;
            AnalysisHeight = e.AnalysisHeight;

            if (e.IsMotionActive && e.BoundingBoxes.Count > 0)
            {
                CurrentBoundingBoxes = e.BoundingBoxes
                    .Select(b => new Rect(b.X, b.Y, b.Width, b.Height))
                    .ToList();
            }
            else
            {
                CurrentBoundingBoxes = [];
            }
        });
    }
}