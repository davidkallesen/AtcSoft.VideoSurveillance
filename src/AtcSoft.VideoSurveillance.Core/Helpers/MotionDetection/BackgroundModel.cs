namespace AtcSoft.VideoSurveillance.Helpers.MotionDetection;

/// <summary>
/// What the scene looks like without anything moving: per pixel a running mean and variance.
/// A pixel that keeps changing (a leaf in the wind) gets a large variance and stops counting as
/// foreground; a moving object differs from the background as a whole, not only at its edges.
/// A foreground pixel learns slowly (something that stopped stays foreground for a while), but
/// only once it has matched the background since it was learnt anew: until then the background
/// is the more likely mistake (something that was in the first picture and left), and the pixel
/// learns at the background's speed, so its ghost goes within a second.
/// </summary>
/// <remarks>
/// The per-pixel step runs on <see cref="Vector{T}"/>, with the plain loop for the last pixels.
/// Both do the same single-precision operations in the same order (no fused multiply-add), so the
/// result is the same to the bit whatever the CPU's vector width.
/// </remarks>
internal sealed class BackgroundModel
{
    private const float StartVariance = 36f;
    private const float MinVariance = 9f;
    private const float MaxVariance = 2500f;

    private float[] mean = [];
    private float[] variance = [];

    // 1 where the pixel has been background since the background was learnt anew.
    private byte[] settled = [];

    // The watch zones' thresholds spread per pixel, kept while the zones and their values stay.
    private byte[]? zonedFor;
    private float[] zonedSigmas = [];
    private float[] zonedMins = [];
    private float[] pixelSigmas = [];
    private float[] pixelMins = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsEmpty
        => mean.Length == 0;

    /// <summary>
    /// Gets the mean per pixel, read only (for the tests that compare the vector and plain steps).
    /// </summary>
    public ReadOnlySpan<float> Mean
        => mean;

    /// <summary>
    /// Gets the variance per pixel, read only.
    /// </summary>
    public ReadOnlySpan<float> Variance
        => variance;

    public void Reset(
        byte[] frame,
        int width,
        int height)
    {
        Width = width;
        Height = height;
        if (mean.Length != frame.Length)
        {
            mean = new float[frame.Length];
            variance = new float[frame.Length];
            settled = new byte[frame.Length];
        }
        else
        {
            Array.Clear(settled);
        }

        for (var i = 0; i < frame.Length; i++)
        {
            mean[i] = frame[i];
            variance[i] = StartVariance;
        }
    }

    /// <summary>
    /// Shifts the whole background by the picture's overall change in brightness, so a lighting
    /// change (exposure, a cloud, dusk) does not make everything foreground while what is in the
    /// picture stays as it was.
    /// </summary>
    /// <param name="frame">The (blurred) picture after the change.</param>
    public void ShiftTo(byte[] frame)
    {
        double sum = 0;
        for (var i = 0; i < frame.Length; i++)
        {
            sum += frame[i] - mean[i];
        }

        var shift = (float)(sum / frame.Length);
        for (var i = 0; i < mean.Length; i++)
        {
            mean[i] += shift;
        }
    }

    /// <summary>
    /// Finds the foreground of a picture and learns from it.
    /// </summary>
    /// <param name="frame">The (blurred) picture.</param>
    /// <param name="mask">Set to 1 where the picture is foreground.</param>
    /// <param name="sigmas">The threshold in standard deviations.</param>
    /// <param name="minThreshold">The threshold's minimum in grey levels.</param>
    /// <param name="backgroundRate">How much a background pixel learns now (0 to 1).</param>
    /// <param name="foregroundRate">How much a foreground pixel learns now (0 to 1).</param>
    /// <param name="ignored">Pixels never counted as foreground (1), still learnt; <see langword="null"/> for none.</param>
    /// <returns>The share of the pixels looked at that is foreground.</returns>
    public double Apply(
        byte[] frame,
        byte[] mask,
        double sigmas,
        double minThreshold,
        double backgroundRate,
        double foregroundRate,
        byte[]? ignored = null)
        => Learn(
            frame,
            mask,
            (float)(sigmas * sigmas),
            (float)(minThreshold * minThreshold),
            pixelSigmas: default,
            pixelMins: default,
            (float)backgroundRate,
            (float)foregroundRate,
            ignored);

