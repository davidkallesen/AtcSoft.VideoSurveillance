namespace Linksoft.VideoSurveillance.Models.Settings;

/// <summary>
/// Settings for motion detection bounding box display.
/// </summary>
public class BoundingBoxSettings
{
    public bool ShowInGrid { get; set; }

    public bool ShowInFullScreen { get; set; }

    public string Color { get; set; } = "Red";

    public int Thickness { get; set; } = 2;

    public int MinArea { get; set; } = 10;

    public int Padding { get; set; } = 4;

    public double Smoothing { get; set; } = 0.3;

    /// <inheritdoc />
    public override string ToString()
        => $"BoundingBoxSettings {{ Color='{Color}', Thickness={Thickness.ToString(CultureInfo.InvariantCulture)} }}";
}