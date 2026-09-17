// ReSharper disable RedundantArgumentDefaultValue
namespace Linksoft.CameraWall.Wpf.Services;

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
                var (changePercent, boundingBoxes) = CalculateFrameDifferenceWithBoundingBoxes(
                    context.PreviousFrame,
                    currentFrame,
                    context.Settings,
                    analysisWidth,
                    analysisHeight);

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

    private static (double ChangePercent, IReadOnlyList<BoundingBox> BoundingBoxes) CalculateFrameDifferenceWithBoundingBoxes(
        byte[] previous,
        byte[] current,
        MotionDetectionSettings settings,
        int analysisWidth,
        int analysisHeight)
    {
        if (previous.Length != current.Length)
        {
            return (0, []);
        }

        // Threshold based on sensitivity (0-100)
        // Map sensitivity to a practical threshold range (15-35)
        // Higher sensitivity (100) = lower threshold (15) - detects subtle changes
        // Lower sensitivity (0) = higher threshold (35) - requires larger changes
        var threshold = (int)(35 - (settings.Sensitivity * 0.2)); // Range: 15-35

        // Use a grid-based approach to filter out scattered noise
        // Divide frame into cells and only consider cells with significant motion
        const int cellSize = 20; // Each cell is 20x20 pixels
        var gridWidth = (analysisWidth + cellSize - 1) / cellSize;
        var gridHeight = (analysisHeight + cellSize - 1) / cellSize;
        var cellChangeCounts = new int[gridWidth * gridHeight];
        var isActiveCell = new bool[gridWidth * gridHeight];

        var totalChangedPixels = 0;

        // Count changed pixels per cell
        for (var y = 0; y < analysisHeight; y++)
        {
            for (var x = 0; x < analysisWidth; x++)
            {
                var i = (y * analysisWidth) + x;
                var diff = Math.Abs(current[i] - previous[i]);

                if (diff <= threshold)
                {
                    continue;
                }

                totalChangedPixels++;

                // Determine which cell this pixel belongs to
                var cellX = x / cellSize;
                var cellY = y / cellSize;
                var cellIndex = (cellY * gridWidth) + cellX;
                cellChangeCounts[cellIndex]++;
            }
        }

        var changePercent = (double)totalChangedPixels / previous.Length * 100.0;

        // Minimum changed pixels per cell to consider it as having motion (not just noise)
        // At 20x20 = 400 pixels per cell, require at least 8% (32 pixels) to count as motion
        var minChangedPerCell = Math.Max(8, (cellSize * cellSize) / 12);

        // Mark active cells
        for (var i = 0; i < cellChangeCounts.Length; i++)
        {
            isActiveCell[i] = cellChangeCounts[i] >= minChangedPerCell;
        }

        // Find all connected components (clusters) of active cells using flood fill
        var visited = new bool[gridWidth * gridHeight];
        var allClusters = new List<List<(int X, int Y)>>();

        for (var cellY = 0; cellY < gridHeight; cellY++)
        {
            for (var cellX = 0; cellX < gridWidth; cellX++)
            {
                var cellIndex = (cellY * gridWidth) + cellX;
                if (!isActiveCell[cellIndex] || visited[cellIndex])
                {
                    continue;
                }

                // Found an unvisited active cell - flood fill to find the cluster
                var cluster = FloodFillCluster(
                    cellX,
                    cellY,
                    gridWidth,
                    gridHeight,
                    isActiveCell,
                    visited);

                if (cluster.Count > 0)
                {
                    allClusters.Add(cluster);
                }
            }
        }

        // Create bounding boxes for all clusters that meet the minimum area requirement
        var boundingBoxes = new List<BoundingBox>();

        foreach (var cluster in allClusters)
        {
            // Find bounds of the cluster
            var minCellX = cluster.Min(c => c.X);
            var maxCellX = cluster.Max(c => c.X);
            var minCellY = cluster.Min(c => c.Y);
            var maxCellY = cluster.Max(c => c.Y);

            // Convert cell coordinates back to pixel coordinates
            var pixelMinX = minCellX * cellSize;
            var pixelMinY = minCellY * cellSize;
            var pixelMaxX = Math.Min(analysisWidth - 1, ((maxCellX + 1) * cellSize) - 1);
            var pixelMaxY = Math.Min(analysisHeight - 1, ((maxCellY + 1) * cellSize) - 1);

            var width = pixelMaxX - pixelMinX + 1;
            var height = pixelMaxY - pixelMinY + 1;
            var area = width * height;

            // Only create bounding box if area exceeds minimum
            if (area < settings.BoundingBox.MinArea)
            {
                continue;
            }

            // Add padding
            var padding = settings.BoundingBox.Padding;
            var paddedX = Math.Max(0, pixelMinX - padding);
            var paddedY = Math.Max(0, pixelMinY - padding);
            var paddedWidth = Math.Min(analysisWidth - paddedX, width + (2 * padding));
            var paddedHeight = Math.Min(analysisHeight - paddedY, height + (2 * padding));

            boundingBoxes.Add(new BoundingBox { X = paddedX, Y = paddedY, Width = paddedWidth, Height = paddedHeight });
        }

        // Sort by area (largest first) for consistent ordering
        boundingBoxes.Sort((a, b) => b.Area.CompareTo(a.Area));

        return (changePercent, boundingBoxes);
    }

    /// <summary>
    /// Flood fill to find all connected active cells starting from a seed cell.
    /// Uses 8-connectivity (includes diagonal neighbors).
    /// </summary>
    private static List<(int X, int Y)> FloodFillCluster(
        int startX,
        int startY,
        int gridWidth,
        int gridHeight,
        bool[] isActiveCell,
        bool[] visited)
    {
        var cluster = new List<(int X, int Y)>();
        var stack = new Stack<(int X, int Y)>();
        stack.Push((startX, startY));

        // 8-connectivity offsets (including diagonals)
        int[] dx = [-1, 0, 1, -1, 1, -1, 0, 1];
        int[] dy = [-1, -1, -1, 0, 0, 1, 1, 1];

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            var index = (y * gridWidth) + x;

            if (x < 0 || x >= gridWidth || y < 0 || y >= gridHeight)
            {
                continue;
            }

            if (visited[index] || !isActiveCell[index])
            {
                continue;
            }

            visited[index] = true;
            cluster.Add((x, y));

            // Add all 8 neighbors
            for (var i = 0; i < 8; i++)
            {
                var nx = x + dx[i];
                var ny = y + dy[i];
                if (nx >= 0 && nx < gridWidth && ny >= 0 && ny < gridHeight)
                {
                    var neighborIndex = (ny * gridWidth) + nx;
                    if (!visited[neighborIndex] && isActiveCell[neighborIndex])
                    {
                        stack.Push((nx, ny));
                    }
                }
            }
        }

        return cluster;
    }
}