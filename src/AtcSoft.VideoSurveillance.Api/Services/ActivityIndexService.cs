namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// Runs the <see cref="ActivityIndexRecorder"/> on the server: an activity index beside every
/// recording, with each camera's own motion settings.
/// </summary>
/// <remarks>
/// Event-driven, on <see cref="BackgroundServiceBase{T}"/> like the other hosted workers:
/// <see cref="DoWorkAsync"/> is a no-op.
/// </remarks>
public sealed class ActivityIndexService : BackgroundServiceBase<ActivityIndexService>
{
    private readonly ActivityIndexRecorder recorder;

    public ActivityIndexService(
        ILogger<ActivityIndexService> logger,
        ActivityIndexServiceOptions options,
        IRecordingService recordingService,
        IMotionDetectionService motionDetectionService,
        ICameraStorageService storageService,
        IApplicationSettingsService settingsService)
        : base(logger, options)
    {
        ArgumentNullException.ThrowIfNull(storageService);
        ArgumentNullException.ThrowIfNull(settingsService);

        recorder = new ActivityIndexRecorder(
            logger,
            recordingService,
            motionDetectionService,
            cameraId => storageService.GetCameraById(cameraId) is { } camera
                ? MotionDetectionPolicy.Resolve(camera, settingsService.MotionDetection)
                : null);
    }

    /// <summary>
    /// Gets the path of the index being written for a camera.
    /// </summary>
    /// <param name="cameraId">The camera.</param>
    /// <returns>The path, or <see langword="null"/> when none is being written.</returns>
    public string? GetIndexPath(Guid cameraId)
        => recorder.GetIndexPath(cameraId);

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        recorder.Start();
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        recorder.Stop();
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task DoWorkAsync(CancellationToken stoppingToken)
        => Task.CompletedTask; // Event-driven: the recorder works in the handlers it subscribes in Start.

    /// <inheritdoc />
    public override void Dispose()
    {
        recorder.Dispose();
        base.Dispose();
    }
}