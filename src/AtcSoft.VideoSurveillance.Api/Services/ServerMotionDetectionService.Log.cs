namespace Linksoft.VideoSurveillance.Api.Services;

public sealed partial class ServerMotionDetectionService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Motion detection started for camera {CameraId}")]
    private partial void LogMotionDetectionStarted(Guid cameraId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Motion detection stopped for camera {CameraId}")]
    private partial void LogMotionDetectionStopped(Guid cameraId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Motion detection loop error for camera {CameraId}")]
    private partial void LogMotionDetectionLoopError(Exception ex, Guid cameraId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Motion detection algorithm not yet implemented for camera {CameraId}; loop is idle until frame differencing is wired up")]
    private partial void LogMotionDetectionAlgorithmNotImplemented(Guid cameraId);
}