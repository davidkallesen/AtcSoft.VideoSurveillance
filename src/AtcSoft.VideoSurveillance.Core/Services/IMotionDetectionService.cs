namespace Linksoft.VideoSurveillance.Services;

/// <summary>
/// Service for detecting motion in camera video streams.
/// </summary>
public interface IMotionDetectionService
{
    event EventHandler<MotionDetectedEventArgs>? MotionDetected;

    void StartDetection(
        Guid cameraId,
        IMediaPipeline pipeline,
        MotionDetectionSettings? settings = null);

    void StopDetection(Guid cameraId);

    bool IsDetectionActive(Guid cameraId);

    bool IsMotionDetected(Guid cameraId);

    IReadOnlyList<BoundingBox> GetLastBoundingBoxes(Guid cameraId);

    (int Width, int Height) GetAnalysisResolution(Guid cameraId);
}