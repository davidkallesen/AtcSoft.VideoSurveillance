namespace AtcSoft.VideoSurveillance.Api.Services;

public sealed partial class ActivityIndexService
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Activity index started for camera {CameraId}: {Path}")]
    private partial void LogIndexStarted(Guid cameraId, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Activity index failed for camera {CameraId}")]
    private partial void LogIndexFailed(Exception ex, Guid cameraId);
}