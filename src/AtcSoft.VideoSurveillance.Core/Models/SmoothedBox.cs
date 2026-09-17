namespace Linksoft.VideoSurveillance.Models;

/// <summary>
/// Holds smoothed position values for a single bounding box.
/// </summary>
public sealed class SmoothedBox
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool HasInitialPosition { get; set; }

    /// <inheritdoc />
    public override string ToString()
        => $"SmoothedBox {{ Left={Left.ToString(CultureInfo.InvariantCulture)}, Top={Top.ToString(CultureInfo.InvariantCulture)}, Width={Width.ToString(CultureInfo.InvariantCulture)}, Height={Height.ToString(CultureInfo.InvariantCulture)} }}";
}