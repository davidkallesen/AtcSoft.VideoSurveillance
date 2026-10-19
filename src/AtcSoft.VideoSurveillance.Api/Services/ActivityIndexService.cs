namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// Writes the activity index beside every recording the server makes, so a timeline can show
/// where in it something happened. While a camera records, its pictures are analysed (for the
/// index only, when the camera is not watched for motion) and every analysed second goes into
/// <c>{video}.activity.jsonl</c>. A new segment gets an index of its own.
/// </summary>
/// <remarks>
/// Event-driven, on <see cref="BackgroundServiceBase{T}"/> like the other hosted workers:
/// <see cref="DoWorkAsync"/> is a no-op.
/// </remarks>
public sealed partial class ActivityIndexService : BackgroundServiceBase<ActivityIndexService>
{
    private readonly ServerRecordingService recordingService;
    private readonly ServerMotionDetectionService motionDetectionService;
    private readonly ICameraStorageService storageService;
    private readonly IApplicationSettingsService settingsService;
    private readonly ConcurrentDictionary<Guid, IndexFile> files = new();

    public ActivityIndexService(
        ILogger<ActivityIndexService> logger,
        ActivityIndexServiceOptions options,
        ServerRecordingService recordingService,
        ServerMotionDetectionService motionDetectionService,
        ICameraStorageService storageService,
        IApplicationSettingsService settingsService)
        : base(logger, options)
    {
        this.recordingService = recordingService;
        this.motionDetectionService = motionDetectionService;
        this.storageService = storageService;
        this.settingsService = settingsService;
    }

    /// <summary>
    /// Gets the path of the index being written for a camera.
    /// </summary>
    /// <param name="cameraId">The camera.</param>
    /// <returns>The path, or <see langword="null"/> when none is being written.</returns>
    public string? GetIndexPath(Guid cameraId)
        => files.TryGetValue(cameraId, out var file) ? file.Path : null;

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        recordingService.RecordingStateChanged += OnRecordingStateChanged;
        motionDetectionService.PictureAnalyzed += OnPictureAnalyzed;

        // A recording that started before this service did.
        foreach (var session in recordingService.GetActiveSessions())
        {
            try
            {
                Open(session.CameraId, session.CurrentFilePath);
            }
            catch (Exception ex)
            {
                LogIndexFailed(ex, session.CameraId);
            }
        }

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        recordingService.RecordingStateChanged -= OnRecordingStateChanged;
        motionDetectionService.PictureAnalyzed -= OnPictureAnalyzed;

        foreach (var cameraId in files.Keys.ToList())
        {
            Close(cameraId);
        }

        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task DoWorkAsync(CancellationToken stoppingToken)
        => Task.CompletedTask; // Event-driven: all work runs in the handlers wired up in StartAsync.

    // The events fire on the threads that raise them (the demux thread, the detection loop):
    // a failure here is logged, never thrown into them.
    private void OnRecordingStateChanged(
        object? sender,
        RecordingStateChangedEventArgs e)
    {
        try
        {
            if (e.NewState == RecordingState.Idle)
            {
                Close(e.CameraId);

                // A recording that goes on in a new segment reports a stop too.
                if (!recordingService.IsRecording(e.CameraId))
                {
                    motionDetectionService.StopIndexing(e.CameraId);
                }
            }
            else if (e.FilePath is { Length: > 0 } filePath)
            {
                Open(e.CameraId, filePath);
            }
        }
        catch (Exception ex)
        {
            LogIndexFailed(ex, e.CameraId);
        }
    }

    private void OnPictureAnalyzed(
        object? sender,
        PictureAnalyzedEventArgs e)
    {
        if (!files.TryGetValue(e.CameraId, out var file))
        {
            return;
        }

        try
        {
            lock (file.Sync)
            {
                if (!file.IsClosed)
                {
                    file.Writer.Add(e.Time, e.Result);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // A full disk or a file taken away: this segment's index ends here, the recording goes on.
            LogIndexFailed(ex, e.CameraId);
            Close(e.CameraId);
        }
    }

    private void Open(
        Guid cameraId,
        string videoPath)
    {
        if (files.TryGetValue(cameraId, out var open) &&
            string.Equals(open.VideoPath, videoPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Close(cameraId);

        var start = recordingService.GetSession(cameraId) is { } session
            ? ToUtc(session.StartTime)
            : DateTimeOffset.UtcNow;
        var path = RecordingPathHelper.BuildActivityPath(videoPath);
        ActivityLogWriter? writer = null;
        try
        {
            writer = ActivityLogWriter.Create(path, start);
            files[cameraId] = new IndexFile(videoPath, path, writer);
            writer = null;
        }
        finally
        {
            writer?.Dispose();
        }

        if (storageService.GetCameraById(cameraId) is { } camera &&
            recordingService.FindPipeline(cameraId) is { } pipeline)
        {
            motionDetectionService.StartIndexing(
                cameraId,
                pipeline,
                MotionDetectionPolicy.Resolve(camera, settingsService.MotionDetection));
        }

        LogIndexStarted(cameraId, path);
    }

    private void Close(Guid cameraId)
    {
        if (!files.TryRemove(cameraId, out var file))
        {
            return;
        }

        try
        {
            lock (file.Sync)
            {
                file.IsClosed = true;
                file.Writer.Dispose();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogIndexFailed(ex, cameraId);
        }
    }

    private static DateTimeOffset ToUtc(DateTime time)
        => time.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(time)
            : new DateTimeOffset(time.ToUniversalTime());

    private sealed class IndexFile(
        string videoPath,
        string path,
        ActivityLogWriter writer)
    {
        public Lock Sync { get; } = new();

        public string VideoPath { get; } = videoPath;

        public string Path { get; } = path;

        public ActivityLogWriter Writer { get; } = writer;

        public bool IsClosed { get; set; }
    }
}