    /// <summary>
    /// As <see cref="Apply"/>, with the thresholds of the watch zone each pixel is in: index 0 of
    /// the thresholds is the camera's, outside every watch zone.
    /// </summary>
    /// <param name="frame">The picture, blurred.</param>
    /// <param name="mask">Where the foreground is written, one byte per pixel.</param>
    /// <param name="sigmas">The standard deviations a pixel must differ by, by zone.</param>
    /// <param name="minThresholds">The grey levels a pixel must differ by, by zone.</param>
    /// <param name="watched">The watch zone of each pixel (see <see cref="ZoneMask.Watched"/>).</param>
    /// <param name="backgroundRate">How much the background learns of a pixel that is not foreground.</param>
    /// <param name="foregroundRate">How much it learns of one that is.</param>
    /// <param name="ignored">The ignored pixels, or <see langword="null"/>.</param>
    /// <returns>The share of the pixels looked at that are foreground.</returns>
    public double ApplyZoned(
        byte[] frame,
        byte[] mask,
        IReadOnlyList<double> sigmas,
        IReadOnlyList<double> minThresholds,
        byte[] watched,
        double backgroundRate,
        double foregroundRate,
        byte[]? ignored = null)
    {
        ArgumentNullException.ThrowIfNull(sigmas);
        ArgumentNullException.ThrowIfNull(minThresholds);
        ArgumentNullException.ThrowIfNull(watched);

        var s2 = sigmas.Select(s => (float)(s * s)).ToArray();
        var min2 = minThresholds.Select(m => (float)(m * m)).ToArray();
        SpreadZones(watched, s2, min2);
        return Learn(
            frame,
            mask,
            sigmas2: 0,
            mins2: 0,
            pixelSigmas,
            pixelMins,
            (float)backgroundRate,
            (float)foregroundRate,
            ignored);
    }

    // Each pixel's squared thresholds from its zone; spread again only when the zones or their values
    // change. A new layout comes as a new array (ZoneMask.Watched), never as an edit of the old one.
    private void SpreadZones(
        byte[] watched,
        float[] s2,
        float[] min2)
    {
        if (ReferenceEquals(zonedFor, watched) &&
            pixelSigmas.Length == watched.Length &&
            s2.AsSpan().SequenceEqual(zonedSigmas) &&
            min2.AsSpan().SequenceEqual(zonedMins))
        {
            return;
        }

        if (pixelSigmas.Length != watched.Length)
        {
            pixelSigmas = new float[watched.Length];
            pixelMins = new float[watched.Length];
        }

        for (var i = 0; i < watched.Length; i++)
        {
            pixelSigmas[i] = s2[watched[i]];
            pixelMins[i] = min2[watched[i]];
        }

        zonedFor = watched;
        zonedSigmas = s2;
        zonedMins = min2;
    }

