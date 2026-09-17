namespace Linksoft.VideoSurveillance.Services;

/// <summary>
/// Service for generating recording thumbnails.
/// </summary>
public interface IThumbnailGeneratorService
{
    void StartCapture(
        Guid cameraId,
        string cameraName,
        IMediaPipeline pipeline,
        string videoFilePath,
        int tileCount = 4);

    void StopCapture(Guid cameraId);

    bool IsCaptureActive(Guid cameraId);

    void StopAllCaptures();
}