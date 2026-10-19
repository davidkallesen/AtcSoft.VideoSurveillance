namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// Server-side implementation of <see cref="IMotionDetectionService"/>. A few
/// times a second it takes a picture from the camera's pipeline, compares it
/// with the one before (<see cref="MotionFrameAnalyzer"/>, shared with the
/// desktop app) and reports what moved.
/// </summary>
/// <remarks>
/// A motion lasts from the first picture in which something moved until the
/// scene has been quiet for the post-motion time. While it lasts, every
/// picture with movement raises <see cref="MotionDetected"/> with the boxes
/// (for live overlays); its end raises the event once with
/// <see cref="MotionDetectedEventArgs.IsMotionActive"/> false.
/// </remarks>
public sealed partial class ServerMotionDetectionService : IMotionDetectionService, IDisposable
{
    // A picture is captured as a PNG and decoded again, which costs far more
    // than the comparison. Four a second catches a person walking through
    // the scene and keeps a server with many cameras usable.
    private const int MaxAnalysesPerSecond = 4;

    private const int DefaultAnalysisWidth = 320;
    private const int DefaultAnalysisHeight = 240;

    private readonly ILogger<ServerMotionDetectionService> logger;
    private readonly IMotionFrameReader? frameReader;
    private readonly TimeProvider timeProvider;
    private readonly bool runLoops;
    private readonly Func<MotionDetectionSettings, IMotionDetector> createDetector;
    private readonly ConcurrentDictionary<Guid, DetectionContext> contexts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerMotionDetectionService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="frameReader">
    /// Turns a captured picture into a grayscale one. Optional: a host that
    /// cannot read pictures (not Windows) runs without motion detection.
    /// </param>
    /// <param name="timeProvider">The clock; the system's when not given.</param>
    /// <param name="runLoops">
    /// Whether a started detection analyses pictures on its own. Tests turn
    /// it off and call <see cref="AnalyzeOnceAsync"/> themselves.
    /// </param>
    /// <param name="createDetector">Makes a camera's detector; the built-in one when not given.</param>
    public ServerMotionDetectionService(
        ILogger<ServerMotionDetectionService> logger,
        IMotionFrameReader? frameReader = null,
        TimeProvider? timeProvider = null,
        bool runLoops = true,
        Func<MotionDetectionSettings, IMotionDetector>? createDetector = null)
    {
        this.createDetector = createDetector ?? (s => new BuiltInMotionDetector(s));
        this.logger = logger;
        this.frameReader = frameReader;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.runLoops = runLoops;
    }

    /// <inheritdoc/>
    public event EventHandler<MotionDetectedEventArgs>? MotionDetected;

    /// <summary>
    /// Gets the time between two analysed pictures for the given settings.
    /// </summary>
    /// <param name="settings">The camera's motion settings.</param>
    /// <returns>The interval.</returns>
    public static TimeSpan GetInterval(MotionDetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var perSecond = Math.Clamp(settings.AnalysisFrameRate, 1, MaxAnalysesPerSecond);
        return TimeSpan.FromMilliseconds(1000.0 / perSecond);
    }