    // The per-pixel step: foreground where the picture differs from the mean by more than both
    // thresholds (squared), then the mean and variance learn at the pixel's rate. The thresholds are
    // the same for every pixel, or per pixel when those spans are given.
    private double Learn(
        byte[] frame,
        byte[] mask,
        float sigmas2,
        float mins2,
        ReadOnlySpan<float> pixelSigmas,
        ReadOnlySpan<float> pixelMins,
        float bg,
        float fg,
        byte[]? ignored)
    {
        var perPixel = !pixelSigmas.IsEmpty;
        var count = 0;
        var looked = frame.Length;
        var i = 0;
        var lanes = Vector<float>.Count;
        var step = Vector<byte>.Count;
        if (Vector.IsHardwareAccelerated && frame.Length >= step)
        {
            var thresholds = new LearnStep(sigmas2, mins2, bg, fg);
            var counted = Vector<int>.Zero;
            var lookedAt = Vector<int>.Zero;
            for (; i + step <= frame.Length; i += step)
            {
                Vector.Widen(new Vector<byte>(frame.AsSpan(i, step)), out var pixelsLow, out var pixelsHigh);
                Vector.Widen(pixelsLow, out var pixels0, out var pixels1);
                Vector.Widen(pixelsHigh, out var pixels2, out var pixels3);
                var keep = ignored is null
                    ? Vector<byte>.AllBitsSet
                    : Vector.Equals(new Vector<byte>(ignored.AsSpan(i, step)), Vector<byte>.Zero);

                // Widened as signed, so all bits set stays all bits set (-1).
                Vector.Widen(Vector.AsVectorSByte(keep), out var keepLow, out var keepHigh);
                Vector.Widen(keepLow, out var keep0, out var keep1);
                Vector.Widen(keepHigh, out var keep2, out var keep3);
                var wasSettled = new Vector<byte>(settled.AsSpan(i, step));
                Vector.Widen(Vector.AsVectorSByte(~Vector.Equals(wasSettled, Vector<byte>.Zero)), out var settledLow, out var settledHigh);
                Vector.Widen(settledLow, out var settled0, out var settled1);
                Vector.Widen(settledHigh, out var settled2, out var settled3);

                var foreground0 = LearnLanes(i, pixels0, keep0, settled0, pixelSigmas, pixelMins, thresholds);
                var foreground1 = LearnLanes(i + lanes, pixels1, keep1, settled1, pixelSigmas, pixelMins, thresholds);
                var foreground2 = LearnLanes(i + (2 * lanes), pixels2, keep2, settled2, pixelSigmas, pixelMins, thresholds);
                var foreground3 = LearnLanes(i + (3 * lanes), pixels3, keep3, settled3, pixelSigmas, pixelMins, thresholds);

                // -1 per foreground pixel and per pixel looked at.
                counted += foreground0 + foreground1 + foreground2 + foreground3;
                lookedAt += keep0 + keep1 + keep2 + keep3;
                var foreground = Vector.AsVectorByte(Vector.Narrow(Vector.Narrow(foreground0, foreground1), Vector.Narrow(foreground2, foreground3)));
                (foreground & Vector<byte>.One).CopyTo(mask.AsSpan(i, step));
                (wasSettled | Vector.AndNot(Vector<byte>.One, foreground)).CopyTo(settled.AsSpan(i, step));
            }

            count = -Vector.Sum(counted);
            looked = -Vector.Sum(lookedAt) + (frame.Length - i);
        }

        for (; i < frame.Length; i++)
        {
            var d = frame[i] - mean[i];
            var d2 = d * d;
            var isForeground = perPixel
                ? d2 > pixelMins[i] && d2 > pixelSigmas[i] * variance[i]
                : d2 > mins2 && d2 > sigmas2 * variance[i];
            if (ignored is not null && ignored[i] != 0)
            {
                isForeground = false;
                looked--;
            }

            mask[i] = isForeground ? (byte)1 : (byte)0;
            if (isForeground)
            {
                count++;
            }

            var rate = isForeground && settled[i] != 0 ? fg : bg;
            if (!isForeground)
            {
                settled[i] = 1;
            }

            mean[i] += rate * d;

            // The variance learns from every pixel, so swaying leaves raise theirs.
            variance[i] = Math.Clamp(variance[i] + (rate * (d2 - variance[i])), MinVariance, MaxVariance);
        }

        return looked > 0 ? count / (double)looked : 0;
    }

    // One vector of pixels from 'at': the same steps as the plain loop. Returns -1 where foreground.
    private Vector<int> LearnLanes(
        int at,
        Vector<uint> pixels,
        Vector<int> keep,
        Vector<int> wasSettled,
        ReadOnlySpan<float> pixelSigmas,
        ReadOnlySpan<float> pixelMins,
        in LearnStep step)
    {
        var lanes = Vector<float>.Count;
        var means = mean.AsSpan(at, lanes);
        var variances = variance.AsSpan(at, lanes);
        var m = new Vector<float>(means);
        var v = new Vector<float>(variances);
        var d = Vector.ConvertToSingle(Vector.AsVectorInt32(pixels)) - m;
        var d2 = d * d;
        var s = pixelSigmas.IsEmpty ? step.Sigmas : new Vector<float>(pixelSigmas.Slice(at, lanes));
        var min = pixelMins.IsEmpty ? step.Mins : new Vector<float>(pixelMins.Slice(at, lanes));
        var isForeground = Vector.GreaterThan(d2, min) & Vector.GreaterThan(d2, s * v) & keep;
        var rate = Vector.ConditionalSelect(isForeground & wasSettled, step.Foreground, step.Background);
        (m + (rate * d)).CopyTo(means);
        Vector.Min(Vector.Max(v + (rate * (d2 - v)), step.Low), step.High).CopyTo(variances);
        return isForeground;
    }

    // The values of one Learn as vectors.
    [StructLayout(LayoutKind.Auto)]
    private readonly struct LearnStep(
        float sigmas2,
        float mins2,
        float background,
        float foreground)
    {
        public Vector<float> Sigmas { get; } = new(sigmas2);

        public Vector<float> Mins { get; } = new(mins2);

        public Vector<float> Background { get; } = new(background);

        public Vector<float> Foreground { get; } = new(foreground);

        public Vector<float> Low { get; } = new(MinVariance);

        public Vector<float> High { get; } = new(MaxVariance);
    }
}