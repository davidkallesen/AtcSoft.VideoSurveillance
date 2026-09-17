namespace Linksoft.VideoSurveillance.Enums;

/// <summary>
/// Specifies the video codec to transcode recordings into.
/// </summary>
public enum VideoTranscodeCodec
{
    /// <summary>
    /// No transcoding — copy the original video stream as-is.
    /// </summary>
    None,

    /// <summary>
    /// Transcode video to H.264 (AVC) for broad playback compatibility.
    /// </summary>
    H264,
}