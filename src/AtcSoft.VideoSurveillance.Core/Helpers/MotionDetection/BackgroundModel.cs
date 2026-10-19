namespace AtcSoft.VideoSurveillance.Helpers.MotionDetection;

/// <summary>
/// What the scene looks like without anything moving: per pixel a running mean and variance.
/// A pixel that keeps changing (a leaf in the wind) gets a large variance and stops counting as
/// foreground; a moving object differs from the background as a whole, not only at its edges.
/// </summary>
internal sealed class BackgroundModel
{
    private const float StartVariance = 36f;
    private const float MinVariance = 9f;
    private const float MaxVariance = 2500f;

    private float[] mean = [];
    private float[] variance = [];

    public int Width { get; private set; }

    public int Height { get; private set; }

    public bool IsEmpty
        => mean.Length == 0;

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
    {
        var count = 0;
        var looked = frame.Length;
        var s2 = (float)(sigmas * sigmas);
        var min2 = (float)(minThreshold * minThreshold);
        var bg = (float)backgroundRate;
        var fg = (float)foregroundRate;
        for (var i = 0; i < frame.Length; i++)
        {
            var d = frame[i] - mean[i];
            var d2 = d * d;
            var isForeground = d2 > min2 && d2 > s2 * variance[i];
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

            var rate = isForeground ? fg : bg;
            mean[i] += rate * d;

            // The variance learns from every pixel, so swaying leaves raise theirs.
            variance[i] = Math.Clamp(variance[i] + (rate * (d2 - variance[i])), MinVariance, MaxVariance);
        }

        return looked > 0 ? count / (double)looked : 0;
    }
}