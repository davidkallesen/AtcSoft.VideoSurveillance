namespace Linksoft.VideoSurveillance.Api.Services;

/// <summary>
/// Background service that subscribes to Core service events and broadcasts them
/// to connected SignalR clients via the <see cref="SurveillanceHub"/>.
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
    // Bound the time any single broadcast can spend in SendAsync so a
    // slow/disconnected client cannot accumulate unbounded in-flight tasks
    // while events keep firing from the recording / motion services.
    private static readonly TimeSpan BroadcastTimeout = TimeSpan.FromSeconds(5);

    private readonly IHubContext<SurveillanceHub> hubContext;
    private readonly IRecordingService recordingService;
    private readonly ServerRecordingService serverRecordingService;
    private readonly IMotionDetectionService motionDetectionService;
    private readonly IUsbCameraLifecycleCoordinator usbLifecycle;

    public SurveillanceEventBroadcaster(
        ILogger<SurveillanceEventBroadcaster> logger,
        SurveillanceEventBroadcasterOptions options,
        IHubContext<SurveillanceHub> hubContext,
        IRecordingService recordingService,
        ServerRecordingService serverRecordingService,
        IMotionDetectionService motionDetectionService,
        IUsbCameraLifecycleCoordinator usbLifecycle)
        : base(logger, options)
    {
        this.hubContext = hubContext;
        this.recordingService = recordingService;
        this.serverRecordingService = serverRecordingService;
        this.motionDetectionService = motionDetectionService;
        this.usbLifecycle = usbLifecycle;
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
            using var cts = new CancellationTokenSource(BroadcastTimeout);
            await hubContext.Clients.All
                .SendAsync(
                    "RecordingStateChanged",
                    new
                    {
                        e.CameraId,
                        NewState = e.NewState.ToString(),
                        OldState = e.OldState.ToString(),
                        e.FilePath,
                        e.Timestamp,
                    },
                    cts.Token)
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
            using var cts = new CancellationTokenSource(BroadcastTimeout);
            await hubContext.Clients.All
                .SendAsync(
                    "ConnectionStateChanged",
                    new
                    {
                        CameraId = cameraId,
                        NewState = newState.ToString(),
                        Timestamp = DateTimeOffset.UtcNow,
                    },
                    cts.Token)
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
            using var cts = new CancellationTokenSource(BroadcastTimeout);
            await hubContext.Clients.All
                .SendAsync(
                    "UsbCameraLifecycleChanged",
                    new
                    {
                        e.CameraId,
                        Phase = e.Phase.ToString(),
                        e.Device.DeviceId,
                        e.Device.FriendlyName,
                        Timestamp = DateTimeOffset.UtcNow,
                    },
                    cts.Token)
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
            using var cts = new CancellationTokenSource(BroadcastTimeout);
            await hubContext.Clients.All
                .SendAsync(
                    "MotionDetected",
                    new
                    {
                        e.CameraId,
                        e.IsMotionActive,
                        e.ChangePercentage,
                        BoundingBoxes = e.BoundingBoxes.Select(b => new
                        {
                            b.X,
                            b.Y,
                            b.Width,
                            b.Height,
                        }),
                        e.AnalysisWidth,
                        e.AnalysisHeight,
                        e.Timestamp,
                    },
                    cts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogBroadcastMotionFailed(ex, e.CameraId);
        }
    }
}