namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// Background service that subscribes to Core service events and broadcasts them
/// to connected SignalR clients via the <see cref="SurveillanceHub"/>, and to the
/// readers of GET /events via the <see cref="SurveillanceEventFeed"/>: one mapping
/// (<see cref="HubEventMapping"/>) for both, so they say the same.
/// <br/><br/>
/// Built on <see cref="BackgroundServiceBase{T}"/> for consistency with the
/// other hosted workers (shared start/stop logging and health-running state),
/// even though the work itself is event-driven rather than periodic:
/// <see cref="DoWorkAsync"/> is a no-op and the base idle loop parks on a long
/// delay (see <see cref="SurveillanceEventBroadcasterOptions"/>). Subscription
/// happens in <see cref="StartAsync"/> and tear-down in <see cref="StopAsync"/>.
/// </summary>
public sealed partial class SurveillanceEventBroadcaster : BackgroundServiceBase<SurveillanceEventBroadcaster>
{
    // Bound the time any single broadcast can spend sending so a
    // slow/disconnected client cannot accumulate unbounded in-flight tasks
    // while events keep firing from the recording / motion services.
    private static readonly TimeSpan BroadcastTimeout = TimeSpan.FromSeconds(5);

    private readonly IHubContext<SurveillanceHub, ISurveillanceHubClient> hubContext;
    private readonly IRecordingService recordingService;
    private readonly ServerRecordingService serverRecordingService;
    private readonly IMotionDetectionService motionDetectionService;
    private readonly IUsbCameraLifecycleCoordinator usbLifecycle;
    private readonly SurveillanceEventFeed feed;

    public SurveillanceEventBroadcaster(
        ILogger<SurveillanceEventBroadcaster> logger,
        SurveillanceEventBroadcasterOptions options,
        IHubContext<SurveillanceHub, ISurveillanceHubClient> hubContext,
        IRecordingService recordingService,
        ServerRecordingService serverRecordingService,
        IMotionDetectionService motionDetectionService,
        IUsbCameraLifecycleCoordinator usbLifecycle,
        SurveillanceEventFeed feed)
        : base(logger, options)
    {
        this.hubContext = hubContext;
        this.recordingService = recordingService;
        this.serverRecordingService = serverRecordingService;
        this.motionDetectionService = motionDetectionService;
        this.usbLifecycle = usbLifecycle;
        this.feed = feed;
    }

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        recordingService.RecordingStateChanged += OnRecordingStateChanged;
        motionDetectionService.MotionDetected += OnMotionDetected;
        serverRecordingService.CameraConnectionStateChanged += OnCameraConnectionStateChanged;
        usbLifecycle.StateChanged += OnUsbLifecycleChanged;

        LogBroadcasterStarted();

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        recordingService.RecordingStateChanged -= OnRecordingStateChanged;
        motionDetectionService.MotionDetected -= OnMotionDetected;
        serverRecordingService.CameraConnectionStateChanged -= OnCameraConnectionStateChanged;
        usbLifecycle.StateChanged -= OnUsbLifecycleChanged;

        LogBroadcasterStopped();

        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task DoWorkAsync(CancellationToken stoppingToken)
        => Task.CompletedTask; // Event-driven: all work runs in the handlers wired up in StartAsync.

    // Sync event handlers that fire-and-forget a Task. async void in an event
    // handler means an unhandled synchronous-portion exception escapes onto
    // the firing thread (the demux thread for recording / motion events) —
    // crashing recording. Routing through a Task keeps any failure isolated
    // to the Task's faulted state, observable via the inner try/catch.
    private void OnRecordingStateChanged(
        object? sender,
        RecordingStateChangedEventArgs e)
        => _ = BroadcastRecordingStateChangedAsync(e);

    private void OnMotionDetected(
        object? sender,
        MotionDetectedEventArgs e)
        => _ = BroadcastMotionDetectedAsync(e);

    private void OnCameraConnectionStateChanged(
        Guid cameraId,
        ConnectionState newState)
        => _ = BroadcastCameraConnectionStateChangedAsync(cameraId, newState);

    private void OnUsbLifecycleChanged(
        object? sender,
        UsbCameraLifecycleChangedEventArgs e)
        => _ = BroadcastUsbLifecycleChangedAsync(e);

    private async Task BroadcastRecordingStateChangedAsync(
        RecordingStateChangedEventArgs e)
    {
        try
        {
            var hubEvent = e.ToHubEvent();
            feed.Publish(hubEvent.ToStreamEvent());
            await hubContext.Clients.All
                .RecordingStateChanged(hubEvent)
                .WaitAsync(BroadcastTimeout)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogBroadcastRecordingFailed(ex, e.CameraId);
        }
    }

    private async Task BroadcastCameraConnectionStateChangedAsync(
        Guid cameraId,
        ConnectionState newState)
    {
        try
        {
            var hubEvent = HubEventMapping.ToHubEvent(cameraId, newState, DateTimeOffset.UtcNow);
            feed.Publish(hubEvent.ToStreamEvent());
            await hubContext.Clients.All
                .ConnectionStateChanged(hubEvent)
                .WaitAsync(BroadcastTimeout)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogBroadcastConnectionFailed(ex, cameraId);
        }
    }

    private async Task BroadcastUsbLifecycleChangedAsync(
        UsbCameraLifecycleChangedEventArgs e)
    {
        try
        {
            var hubEvent = e.ToHubEvent(DateTimeOffset.UtcNow);
            feed.Publish(hubEvent.ToStreamEvent());
            await hubContext.Clients.All
                .UsbCameraLifecycleChanged(hubEvent)
                .WaitAsync(BroadcastTimeout)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogBroadcastUsbLifecycleFailed(ex, e.CameraId);
        }
    }

    private async Task BroadcastMotionDetectedAsync(MotionDetectedEventArgs e)
    {
        try
        {
            var hubEvent = e.ToHubEvent();
            feed.Publish(hubEvent.ToStreamEvent());
            await hubContext.Clients.All
                .MotionDetected(hubEvent)
                .WaitAsync(BroadcastTimeout)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogBroadcastMotionFailed(ex, e.CameraId);
        }
    }
}