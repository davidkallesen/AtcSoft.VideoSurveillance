namespace Linksoft.VideoSurveillance.Models.Settings;

/// <summary>
/// Settings for motion detection functionality.
/// </summary>
public class MotionDetectionSettings
{
    public int Sensitivity { get; set; } = 30;

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