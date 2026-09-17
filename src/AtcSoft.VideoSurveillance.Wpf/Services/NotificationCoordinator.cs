namespace Linksoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// Subscribes to all SignalR hub events and dispatches toast notifications
/// based on user preferences. Maintains a notification history log.
/// </summary>
public sealed class NotificationCoordinator : IDisposable
{
    private const int MaxHistoryEntries = 100;
    private static readonly TimeSpan MotionRateLimitInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ToastExpiration = TimeSpan.FromSeconds(5);

    private readonly SurveillanceHubService hubService;
    private readonly IToastNotificationService toastNotificationService;
    private readonly ConcurrentDictionary<Guid, string> cameraNameCache = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> lastMotionNotification = new();

    private bool isStarted;
    private bool isHubDisconnected;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationCoordinator"/> class.
    /// </summary>
    public NotificationCoordinator(
        SurveillanceHubService hubService,
        IToastNotificationService toastNotificationService)
    {
        ArgumentNullException.ThrowIfNull(hubService);
        ArgumentNullException.ThrowIfNull(toastNotificationService);

        this.hubService = hubService;
        this.toastNotificationService = toastNotificationService;
    }

    /// <summary>
    /// Gets the notification history collection (UI-bindable).
    /// </summary>
    public ObservableCollection<NotificationEntry> History { get; } = [];

    /// <summary>
    /// Gets or sets the notification preferences.
    /// </summary>
    public NotificationPreferences Preferences { get; set; } = new();

    /// <summary>
    /// Pre-populates the camera name cache from an API result.
    /// </summary>
    public void UpdateCameraNameCache(IEnumerable<Camera>? cameras)
    {
        if (cameras is null)
        {
            return;
        }

        foreach (var camera in cameras)
        {
            cameraNameCache[camera.Id] = camera.DisplayName;
        }
    }

    /// <summary>
    /// Subscribes to hub events. Call after <c>mainWindow.Show()</c> to ensure the visual tree is ready.
    /// </summary>
    public void Start()
    {
        if (isStarted)
        {
            return;
        }

        isStarted = true;

        hubService.OnConnectionStateChanged += OnConnectionStateChanged;
        hubService.OnRecordingStateChanged += OnRecordingStateChanged;
        hubService.OnMotionDetected += OnMotionDetected;
        hubService.OnHubConnectionStateChanged += OnHubConnectionStateChanged;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!isStarted)
        {
            return;
        }

        hubService.OnConnectionStateChanged -= OnConnectionStateChanged;
        hubService.OnRecordingStateChanged -= OnRecordingStateChanged;
        hubService.OnMotionDetected -= OnMotionDetected;
        hubService.OnHubConnectionStateChanged -= OnHubConnectionStateChanged;

        isStarted = false;
    }

    private void OnHubConnectionStateChanged(string state)
    {
        if (string.Equals(state, "Disconnected", StringComparison.OrdinalIgnoreCase) && !isHubDisconnected)
        {
            isHubDisconnected = true;
            ShowNotification(
                "Server Connection Lost",
                "The connection to the surveillance server has been lost.",
                NotificationEventType.CameraDisconnected);
        }
        else if (string.Equals(state, "Connected", StringComparison.OrdinalIgnoreCase) && isHubDisconnected)
        {
            isHubDisconnected = false;
            ShowNotification(
                "Server Connected",
                "The connection to the surveillance server has been restored.",
                NotificationEventType.CameraReconnected);
        }
    }

    private void OnConnectionStateChanged(
        SurveillanceHubService.ConnectionStateEvent e)
    {
        var cameraName = GetCameraName(e.CameraId);

        if (string.Equals(e.NewState, "disconnected", StringComparison.OrdinalIgnoreCase) &&
            Preferences.NotifyOnDisconnect)
        {
            ShowNotification(
                cameraName,
                "Camera disconnected",
                NotificationEventType.CameraDisconnected);
        }
        else if (string.Equals(e.NewState, "connected", StringComparison.OrdinalIgnoreCase) &&
                 Preferences.NotifyOnReconnect)
        {
            ShowNotification(
                cameraName,
                "Camera connected",
                NotificationEventType.CameraReconnected);
        }
    }

    private void OnRecordingStateChanged(
        SurveillanceHubService.RecordingStateEvent e)
    {
        var cameraName = GetCameraName(e.CameraId);

        var isNowRecording =
            string.Equals(e.NewState, "recording", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.NewState, "recordingMotion", StringComparison.OrdinalIgnoreCase);

        var wasRecording =
            string.Equals(e.OldState, "recording", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.OldState, "recordingMotion", StringComparison.OrdinalIgnoreCase);

        if (isNowRecording && Preferences.NotifyOnRecordingStarted)
        {
            ShowNotification(
                cameraName,
                "Recording started",
                NotificationEventType.RecordingStarted);
        }
        else if (!isNowRecording && wasRecording && Preferences.NotifyOnRecordingStopped)
        {
            ShowNotification(
                cameraName,
                "Recording stopped",
                NotificationEventType.RecordingStopped);
        }
    }

    private void OnMotionDetected(SurveillanceHubService.MotionDetectedEvent e)
    {
        if (!e.IsMotionActive || !Preferences.NotifyOnMotionDetected)
        {
            return;
        }

        // Rate-limit: max 1 per camera per 10 seconds
        var now = DateTimeOffset.UtcNow;
        if (lastMotionNotification.TryGetValue(e.CameraId, out var lastTime) &&
            now - lastTime < MotionRateLimitInterval)
        {
            return;
        }

        lastMotionNotification[e.CameraId] = now;

        var cameraName = GetCameraName(e.CameraId);
        ShowNotification(
            cameraName,
            $"Motion detected ({e.ChangePercentage:F1}%)",
            NotificationEventType.MotionDetected);
    }

    private void ShowNotification(
        string title,
        string message,
        NotificationEventType eventType)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            // Add to history
            var entry = new NotificationEntry(DateTimeOffset.Now, title, message, eventType);
            History.Insert(0, entry);

            while (History.Count > MaxHistoryEntries)
            {
                History.RemoveAt(History.Count - 1);
            }

            // Show toast
            toastNotificationService.ShowInformation(
                title,
                message,
                useDesktop: true,
                expirationTime: ToastExpiration);

            // Play sound if enabled
            if (Preferences.PlaySound)
            {
                SystemSounds.Exclamation.Play();
            }
        });
    }

    private string GetCameraName(Guid cameraId)
        => cameraNameCache.TryGetValue(cameraId, out var name)
            ? name
            : $"Camera {cameraId.ToString()[..8]}";
}