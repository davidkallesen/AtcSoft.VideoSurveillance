namespace AtcSoft.VideoSurveillance.Storybook.Scoring;

/// <summary>
/// Runs a scene through a detector on the fast path: the frames go straight to the detector,
/// without encoding. The boxes scored are the boxes the apps show: a detector's boxes stay on
/// screen until it reports new ones or motion ends, as in the tiles.
/// </summary>
public static class StorybookRunner
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Runs a scene.
    /// </summary>
    /// <param name="scene">The storybook.</param>
    /// <param name="detector">A fresh detector.</param>
    /// <param name="options">How the scene is fed to it.</param>
    /// <returns>The score and the frames.</returns>
    public static StorybookRun Run(
        StorybookScene scene,
        IMotionDetector detector,
        StorybookRunOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(detector);

        options ??= new StorybookRunOptions();
        var renderer = new SceneRenderer(scene);
        var frames = new List<ScoredFrame>();
        IReadOnlyList<BoundingBox> shown = [];
        var step = 1 / options.AnalysesPerSecond;

        for (var k = 0; k * step <= scene.Video.DurationSeconds; k++)
        {
            var time = k * step;
            var frameIndex = Math.Min((int)Math.Round(time * scene.Video.Fps), renderer.FrameCount - 1);
            var frame = renderer.Render(frameIndex);
            var analysis = Resize(frame.Luma, frame.Width, frame.Height, options.AnalysisWidth, options.AnalysisHeight);
            var result = detector.Process(new MotionFrame(analysis, options.AnalysisWidth, options.AnalysisHeight, Epoch.AddSeconds(frame.Time)));

            shown = result.Change switch
            {
                MotionHoldChange.Started or MotionHoldChange.Continues => result.Boxes,
                MotionHoldChange.Ended => [],
                _ => result.IsActive ? shown : [],
            };

            var scaleX = frame.Width / (double)options.AnalysisWidth;
            var scaleY = frame.Height / (double)options.AnalysisHeight;
            var detected = shown
                .Select(b => new DetectedBox(new PixelBox(b.X * scaleX, b.Y * scaleY, b.Width * scaleX, b.Height * scaleY), b.TrackId))
                .ToList();

            frames.Add(new ScoredFrame(frame.Time, frame.Truth, frame.IgnoreAll, detected));
        }

        return new StorybookRun(StorybookScorer.Score(frames, scene.Expect), frames);
    }

    /// <summary>
    /// Scales a grey picture bilinearly, as the apps do (the aspect ratio is not kept).
    /// </summary>
    /// <param name="source">The pixels.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="toWidth">The new width.</param>
    /// <param name="toHeight">The new height.</param>
    /// <returns>The scaled pixels.</returns>
    public static byte[] Resize(
        byte[] source,
        int width,
        int height,
        int toWidth,
        int toHeight)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (width == toWidth && height == toHeight)
        {
            return (byte[])source.Clone();
        }

        var result = new byte[toWidth * toHeight];
        var sx = width / (double)toWidth;
        var sy = height / (double)toHeight;
        for (var y = 0; y < toHeight; y++)
        {
            var fy = Math.Clamp(((y + 0.5) * sy) - 0.5, 0, height - 1);
            var y0 = (int)fy;
            var y1 = Math.Min(y0 + 1, height - 1);
            var wy = fy - y0;
            for (var x = 0; x < toWidth; x++)
            {
                var fx = Math.Clamp(((x + 0.5) * sx) - 0.5, 0, width - 1);
                var x0 = (int)fx;
                var x1 = Math.Min(x0 + 1, width - 1);
                var wx = fx - x0;
                var top = (source[(y0 * width) + x0] * (1 - wx)) + (source[(y0 * width) + x1] * wx);
                var bottom = (source[(y1 * width) + x0] * (1 - wx)) + (source[(y1 * width) + x1] * wx);
                result[(y * toWidth) + x] = (byte)Math.Round((top * (1 - wy)) + (bottom * wy));
            }
        }

        return result;
    }
}