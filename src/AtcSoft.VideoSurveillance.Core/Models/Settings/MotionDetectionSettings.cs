namespace AtcSoft.VideoSurveillance.Models.Settings;

/// <summary>
/// Settings for motion detection functionality.
/// </summary>
public class MotionDetectionSettings
{
    public int Sensitivity { get; set; } = 30;

    /// <summary>
    /// Gets or sets the smallest object that counts, in percent of the picture's area. A watch
    /// zone without a smallest object of its own takes this one.
    /// </summary>
    public double SmallestObjectPercent { get; set; } = Helpers.MotionDetection.MotionDetectorOptions.DefaultSmallestObjectPercent;

    /// <summary>
    /// Gets or sets how long something that stops moving (a parked car) is still seen before it
    /// becomes part of the background, in seconds.
    /// </summary>
    public int StoppedObjectSeconds { get; set; } = Helpers.MotionDetection.MotionDetectorOptions.DefaultStoppedObjectSeconds;

    /// <summary>
    /// Gets or sets whether a change of light over most of the picture (a cloud, a lamp switched
    /// on) is ignored rather than seen as motion.
    /// </summary>
    public bool IgnoreLightingChanges { get; set; } = true;

    /// <summary>
    /// Gets or sets the share of changed pixels that is motion, in percent. Only the comparison
    /// detector (<see cref="Helpers.FrameDifferenceMotionDetector"/>) reads it; the built-in
    /// detector uses <see cref="SmallestObjectPercent"/>.
    /// </summary>
    public double MinimumChangePercent { get; set; } = 0.5;

    public int AnalysisFrameRate { get; set; } = 15;

    public int AnalysisWidth { get; set; } = 800;

    public int AnalysisHeight { get; set; } = 600;

    public int PostMotionDurationSeconds { get; set; } = 10;

    public int CooldownSeconds { get; set; } = 5;

    public BoundingBoxSettings BoundingBox { get; set; } = new();

    /// <inheritdoc />
    public override string ToString()
        => $"MotionDetectionSettings {{ Sensitivity={Sensitivity.ToString(CultureInfo.InvariantCulture)}, MinChange={MinimumChangePercent.ToString(CultureInfo.InvariantCulture)}% }}";
}