    /// <inheritdoc/>
    public void StartDetection(
        Guid cameraId,
        IMediaPipeline pipeline,
        MotionDetectionSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        if (frameReader is null)
        {
            LogMotionDetectionNotAvailable(cameraId);
            return;
        }

        if (contexts.ContainsKey(cameraId))
        {
            return;
        }

        CancellationTokenSource? cts = null;
        try
        {
            cts = new CancellationTokenSource();
            var cameraSettings = settings ?? new MotionDetectionSettings();
            var context = new DetectionContext(pipeline, cameraSettings, createDetector(cameraSettings), cts);

            if (contexts.TryAdd(cameraId, context))
            {
                cts = null;
                if (runLoops)
                {
                    _ = RunDetectionLoopAsync(cameraId, context);
                }

                LogMotionDetectionStarted(cameraId);
            }
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <inheritdoc/>
    public void StopDetection(Guid cameraId)
    {
        if (!contexts.TryRemove(cameraId, out var context))
        {
            return;
        }

        context.Cts.Cancel();
        context.Cts.Dispose();

        // Whoever acted on the motion (a recording) has to hear that it is over.
        bool wasActive;
        lock (context.Sync)
        {
            wasActive = context.IsMotionActive;
            context.IsMotionActive = false;
            context.LastBoundingBoxes = [];
        }

        if (wasActive)
        {
            RaiseMotionEnded(cameraId, context);
        }

        LogMotionDetectionStopped(cameraId);
    }

    /// <inheritdoc/>
    public bool IsDetectionActive(Guid cameraId)
        => contexts.ContainsKey(cameraId);

    /// <inheritdoc/>
    public bool IsMotionDetected(Guid cameraId)
        => contexts.TryGetValue(cameraId, out var ctx) && ctx.IsMotionActive;

    /// <inheritdoc/>
    public IReadOnlyList<BoundingBox> GetLastBoundingBoxes(Guid cameraId)
        => contexts.TryGetValue(cameraId, out var ctx)
            ? ctx.LastBoundingBoxes
            : [];

    /// <inheritdoc/>
    public (int Width, int Height) GetAnalysisResolution(Guid cameraId)
        => contexts.TryGetValue(cameraId, out var ctx)
            ? GetAnalysisSize(ctx.Settings)
            : (DefaultAnalysisWidth, DefaultAnalysisHeight);

    /// <summary>
    /// Takes one picture from the camera and compares it with the one before.
    /// </summary>
    /// <param name="cameraId">The camera.</param>
    /// <param name="cancellationToken">Cancels the capture.</param>
    /// <returns>A task that completes when the picture has been analysed.</returns>
    public async Task AnalyzeOnceAsync(
        Guid cameraId,
        CancellationToken cancellationToken = default)
    {
        if (frameReader is null || !contexts.TryGetValue(cameraId, out var context))
        {
            return;
        }

        byte[]? current;
        var (width, height) = GetAnalysisSize(context.Settings);
        try
        {
            var picture = await context.Pipeline.CaptureFrameAsync(cancellationToken).ConfigureAwait(false);
            current = picture is { Length: > 0 }
                ? frameReader.ReadGrayscale(picture, width, height)
                : null;
            context.ConsecutiveFailures = 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            // Whoever owns the pipeline closed it. There will be no more
            // pictures, so the detection ends, and any motion with it.
            LogPipelineClosed(cameraId);
            StopDetection(cameraId);
            return;
        }
        catch (Exception ex)
        {
            // A camera that dropped out is the pipeline owner's business;
            // here it only means there is no picture to look at.
            context.ConsecutiveFailures++;
            if (context.ConsecutiveFailures == 1 || context.ConsecutiveFailures % 240 == 0)
            {
                LogFrameCaptureFailed(ex, cameraId, context.ConsecutiveFailures);
            }

            current = null;
        }

        Analyze(cameraId, context, current, width, height);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var cameraId in contexts.Keys.ToList())
        {
            StopDetection(cameraId);
        }
    }

    private static (int Width, int Height) GetAnalysisSize(
        MotionDetectionSettings settings)
        => (settings.AnalysisWidth > 0 ? settings.AnalysisWidth : DefaultAnalysisWidth,
            settings.AnalysisHeight > 0 ? settings.AnalysisHeight : DefaultAnalysisHeight);

    private void Analyze(
        Guid cameraId,
        DetectionContext context,
        byte[]? current,
        int width,
        int height)
    {
        MotionDetectedEventArgs? toRaise = null;
        var now = timeProvider.GetUtcNow();

        lock (context.Sync)
        {
            // Stopped while the picture was being captured.
            if (!contexts.ContainsKey(cameraId))
            {
                return;
            }

            // The detector is shared with the desktop: the comparison, the gate and the hold.
            // Without a picture, time still passes, so motion can end.
            var result = current is not null
                ? context.Detector.Process(new MotionFrame(current, width, height, now))
                : context.Detector.Idle(now);

            if (result.Change is MotionHoldChange.Started or MotionHoldChange.Continues)
            {
                context.IsMotionActive = true;
                context.LastMotionAt = now;
                context.LastBoundingBoxes = result.Boxes;
                toRaise = new MotionDetectedEventArgs(
                    cameraId,
                    result.ChangePercent,
                    isMotionActive: true,
                    result.Boxes,
                    width,
                    height);
            }
            else if (result.Change == MotionHoldChange.Ended)
            {
                context.IsMotionActive = false;
                context.LastBoundingBoxes = [];
                toRaise = new MotionDetectedEventArgs(
                    cameraId,
                    result.ChangePercent,
                    isMotionActive: false,
                    boundingBoxes: null,
                    width,
                    height);
            }
        }

        if (toRaise is not null)
        {
            Raise(cameraId, toRaise);
        }
    }

    private void RaiseMotionEnded(
        Guid cameraId,
        DetectionContext context)
    {
        var (width, height) = GetAnalysisSize(context.Settings);
        Raise(cameraId, new MotionDetectedEventArgs(cameraId, 0, isMotionActive: false, boundingBoxes: null, width, height));
    }

    // A subscriber that throws must not end the detection of the camera.
    private void Raise(
        Guid cameraId,
        MotionDetectedEventArgs args)
    {
        try
        {
            MotionDetected?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            LogMotionSubscriberFailed(ex, cameraId);
        }
    }

    private async Task RunDetectionLoopAsync(
        Guid cameraId,
        DetectionContext context)
    {
        try
        {
            var token = context.Cts.Token;
            using var timer = new PeriodicTimer(GetInterval(context.Settings), timeProvider);

            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                await AnalyzeOnceAsync(cameraId, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping detection
        }
        catch (ObjectDisposedException)
        {
            // The detection was stopped between two pictures.
        }
        catch (Exception ex)
        {
            LogMotionDetectionLoopError(ex, cameraId);
        }
    }

    private sealed class DetectionContext(
        IMediaPipeline pipeline,
        MotionDetectionSettings settings,
        IMotionDetector detector,
        CancellationTokenSource cts)
    {
        public Lock Sync { get; } = new();

        public IMediaPipeline Pipeline { get; } = pipeline;

        public MotionDetectionSettings Settings { get; } = settings;

        public CancellationTokenSource Cts { get; } = cts;

        public bool IsMotionActive { get; set; }

        // The same detector as the desktop's, behind the shared seam.
        public IMotionDetector Detector { get; } = detector;

        public DateTimeOffset LastMotionAt { get; set; }

        public int ConsecutiveFailures { get; set; }

        public IReadOnlyList<BoundingBox> LastBoundingBoxes { get; set; } = [];
    }
}