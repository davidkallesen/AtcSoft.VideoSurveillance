namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// The style that shows a camera's picture turned the way the camera is
/// set up. The live stream is the camera's own video, passed on untouched,
/// so the turn has to happen in the browser.
/// </summary>
public static class VideoRotationStyle
{
    private const string Fill = "width: 100%; height: 100%; object-fit: contain;";

    /// <summary>
    /// Gets the inline style for a video element inside a tile. The tile
    /// must be a size container (<c>container-type: size</c>) and
    /// positioned, because a quarter turn sizes the element from the tile.
    /// </summary>
    public static string For(CameraRotation? rotation)
        => rotation switch
        {
            CameraRotation.Rotate90 => QuarterTurn(90),
            CameraRotation.Rotate270 => QuarterTurn(270),
            CameraRotation.Rotate180 => Fill + " transform: rotate(180deg);",
            _ => Fill,
        };

    /// <summary>
    /// Gets a value indicating whether the picture is turned. A turned
    /// element takes the browser's own video controls with it, so they are
    /// left out then.
    /// </summary>
    public static bool IsRotated(CameraRotation? rotation)
        => rotation is CameraRotation.Rotate90 or CameraRotation.Rotate180 or CameraRotation.Rotate270;

    // Laid out with the tile's height as its width and the other way round,
    // centred, then turned: after the turn it covers the tile exactly.
    private static string QuarterTurn(int degrees)
        => "position: absolute; top: 50%; left: 50%; width: 100cqh; height: 100cqw; object-fit: contain; " +
           string.Create(CultureInfo.InvariantCulture, $"transform: translate(-50%, -50%) rotate({degrees}deg);");
}