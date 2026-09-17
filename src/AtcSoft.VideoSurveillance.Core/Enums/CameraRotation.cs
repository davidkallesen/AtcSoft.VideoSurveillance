namespace Linksoft.VideoSurveillance.Enums;

/// <summary>
/// Specifies the clockwise rotation applied to a camera stream.
/// Applied at the GPU video processor stage so display, snapshots, and
/// (via MP4 metadata) recordings are all rotated.
/// </summary>
public enum CameraRotation
{
    /// <summary>No rotation (default).</summary>
    None = 0,

    /// <summary>Rotate the stream 90° clockwise.</summary>
    Rotate90 = 90,

    /// <summary>Rotate the stream 180°.</summary>
    Rotate180 = 180,

    /// <summary>Rotate the stream 270° clockwise (90° counter-clockwise).</summary>
    Rotate270 = 270,
}