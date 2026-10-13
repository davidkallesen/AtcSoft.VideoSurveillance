namespace AtcSoft.VideoSurveillance.Services;

/// <summary>
/// Tells whether a camera is being streamed to a viewer by a process of its
/// own. A USB camera can only be opened by one program at a time, so whoever
/// wants to open it has to know.
/// </summary>
public interface ILiveStreamStatus
{
    /// <summary>
    /// Gets whether the camera is currently being streamed.
    /// </summary>
    /// <param name="cameraId">The camera.</param>
    /// <returns><see langword="true"/> while a stream of the camera is running.</returns>
    bool IsStreaming(Guid cameraId);
}