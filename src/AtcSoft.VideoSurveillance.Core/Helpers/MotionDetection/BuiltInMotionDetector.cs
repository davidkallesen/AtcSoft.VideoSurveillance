namespace AtcSoft.VideoSurveillance.Helpers.MotionDetection;

/// <summary>
/// The motion detector of the bounding-box plan, in plain C#: a light blur, a learnt background
/// per pixel (mean and variance), a lighting-change check, morphology on the foreground, blobs,
/// merging of near blobs, and a tracker. Motion is on while a confirmed track exists: something
/// that has been seen for a while and has travelled. A tree in the wind sways on the spot and
/// never confirms; a slow walker differs from the background as a whole figure.
/// </summary>
public sealed class BuiltInMotionDetector : IMotionDetector
{
    private readonly MotionDetectorOptions options;
    private readonly MotionHold hold;
    private readonly BackgroundModel background = new();
    private readonly MotionTracker tracker = new();
    private readonly DetectorBuffers buffers = new();
    private DateTimeOffset? lastTime;
    private DateTimeOffset? settleUntil;
    private bool lastHadBoxes;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuiltInMotionDetector"/> class.
    /// </summary>
    /// <param name="settings">The camera's motion settings.</param>
    /// <param name="options">The tuning; from <paramref name="settings"/> when not given.</param>
    public BuiltInMotionDetector(
        MotionDetectionSettings settings,
        MotionDetectorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        this.options = options ?? MotionDetectorOptions.From(settings);
        hold = new MotionHold(TimeSpan.FromSeconds(Math.Max(0, settings.PostMotionDurationSeconds)));
    }

    /// <inheritdoc />
    public MotionDetectorResult Process(MotionFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        buffers.EnsurePixels(frame.Gray.Length);
        var blurred = buffers.Blurred;
        MaskOperations.Blur3(frame.Gray, blurred, buffers.Rows, frame.Width, frame.Height);
        var dt = lastTime is { } last ? Math.Max(0, (frame.Time - last).TotalSeconds) : 0;
        lastTime = frame.Time;

        if (background.IsEmpty || background.Width != frame.Width || background.Height != frame.Height)
        {
            background.Reset(blurred, frame.Width, frame.Height);
            tracker.Reset();
            return Result(moving: false, frame.Time, 0, []);
        }

        var mask = buffers.Mask;
        var share = background.Apply(
            blurred,
            mask,
            options.ThresholdSigmas,
            options.MinThreshold,
            Rate(dt, options.BackgroundSeconds),
            Rate(dt, options.ForegroundSeconds));

        // Most of the picture changed at once: light (exposure, a cloud, dusk), not an object.
        // Shift the background by the overall change, so what moves stays foreground; if the
        // change was not even (the IR switch from colour to grey), learn the picture anew.
        if (share > options.LightingChangeShare)
        {
            background.ShiftTo(blurred);
            share = background.Apply(blurred, mask, options.ThresholdSigmas, options.MinThreshold, 0, 0);
            if (share > options.LightingChangeShare)
            {
                background.Reset(blurred, frame.Width, frame.Height);
                tracker.Reset();
            }

            settleUntil = frame.Time.AddSeconds(options.LightingSettleSeconds);
            return Result(moving: false, frame.Time, share * 100, []);
        }

        if (settleUntil is { } settle && frame.Time < settle)
        {
            return Result(moving: false, frame.Time, share * 100, []);
        }

        MaskOperations.Open(mask, frame.Width, frame.Height, radius: 1, buffers.Temp);
        MaskOperations.Close(mask, frame.Width, frame.Height, radius: 3, buffers.Temp);

        var minPixels = options.SmallestObjectShare * frame.Width * frame.Height;
        var blobs = ConnectedComponents.Find(mask, frame.Width, frame.Height, buffers)
            .Where(b => b.Pixels >= minPixels)
            .ToList();
        blobs = ConnectedComponents.Merge(blobs, (int)Math.Ceiling(options.MergeGapShare * frame.Width));

        var time = (frame.Time - DateTimeOffset.UnixEpoch).TotalSeconds;
        var tracks = tracker.Update(blobs, time, options, frame.Width);
        var boxes = tracks
            .Select(t => new BoundingBox { X = t.Box.MinX, Y = t.Box.MinY, Width = t.Box.Width, Height = t.Box.Height, TrackId = t.Id })
            .OrderByDescending(b => b.Area)
            .ToList();

        return Result(boxes.Count > 0, frame.Time, share * 100, boxes);
    }

    /// <inheritdoc />
    public MotionDetectorResult Idle(DateTimeOffset now)
        => Result(moving: false, now, 0, []);

    /// <inheritdoc />
    public void Reset()
    {
        background.Reset([], 0, 0);
        tracker.Reset();
        lastTime = null;
        settleUntil = null;
        lastHadBoxes = false;
    }

    // How much a pixel learns in dt for a time constant: the share of the way to the new value.
    private static double Rate(
        double dt,
        double timeConstant)
        => timeConstant <= 0 ? 1 : 1 - Math.Exp(-dt / timeConstant);

    // While motion is held, new boxes are reported, and once more when they went away (empty),
    // so a box is not left on screen until motion ends; quiet pictures in between report nothing.
    private MotionDetectorResult Result(
        bool moving,
        DateTimeOffset time,
        double changePercent,
        IReadOnlyList<BoundingBox> boxes)
    {
        var change = hold.Update(moving, time);
        if (change == MotionHoldChange.None && hold.IsActive && lastHadBoxes)
        {
            change = MotionHoldChange.Continues;
        }

        lastHadBoxes = boxes.Count > 0 && change != MotionHoldChange.Ended;
        return new MotionDetectorResult(hold.IsActive, change, changePercent, change == MotionHoldChange.Ended ? [] : boxes);
    }
}