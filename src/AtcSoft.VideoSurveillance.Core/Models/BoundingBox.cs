namespace Linksoft.VideoSurveillance.Models;

/// <summary>
/// Represents a rectangular bounding box in analysis coordinates.
/// Used for motion detection regions, replacing WPF-specific System.Windows.Rect.
/// </summary>
public class BoundingBox
{
    /// <summary>
    /// Gets or sets the X coordinate of the top-left corner.
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Gets or sets the Y coordinate of the top-left corner.
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Gets or sets the width of the bounding box.
    /// </summary>
    public double Width { get; set; }

    /// <summary>
    /// Gets or sets the height of the bounding box.
    /// </summary>
    public double Height { get; set; }

    /// <summary>
    /// Gets the area of the bounding box.
    /// </summary>
    public double Area => Width * Height;

    /// <inheritdoc />
    public override string ToString()
        => $"BoundingBox {{ X={X.ToString(CultureInfo.InvariantCulture)}, Y={Y.ToString(CultureInfo.InvariantCulture)}, Width={Width.ToString(CultureInfo.InvariantCulture)}, Height={Height.ToString(CultureInfo.InvariantCulture)} }}";
}