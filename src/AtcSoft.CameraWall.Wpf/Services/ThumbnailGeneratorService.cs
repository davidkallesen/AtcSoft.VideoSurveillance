namespace Linksoft.CameraWall.Wpf.Services;

using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using DrawingImaging = System.Drawing.Imaging;

/// <summary>
/// Service for generating recording thumbnails by capturing frames and creating a grid layout.
/// </summary>
[Registration(Lifetime.Singleton)]
public partial class ThumbnailGeneratorService : IThumbnailGeneratorService
{
    private const int FrameWidth = 320;
    private const int FrameHeight = 240;

    private readonly ILogger<ThumbnailGeneratorService> logger;
    private readonly ConcurrentDictionary<Guid, ThumbnailCaptureContext> captures = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ThumbnailGeneratorService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public ThumbnailGeneratorService(ILogger<ThumbnailGeneratorService> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public void StartCapture(
        Guid cameraId,
        string cameraName,
        IMediaPipeline pipeline,
        string videoFilePath,
        int tileCount = 4)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(cameraName);

        // Validate tile count (must be 1 or 4)
        if (tileCount != 1 && tileCount != 4)
        {
            tileCount = 4;
        }

        // Stop any existing capture for this camera
        StopCapture(cameraId);

        var thumbnailPath = Path.ChangeExtension(videoFilePath, ".png");
        var context = new ThumbnailCaptureContext(cameraName, pipeline, thumbnailPath, tileCount);

        if (!captures.TryAdd(cameraId, context))
        {
            return;
        }

        LogStartingThumbnailCapture(cameraName, thumbnailPath, tileCount);

        // Capture first frame immediately
        context.ScheduledCaptures++;
        _ = CaptureFrameAsync(cameraId, context);

        // For single tile, we only need one frame - stop immediately
        if (tileCount == 1)
        {
            StopCapture(cameraId);
            return;
        }

        // Set up timer for remaining frames (only for 4-tile mode)
        context.Timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };

