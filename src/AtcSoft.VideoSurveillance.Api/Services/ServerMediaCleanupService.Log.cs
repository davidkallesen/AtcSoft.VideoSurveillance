namespace Linksoft.VideoSurveillance.Api.Services;

public sealed partial class ServerMediaCleanupService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Cleanup pass starting - Recording retention: {RecordingDays} days, Include snapshots: {IncludeSnapshots}, Snapshot retention: {SnapshotDays} days")]
    private partial void LogCleanupStarting(int recordingDays, bool includeSnapshots, int snapshotDays);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleanup pass complete - Recordings: {Recordings}, Snapshots: {Snapshots}, Thumbnails: {Thumbnails}, Directories: {Directories}, Bytes freed: {Bytes}, Errors: {Errors}")]
    private partial void LogCleanupCompleted(int recordings, int snapshots, int thumbnails, int directories, string bytes, int errors);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted media file: {File}")]
    private partial void LogDeletedFile(string file);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted thumbnail: {File}")]
    private partial void LogDeletedThumbnail(string file);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removed empty directory: {Directory}")]
    private partial void LogRemovedEmptyDirectory(string directory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete file: {File}")]
    private partial void LogFileError(Exception ex, string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove directory: {Directory}")]
    private partial void LogDirectoryError(Exception ex, string directory);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to enumerate active recording sessions; cleanup may delete an active recording")]
    private partial void LogActiveSessionLookupFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Disk space OK on '{Drive}': {FreeMb:F0} MB free")]
    private partial void LogDiskSpaceOk(string drive, double freeMb);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Disk space low on '{Drive}': {FreeMb:F0} MB free (threshold {ThresholdMb} MB) — starting reclaim pass")]
    private partial void LogDiskSpaceLow(string drive, double freeMb, int thresholdMb);

    [LoggerMessage(Level = LogLevel.Error, Message = "Disk space CRITICAL on '{Drive}': {FreeMb:F0} MB free — reclaim triggered; all non-active recordings will be deleted if needed")]
    private partial void LogDiskSpaceCritical(string drive, double freeMb);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Disk space check failed for '{Path}'")]
    private partial void LogDiskSpaceCheckFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disk space reclaim started for '{Path}': target {ThresholdMb} MB free")]
    private partial void LogDiskSpaceReclaimStarted(string path, int thresholdMb);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disk space reclaim complete for '{Path}': freed {FreedMb:F2} MB ({Count} files)")]
    private partial void LogDiskSpaceReclaimComplete(string path, double freedMb, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Disk space reclaim exhausted all non-active recordings in '{Path}': freed {FreedMb:F2} MB but still below threshold")]
    private partial void LogDiskSpaceReclaimStillShort(string path, double freedMb);
}