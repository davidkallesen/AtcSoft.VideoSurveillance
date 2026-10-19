namespace AtcSoft.VideoSurveillance.Api.Domain.ApiHandlers.Recordings;

/// <summary>
/// Handler business logic for the ListRecordings operation.
/// </summary>
/// <param name="recordingService">The recordings in progress.</param>
/// <param name="settingsService">The application settings.</param>
/// <param name="cameraStorageService">The cameras.</param>
/// <param name="durationProvider">Reads how long a finished recording is. Optional: without it the length is left out.</param>
public sealed class ListRecordingsHandler(
    IRecordingService recordingService,
    IApplicationSettingsService settingsService,
    ICameraStorageService cameraStorageService,
    IRecordingDurationProvider? durationProvider = null) : IListRecordingsHandler
{
    public Task<ListRecordingsResult> ExecuteAsync(
        ListRecordingsParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var recordingPath = settingsService.Recording.RecordingPath;
        var recordings = RecordingCatalog
            .Find(recordingPath, cameraStorageService.GetAllCameras(), recordingService.GetActiveSessions(), parameters.CameraId, DateTimeOffset.UtcNow)
            .Select(file => ToRecording(file, recordingPath))
            .OrderByDescending(r => r.StartedAt)
            .ToList();

        return Task.FromResult(ListRecordingsResult.Ok(recordings));
    }

    private Recording ToRecording(
        RecordingFile file,
        string recordingPath)
    {
        var thumbnail = FindRelativeThumbnail(file.FullPath, recordingPath);
        string length;
        if (file.InProgress)
        {
            length = System.Xml.XmlConvert.ToString(file.LastWrittenAt - file.StartedAt);
        }
        else
        {
            length = durationProvider?.GetDuration(file.FullPath) is { } duration
                ? System.Xml.XmlConvert.ToString(duration)
                : string.Empty;
        }

        return new Recording(
            Id: file.InProgress ? file.CameraId : RecordingMappingExtensions.CreateId(file.RelativePath),
            CameraId: file.CameraId,
            CameraName: file.CameraName,
            FilePath: file.RelativePath,
            StartedAt: file.StartedAt,
            Duration: length,
            FileSizeBytes: file.Length,
            HasThumbnail: thumbnail is not null,
            InProgress: file.InProgress,
            ThumbnailPath: thumbnail);
    }

    private static string? FindRelativeThumbnail(
        string videoPath,
        string recordingPath)
    {
        if (string.IsNullOrEmpty(recordingPath))
        {
            return null;
        }

        var thumbnail = RecordingPathHelper.FindThumbnailPath(videoPath, recordingPath);
        if (thumbnail is null)
        {
            return null;
        }

        var relative = RecordingCatalog.ToRelativePath(thumbnail, recordingPath);
        return Path.IsPathRooted(relative) ? null : relative;
    }
}