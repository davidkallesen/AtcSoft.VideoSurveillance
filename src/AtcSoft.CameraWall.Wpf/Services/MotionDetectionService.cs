// ReSharper disable RedundantArgumentDefaultValue
namespace AtcSoft.CameraWall.Wpf.Services;

/// <summary>
/// Service for detecting motion in camera video streams using frame differencing.
/// Uses a staggered scheduler to analyze cameras in sequence, preventing CPU spikes.
/// A camera that records is also analysed for its activity index (<see cref="StartIndexing"/>);
/// one analysed only for the index reports no motion.
/// </summary>
[Registration(Lifetime.Singleton)]
public partial class MotionDetectionService : IMotionDetectionService, IDisposable
{
    private const int DefaultAnalysisWidth = 800;
    private const int DefaultAnalysisHeight = 600;
    private const int MinTargetFps = 2;
    private const int MaxTargetFps = 15;
    private const int MinSchedulerIntervalMs = 16; // ~60 Hz ceiling for scheduler ticks
    private const int MaxSchedulerIntervalMs = 500; // Maximum 500ms between analyses

    private readonly ConcurrentDictionary<Guid, MotionDetectionContext> contexts = new();
    private readonly List<Guid> scheduledCameras = [];
    private readonly Lock schedulerLock = new();
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly ILogger<MotionDetectionService> logger;
    private readonly IMotionFrameReader frameReader;
    private DispatcherTimer? schedulerTimer;
    private int currentCameraIndex;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MotionDetectionService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="frameReader">Reads a captured picture as grey at the analysis size; the same reader as the server's.</param>
    public MotionDetectionService(
        ILogger<MotionDetectionService> logger,
        IMotionFrameReader frameReader)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.frameReader = frameReader ?? throw new ArgumentNullException(nameof(frameReader));
    }

    /// <inheritdoc/>
    public event EventHandler<MotionDetectedEventArgs>? MotionDetected;

    /// <inheritdoc/>
    public event EventHandler<PictureAnalyzedEventArgs>? PictureAnalyzed;

    /// <inheritdoc/>
    public void StartDetection(
        Guid cameraId,
        IMediaPipeline pipeline,
        MotionDetectionSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        // Started again with new settings: begin anew, still feeding the index if it did.
        var indexes = contexts.TryGetValue(cameraId, out var running) && running.Indexes;
        Remove(cameraId);
        Add(cameraId, pipeline, settings, reportsMotion: true, indexes);
    }

    /// <inheritdoc/>
    /// <remarks>A camera that is also analysed for its activity index goes on being analysed, without reporting motion.</remarks>
    public void StopDetection(Guid cameraId)
    {
        if (contexts.TryGetValue(cameraId, out var context) && context.Indexes)
        {
            context.ReportsMotion = false;
            context.IsMotionDetected = false;
            context.LastBoundingBoxes = [];
            return;
        }

        Remove(cameraId);
    }

    /// <inheritdoc/>
    public void StartIndexing(
        Guid cameraId,
        IMediaPipeline pipeline,
        MotionDetectionSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        if (contexts.TryGetValue(cameraId, out var running))
        {
            running.Indexes = true;
            return;
        }

        Add(cameraId, pipeline, settings, reportsMotion: false, indexes: true);
    }

    /// <inheritdoc/>
    public void StopIndexing(Guid cameraId)
    {
        if (!contexts.TryGetValue(cameraId, out var context))
        {
            return;
        }

        context.Indexes = false;
        if (!context.ReportsMotion)
        {
            Remove(cameraId);
        }
    }

    /// <inheritdoc/>
    public bool IsIndexing(Guid cameraId)
        => contexts.TryGetValue(cameraId, out var context) && context.Indexes;

    /// <inheritdoc/>
    public bool IsDetectionActive(Guid cameraId)
        => contexts.TryGetValue(cameraId, out var context) && context.ReportsMotion;

    /// <inheritdoc/>
    public bool IsMotionDetected(Guid cameraId)
    {
        if (contexts.TryGetValue(cameraId, out var context))
        {
            return context.IsMotionDetected;
        }

        return false;
    }

    /// <inheritdoc/>
    public IReadOnlyList<BoundingBox> GetLastBoundingBoxes(Guid cameraId)
        => contexts.TryGetValue(cameraId, out var context)
            ? context.LastBoundingBoxes
            : [];

    /// <inheritdoc/>
    public (int Width, int Height) GetAnalysisResolution(Guid cameraId)
    {
        if (contexts.TryGetValue(cameraId, out var context))
        {
            var width = context.Settings.AnalysisWidth > 0 ? context.Settings.AnalysisWidth : DefaultAnalysisWidth;
            var height = context.Settings.AnalysisHeight > 0 ? context.Settings.AnalysisHeight : DefaultAnalysisHeight;
            return (width, height);
        }

        return (DefaultAnalysisWidth, DefaultAnalysisHeight);
    }

    /// <summary>
    /// Disposes of the service resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        if (disposing)
        {
            // Stop scheduler
            lock (schedulerLock)
            {
                schedulerTimer?.Stop();
                schedulerTimer = null;
                scheduledCameras.Clear();
            }

            foreach (var cameraId in contexts.Keys.ToList())
            {
                Remove(cameraId);
            }
        }

        disposed = true;
    }

    private void Add(
        Guid cameraId,
        IMediaPipeline pipeline,
        MotionDetectionSettings? settings,
        bool reportsMotion,
        bool indexes)
    {
        var context = new MotionDetectionContext(cameraId, pipeline, settings ?? new MotionDetectionSettings())
        {
            ReportsMotion = reportsMotion,
            Indexes = indexes,
        };

        // Mutate contexts and scheduledCameras together under the same lock so
        // a concurrent Start/Stop pair cannot leave a camera scheduled but
        // missing from contexts (which would silently stop analysis for it).
        bool added;
        lock (schedulerLock)
        {
            added = contexts.TryAdd(cameraId, context);
            if (added)
            {
                scheduledCameras.Add(cameraId);
                UpdateSchedulerInterval();
            }
        }

        if (!added)
        {
            context.Dispose();
        }
    }

    private void Remove(Guid cameraId)
    {
        // Same atomic invariant as Add: both maps move together.
        MotionDetectionContext? context;
        lock (schedulerLock)
        {
            contexts.TryRemove(cameraId, out context);
            scheduledCameras.Remove(cameraId);
            UpdateSchedulerInterval();
        }

        context?.Dispose();
    }

    /// <summary>
    /// Updates the scheduler interval based on the number of cameras.
    /// Spreads analysis evenly across time to prevent CPU spikes.
    /// </summary>
    private void UpdateSchedulerInterval()
    {
        // Must be called within schedulerLock
        if (scheduledCameras.Count == 0)
        {
            schedulerTimer?.Stop();
            schedulerTimer = null;
            return;
        }

        // Compute max AnalysisFrameRate across all active cameras (clamped to MinTargetFps..MaxTargetFps)
        var maxFps = MinTargetFps;
        foreach (var id in scheduledCameras)
        {
            if (contexts.TryGetValue(id, out var ctx))
            {
                var fps = Math.Clamp(ctx.Settings.AnalysisFrameRate, MinTargetFps, MaxTargetFps);
                if (fps > maxFps)
                {
                    maxFps = fps;
                }
            }
        }

        // Calculate interval: total analyses per second = cameras * maxFps
        // Interval = 1000ms / totalAnalysesPerSecond
        var totalAnalysesPerSecond = scheduledCameras.Count * maxFps;
        var intervalMs = Math.Max(MinSchedulerIntervalMs, Math.Min(MaxSchedulerIntervalMs, 1000.0 / totalAnalysesPerSecond));

        if (schedulerTimer is null)
        {
            schedulerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(intervalMs),
            };

            schedulerTimer.Tick += OnSchedulerTick;
            schedulerTimer.Start();
        }
        else
        {
            schedulerTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        }
    }

    /// <summary>
    /// Scheduler tick handler - captures a snapshot on the UI thread, then
    /// dispatches heavy image processing to a background thread.
    /// </summary>
    private void OnSchedulerTick(
        object? sender,
        EventArgs e)
    {
        Guid cameraId;

        lock (schedulerLock)
        {
            if (scheduledCameras.Count == 0)
            {
                return;
            }

            // Get next camera in round-robin fashion
            currentCameraIndex %= scheduledCameras.Count;
            cameraId = scheduledCameras[currentCameraIndex];
            currentCameraIndex++;
        }

        if (!contexts.TryGetValue(cameraId, out var context))
        {
            return;
        }

        // Skip if the previous frame is still being analyzed
        if (context.IsAnalyzing)
        {
            return;
        }

        context.IsAnalyzing = true;
        _ = CaptureAndProcessAsync(cameraId, context);
    }

    private async Task CaptureAndProcessAsync(
        Guid cameraId,
        MotionDetectionContext context)
    {
        try
        {
            var frameBytes = await context.Pipeline.CaptureFrameAsync().ConfigureAwait(false);
            if (frameBytes is null || frameBytes.Length == 0)
            {
                return;
            }

            ProcessCapturedFrame(cameraId, frameBytes);
            context.ConsecutiveFails = 0;
        }
        catch (Exception ex)
        {
            context.ConsecutiveFails++;
            if (context.ConsecutiveFails == 1 || context.ConsecutiveFails % 60 == 0)
            {
                LogFrameCaptureFailed(ex, cameraId, context.ConsecutiveFails);
            }
        }
        finally
        {
            context.IsAnalyzing = false;
        }
    }

    private void ProcessCapturedFrame(
        Guid cameraId,
        byte[] frameBytes)
    {
        if (!contexts.TryGetValue(cameraId, out var context))
        {
            return;
        }

        try
        {
            // Get analysis resolution from settings (or use defaults)
            var analysisWidth = context.Settings.AnalysisWidth > 0 ? context.Settings.AnalysisWidth : DefaultAnalysisWidth;
            var analysisHeight = context.Settings.AnalysisHeight > 0 ? context.Settings.AnalysisHeight : DefaultAnalysisHeight;

            // Grey and downscaled by the same reader the server uses.
            var currentFrame = frameReader.ReadGrayscale(frameBytes, analysisWidth, analysisHeight);
            if (currentFrame is null)
            {
                return;
            }

            // The detector is shared with the server: the comparison, the gate and the
            // post-motion hold (ending motion on the first quiet picture made the boxes blink).
            var now = DateTimeOffset.UtcNow;
            var result = context.Detector.Process(new MotionFrame(currentFrame, analysisWidth, analysisHeight, now));

            // The activity index is written off the UI thread; its writer has its own lock.
            PictureAnalyzed?.Invoke(this, new PictureAnalyzedEventArgs(cameraId, now, result));

            // A camera analysed only for its activity index has no motion to report.
            if (!context.ReportsMotion)
            {
                return;
            }

            var changePercent = result.ChangePercent;
            var boundingBoxes = result.Boxes;
            switch (result.Change)
            {
                case MotionHoldChange.Started:
                case MotionHoldChange.Continues:
                    context.LastMotionTime = DateTime.UtcNow;
                    context.LastBoundingBoxes = boundingBoxes;

                    // Marshal to UI thread since subscribers access DependencyProperties
                    RaiseMotionDetected(new MotionDetectedEventArgs(
                        cameraId,
                        changePercent,
                        isMotionActive: true,
                        boundingBoxes,
                        analysisWidth,
                        analysisHeight));
                    break;
                case MotionHoldChange.Ended:
                    context.LastBoundingBoxes = [];
                    RaiseMotionDetected(new MotionDetectedEventArgs(
                        cameraId,
                        changePercent,
                        isMotionActive: false,
                        boundingBoxes: null,
                        analysisWidth,
                        analysisHeight));
                    break;
            }

            context.IsMotionDetected = result.IsActive;
        }
        catch (Exception ex)
        {
            LogFrameAnalysisFailed(ex, cameraId);
        }
    }

    /// <summary>
    /// Raises the MotionDetected event on the UI thread to avoid cross-thread
    /// access violations on WPF DependencyProperties in event handlers.
    /// </summary>
    private void RaiseMotionDetected(MotionDetectedEventArgs args)
    {
        if (dispatcher.CheckAccess())
        {
            MotionDetected?.Invoke(this, args);
        }
        else
        {
            _ = dispatcher.BeginInvoke(() => MotionDetected?.Invoke(this, args));
        }
    }
}