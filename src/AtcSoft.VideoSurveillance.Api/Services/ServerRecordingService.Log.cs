namespace Linksoft.VideoSurveillance.Api.Services;

public sealed partial class ServerRecordingService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Recording started for camera '{CameraName}' ({CameraId}): {FilePath}")]
    private partial void LogRecordingStarted(string cameraName, Guid cameraId, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording stopped for camera '{CameraName}' ({CameraId})")]
    private partial void LogRecordingStopped(string cameraName, Guid cameraId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording segmented for camera '{CameraName}' ({CameraId}): {FilePath}")]
    private partial void LogSegmented(string cameraName, Guid cameraId, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot segment camera {CameraId}: pipeline reference missing")]
    private partial void LogSegmentNoPipeline(Guid cameraId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot segment camera {CameraId}: camera not found in storage")]
    private partial void LogSegmentCameraNotFound(Guid cameraId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Segmentation failed for camera {CameraId}")]
    private partial void LogSegmentFailed(Exception ex, Guid cameraId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stopping pipeline for camera {CameraId} threw")]
    private partial void LogStopPipelineFailed(Exception ex, Guid cameraId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Disposing pipeline for camera {CameraId} threw")]
    private partial void LogDisposePipelineFailed(Exception ex, Guid cameraId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reaping inactive recording session for camera '{CameraName}' ({CameraId}): {Reason}")]
    private partial void LogReapingInactiveSession(string cameraName, Guid cameraId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reaper swept {ReapedCount} inactive recording session(s)")]
    private partial void LogReaperSwept(int reapedCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Disk space low on '{Drive}': {FreeMb:F0} MB free (threshold {ThresholdMb} MB) — reclaiming oldest recordings before start")]
    private partial void LogDiskSpaceLowBeforeRecording(string drive, double freeMb, int thresholdMb);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disk space reclaim complete before recording: freed {FreedMb:F2} MB ({Count} files)")]
    private partial void LogDiskSpaceReclaimComplete(double freedMb, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Disk space reclaim exhausted all non-active recordings on '{Drive}': freed {FreedMb:F2} MB but still below threshold")]
    private partial void LogDiskSpaceReclaimStillShort(string drive, double freedMb);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Disk space reclaim failed before recording start")]
    private partial void LogDiskSpaceReclaimFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Disk space still below threshold after reclaim — recording aborted for camera '{CameraName}'")]
    private partial void LogDiskSpaceGateFailed(string cameraName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Disk still below threshold mid-recording — stopping {Count} active recording(s) to prevent disk exhaustion")]
    private partial void LogDiskSpaceGuardStoppingAllRecordings(int count);
}