        context.Timer.Tick += (_, _) => OnTimerTick(cameraId);
        context.Timer.Start();
    }

    /// <inheritdoc/>
    public void StopCapture(Guid cameraId)
    {
        if (!captures.TryRemove(cameraId, out var context))
        {
            return;
        }

        context.Timer?.Stop();
        context.Timer = null;

        // Generate thumbnail with whatever frames we have
        GenerateThumbnail(cameraId, context);

        // Snapshot frames under lock; an in-flight CaptureFrameAsync may still be
        // adding to the list on a thread-pool thread (ConfigureAwait(false))
        Drawing.Bitmap?[] frames;
        lock (context.FramesLock)
        {
            frames = [.. context.CapturedFrames];
            context.CapturedFrames.Clear();
        }

        foreach (var frame in frames)
        {
            frame?.Dispose();
        }
    }

    /// <inheritdoc/>
    public bool IsCaptureActive(Guid cameraId)
        => captures.ContainsKey(cameraId);

    /// <inheritdoc/>
    public void StopAllCaptures()
    {
        var cameraIds = captures.Keys.ToList();

        foreach (var cameraId in cameraIds)
        {
            StopCapture(cameraId);
        }
    }

    private void OnTimerTick(Guid cameraId)
    {
        if (!captures.TryGetValue(cameraId, out var context))
        {
            return;
        }

        // Avoid scheduling more captures than tiles. The previous tick's
        // CaptureFrameAsync may still be in flight on the thread pool, so we
        // count scheduled (not completed) captures.
        if (context.ScheduledCaptures < context.TileCount)
        {
            context.ScheduledCaptures++;
            _ = CaptureFrameAsync(cameraId, context);
        }

        if (context.ScheduledCaptures >= context.TileCount)
        {
            StopCapture(cameraId);
        }
    }

    private async Task CaptureFrameAsync(
        Guid cameraId,
        ThumbnailCaptureContext context)
    {
        try
        {
            var pngBytes = await context.Pipeline.CaptureFrameAsync().ConfigureAwait(false);

            int frameIndex;
            if (pngBytes is null || pngBytes.Length == 0)
            {
                LogSnapshotCaptureNoData(context.CameraName);
                lock (context.FramesLock)
                {
                    context.CapturedFrames.Add(null);
                    frameIndex = context.CapturedFrames.Count - 1;
                }
            }
            else
            {
                using var memoryStream = new MemoryStream(pngBytes);
                var bitmap = new Drawing.Bitmap(memoryStream);
                lock (context.FramesLock)
                {
                    context.CapturedFrames.Add(bitmap);
                    frameIndex = context.CapturedFrames.Count - 1;
                }
            }

            LogCapturedFrame(frameIndex, context.CameraName);
        }
        catch (Exception ex)
        {
            LogFrameCaptureFailed(ex, context.CameraName);
            lock (context.FramesLock)
            {
                context.CapturedFrames.Add(null);
            }
        }
    }

    private void GenerateThumbnail(
        Guid cameraId,
        ThumbnailCaptureContext context)
    {
        // Snapshot frames under lock; CaptureFrameAsync may still be running
        // on a thread-pool thread when StopCapture invokes us
        Drawing.Bitmap?[] frames;
        lock (context.FramesLock)
        {
            frames = [.. context.CapturedFrames];
        }

        if (frames.Length == 0)
        {
            LogNoFramesCaptured(context.CameraName);
            return;
        }

        try
        {
            // Determine thumbnail dimensions based on tile count
            // 1 tile = 320x240 (single frame), 4 tiles = 640x480 (2x2 grid)
            var (thumbnailWidth, thumbnailHeight) = context.TileCount == 1
                ? (FrameWidth, FrameHeight)
                : (FrameWidth * 2, FrameHeight * 2);

            using var thumbnail = new Drawing.Bitmap(
                thumbnailWidth,
                thumbnailHeight,
                DrawingImaging.PixelFormat.Format24bppRgb);
            using var graphics = Drawing.Graphics.FromImage(thumbnail);

            // Fill background with black (for missing frames)
            graphics.Clear(Drawing.Color.Black);

            // Configure high-quality rendering
            graphics.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = Drawing2D.SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;

            if (context.TileCount == 1)
            {
                // Single tile: draw first frame scaled to fit
                var frame = frames.Length > 0 ? frames[0] : null;
                if (frame is not null)
                {
                    var destRect = new Drawing.Rectangle(0, 0, FrameWidth, FrameHeight);
                    graphics.DrawImage(frame, destRect);
                }
            }
            else
            {
                // Draw frames in 2x2 grid
                // Position mapping:
                // [0] Top-Left (0,0)     [1] Top-Right (320,0)
                // [2] Bottom-Left (0,240) [3] Bottom-Right (320,240)
                var positions = new[]
                {
                    (X: 0, Y: 0),
                    (X: FrameWidth, Y: 0),
                    (X: 0, Y: FrameHeight),
                    (X: FrameWidth, Y: FrameHeight),
                };

                for (var i = 0; i < context.TileCount; i++)
                {
                    var frame = i < frames.Length ? frames[i] : null;

                    if (frame is not null)
                    {
                        var destRect = new Drawing.Rectangle(
                            positions[i].X,
                            positions[i].Y,
                            FrameWidth,
                            FrameHeight);
                        graphics.DrawImage(frame, destRect);
                    }

                    // Missing frames stay black (already filled)
                }
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(context.ThumbnailPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save thumbnail atomically: write to *.tmp then move into
            // place. The recordings browser may read the same path
            // concurrently; without atomic replace it can observe a
            // partially-written PNG (corrupt image or GDI+ IOException).
            var tempPath = context.ThumbnailPath + ".tmp";
            thumbnail.Save(tempPath, DrawingImaging.ImageFormat.Png);
            File.Move(tempPath, context.ThumbnailPath, overwrite: true);

            LogGeneratedThumbnail(
                context.CameraName,
                frames.Count(f => f is not null),
                context.TileCount,
                context.TileCount,
                context.ThumbnailPath);
        }
        catch (Exception ex)
        {
            LogThumbnailGenerationFailed(ex, context.CameraName);
        }
    }

    /// <summary>
    /// Context for tracking thumbnail capture state for a single camera.
    /// </summary>
    private sealed class ThumbnailCaptureContext
    {
        public ThumbnailCaptureContext(
            string cameraName,
            IMediaPipeline pipeline,
            string thumbnailPath,
            int tileCount = 4)
        {
            CameraName = cameraName;
            Pipeline = pipeline;
            ThumbnailPath = thumbnailPath;
            TileCount = tileCount;
        }

        public string CameraName { get; }

        public IMediaPipeline Pipeline { get; }

        public string ThumbnailPath { get; }

        public int TileCount { get; }

        public List<Drawing.Bitmap?> CapturedFrames { get; } = [];

        public Lock FramesLock { get; } = new();

        public DispatcherTimer? Timer { get; set; }

        public int ScheduledCaptures { get; set; }
    }
}