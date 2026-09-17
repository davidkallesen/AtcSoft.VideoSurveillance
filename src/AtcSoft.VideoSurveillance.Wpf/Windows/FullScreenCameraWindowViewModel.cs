// ReSharper disable GCSuppressFinalizeForTypeWithoutDestructor
using ConnectionState = Atc.Network.ConnectionState;

namespace Linksoft.VideoSurveillance.Wpf.Windows;

/// <summary>
/// ViewModel for the fullscreen camera window.
/// Simplified version — no direct RTSP connection or local motion detection.
/// The player is borrowed from the CameraTile (not owned by this VM).
/// </summary>
public sealed partial class FullScreenCameraWindowViewModel : ViewModelBase, IDisposable
{
    private readonly SurveillanceHubService hubService;
    private readonly Guid cameraId;
    private DispatcherTimer? overlayHideTimer;
    private bool disposed;

    [ObservableProperty]
    private IVideoPlayer? player;

    [ObservableProperty]
    private string cameraName = string.Empty;

    [ObservableProperty]
    private string cameraDescription = string.Empty;

    [ObservableProperty]
    private ConnectionState connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private bool isOverlayVisible = true;

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

    public FullScreenCameraWindowViewModel(
        SurveillanceHubService hubService,
        Guid cameraId,
        IVideoPlayer? existingPlayer,
        string cameraName,
        string cameraDescription)
    {
        ArgumentNullException.ThrowIfNull(hubService);

        this.hubService = hubService;
        this.cameraId = cameraId;
        CameraName = cameraName;
        CameraDescription = cameraDescription;

        // Borrow existing player from tile (we do NOT own it)
        if (existingPlayer is not null)
        {
            Player = existingPlayer;
            Player.StateChanged += OnPlayerStateChanged;

            ConnectionState = existingPlayer.State switch
            {
                PlayerState.Playing => ConnectionState.Connected,
                PlayerState.Opening => ConnectionState.Connecting,
                PlayerState.Stopped => ConnectionState.Disconnected,
                PlayerState.Error => ConnectionState.ConnectionFailed,
                _ => ConnectionState.Disconnected,
            };
        }

        hubService.OnConnectionStateChanged += OnHubConnectionStateChanged;
        hubService.OnRecordingStateChanged += OnHubRecordingStateChanged;
        hubService.OnMotionDetected += OnHubMotionDetected;

        StartOverlayHideTimer();
    }

    /// <summary>
    /// Occurs when the window requests to be closed.
    /// </summary>
    public event EventHandler<DialogClosedEventArgs>? CloseRequested;

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

    /// <summary>
    /// Called when the mouse moves in the window.
    /// </summary>
    public void OnMouseMoved()
    {
        IsOverlayVisible = true;
        overlayHideTimer?.Stop();
        overlayHideTimer?.Start();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, new DialogClosedEventArgs(dialogResult: true));
    }

    private void OnPlayerStateChanged(
        object? sender,
        PlayerStateChangedEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ConnectionState = e.NewState switch
            {
                PlayerState.Playing => ConnectionState.Connected,
                PlayerState.Opening => ConnectionState.Connecting,
                PlayerState.Stopped => ConnectionState.Disconnected,
                PlayerState.Error => ConnectionState.ConnectionFailed,
                _ => ConnectionState.Disconnected,
            };
        });
    }

    private void OnHubConnectionStateChanged(
        SurveillanceHubService.ConnectionStateEvent e)
    {
        if (e.CameraId != cameraId)
        {
            return;
        }

        Application.Current?.Dispatcher.Invoke(() =>
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
        if (e.CameraId != cameraId)
        {
            return;
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            IsRecording = string.Equals(e.NewState, "recording", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(e.NewState, "recordingMotion", StringComparison.OrdinalIgnoreCase);
        });
    }

    private void OnHubMotionDetected(
        SurveillanceHubService.MotionDetectedEvent e)
    {
        if (e.CameraId != cameraId)
        {
            return;
        }

        Application.Current?.Dispatcher.Invoke(() =>
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

    private void StartOverlayHideTimer()
    {
        overlayHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3),
        };

        overlayHideTimer.Tick += (_, _) =>
        {
            IsOverlayVisible = false;
            overlayHideTimer.Stop();
        };

        overlayHideTimer.Start();
    }

    private void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
            overlayHideTimer?.Stop();
            overlayHideTimer = null;

            if (recordingTimer is not null)
            {
                recordingTimer.Stop();
                recordingTimer.Tick -= OnRecordingTimerTick;
                recordingTimer = null;
            }

            hubService.OnConnectionStateChanged -= OnHubConnectionStateChanged;
            hubService.OnRecordingStateChanged -= OnHubRecordingStateChanged;
            hubService.OnMotionDetected -= OnHubMotionDetected;

            if (Player is not null)
            {
                Player.StateChanged -= OnPlayerStateChanged;

                // Do NOT dispose the player — it is borrowed from the tile
                Player = null;
            }
        }

        disposed = true;
    }
}