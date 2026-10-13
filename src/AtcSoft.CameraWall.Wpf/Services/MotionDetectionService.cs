// ReSharper disable RedundantArgumentDefaultValue
namespace AtcSoft.CameraWall.Wpf.Services;

/// <summary>
/// Service for detecting motion in camera video streams using frame differencing.
/// Uses a staggered scheduler to analyze cameras in sequence, preventing CPU spikes.
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
    private DispatcherTimer? schedulerTimer;
    private int currentCameraIndex;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MotionDetectionService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MotionDetectionService(ILogger<MotionDetectionService> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public event EventHandler<MotionDetectedEventArgs>? MotionDetected;

    /// <inheritdoc/>
    public void StartDetection(
        Guid cameraId,
        IMediaPipeline pipeline,
        MotionDetectionSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        // Stop existing detection if any
        StopDetection(cameraId);

        var context = new MotionDetectionContext(cameraId, pipeline, settings ?? new MotionDetectionSettings());

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

    /// <inheritdoc/>
    public void StopDetection(Guid cameraId)
    {
        // Same atomic invariant as StartDetection: both maps move together.
        MotionDetectionContext? context;
        lock (schedulerLock)
        {
            contexts.TryRemove(cameraId, out context);
            scheduledCameras.Remove(cameraId);
            UpdateSchedulerInterval();
        }

        context?.Dispose();
    }

    /// <inheritdoc/>
    public bool IsDetectionActive(Guid cameraId)
        => contexts.ContainsKey(cameraId);

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
                StopDetection(cameraId);
            }
        }

        disposed = true;
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
            using var ms = new MemoryStream(frameBytes);
            using var bitmap = new System.Drawing.Bitmap(ms);

            // Get analysis resolution from settings (or use defaults)
            var analysisWidth = context.Settings.AnalysisWidth > 0 ? context.Settings.AnalysisWidth : DefaultAnalysisWidth;
            var analysisHeight = context.Settings.AnalysisHeight > 0 ? context.Settings.AnalysisHeight : DefaultAnalysisHeight;

            // Convert to grayscale and downscale for analysis
            var currentFrame = ConvertToGrayscale(bitmap, analysisWidth, analysisHeight);

            // Compare with previous frame
            if (context.PreviousFrame is not null)
            {
                // The comparison itself is shared with the server.
                var analysis = AtcSoft.VideoSurveillance.Helpers.MotionFrameAnalyzer.Analyze(
                    context.PreviousFrame,
                    currentFrame,
                    analysisWidth,
                    analysisHeight,
                    context.Settings);
                var changePercent = analysis.ChangePercent;
                var boundingBoxes = analysis.BoundingBoxes;

                var wasMotionDetected = context.IsMotionDetected;
                var minimumChange = context.Settings.MinimumChangePercent;
                var isMotionActive = changePercent >= minimumChange;

                if (isMotionActive)
                {
                    context.IsMotionDetected = true;
                    context.LastMotionTime = DateTime.UtcNow;
                    context.LastBoundingBoxes = boundingBoxes;

                    // Always fire event when motion is active - cooldown is handled by RecordingService for recordings
                    // Marshal to UI thread since subscribers access DependencyProperties
                    RaiseMotionDetected(new MotionDetectedEventArgs(
                        cameraId,
                        changePercent,
                        isMotionActive: true,
                        boundingBoxes,
                        analysisWidth,
                        analysisHeight));
                }
                else
                {
                    // Motion stopped
                    if (wasMotionDetected)
                    {
                        // Raise event indicating motion stopped
                        RaiseMotionDetected(new MotionDetectedEventArgs(
                            cameraId,
                            changePercent,
                            isMotionActive: false,
                            boundingBoxes: null,
                            analysisWidth,
                            analysisHeight));
                    }

                    context.IsMotionDetected = false;
                    context.LastBoundingBoxes = [];
                }
            }

            // Store current frame for next comparison
            context.PreviousFrame = currentFrame;
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

    private static byte[] ConvertToGrayscale(
        System.Drawing.Bitmap bitmap,
        int targetWidth,
        int targetHeight)
    {
        // Resize and convert to grayscale using LockBits for performance
        using var resized = new System.Drawing.Bitmap(bitmap, new System.Drawing.Size(targetWidth, targetHeight));

        var grayscale = new byte[targetWidth * targetHeight];
        var rect = new System.Drawing.Rectangle(0, 0, targetWidth, targetHeight);

        // Lock bits for fast pixel access
        var bitmapData = resized.LockBits(
            rect,
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        try
        {
            var stride = bitmapData.Stride;
            var scan0 = bitmapData.Scan0;

            // Use unsafe code for maximum performance
            unsafe
            {
                var ptr = (byte*)scan0.ToPointer();

                for (var y = 0; y < targetHeight; y++)
                {
                    var row = ptr + (y * stride);
                    for (var x = 0; x < targetWidth; x++)
                    {
                        // BGR format (24bpp)
                        var b = row[x * 3];
                        var g = row[(x * 3) + 1];
                        var r = row[(x * 3) + 2];

                        // Standard grayscale conversion using luminosity method
                        // Using integer math for speed: (77*R + 150*G + 29*B) >> 8 ≈ 0.299*R + 0.587*G + 0.114*B
                        grayscale[(y * targetWidth) + x] = (byte)(((77 * r) + (150 * g) + (29 * b)) >> 8);
                    }
                }
            }
        }
        finally
        {
            resized.UnlockBits(bitmapData);
        }

        return grayscale;
    }
}