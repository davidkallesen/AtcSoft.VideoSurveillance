namespace AtcSoft.VideoSurveillance.Api.Domain.ApiHandlers.Recordings;

/// <summary>
/// Handler business logic for the ListRecordings operation.
/// </summary>
public sealed class ListRecordingsHandler(
    IRecordingService recordingService,
    IApplicationSettingsService settingsService,
    ICameraStorageService cameraStorageService) : IListRecordingsHandler
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".avi"];

    public Task<ListRecordingsResult> ExecuteAsync(
        ListRecordingsParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var cameras = cameraStorageService.GetAllCameras();
        var cameraNames = cameras.ToDictionary(c => c.Id, c => c.Display.DisplayName);

        var recordingPath = settingsService.Recording.RecordingPath;

        // Active recording sessions
        var sessions = recordingService.GetActiveSessions();
        var activeRecordings = sessions
            .Where(s => parameters.CameraId is null || s.CameraId == parameters.CameraId)
            .Select(s =>
            {
                var thumbnail = FindRelativeThumbnail(s.CurrentFilePath, recordingPath);
                return new Recording(
                    Id: s.CameraId,
                    CameraId: s.CameraId,
                    CameraName: cameraNames.GetValueOrDefault(s.CameraId, string.Empty),
                    FilePath: ToRelativePath(s.CurrentFilePath, recordingPath),
                    StartedAt: new DateTimeOffset(s.StartTime, TimeSpan.Zero),
                    Duration: System.Xml.XmlConvert.ToString(s.Duration),
                    FileSizeBytes: 0,
                    HasThumbnail: thumbnail is not null,
                    ThumbnailPath: thumbnail);
            })
            .ToList();

        // Historical recordings from filesystem
        if (Directory.Exists(recordingPath))
        {
            var activeFilePaths = new HashSet<string>(
                sessions.Select(s => s.CurrentFilePath),
                StringComparer.OrdinalIgnoreCase);

            // Files are stored under {RecordingPath}/{camera folder}/, directly
            // or in a folder per day, so the camera is the first folder below
            // the root, whatever the layout. Two cameras can share a folder
            // name (same display name); the first one wins rather than the
            // whole listing failing.
            var camerasByFolderName = new Dictionary<string, AtcSoft.VideoSurveillance.Models.CameraConfiguration>(StringComparer.OrdinalIgnoreCase);
            foreach (var camera in cameras)
            {
                camerasByFolderName.TryAdd(
                    RecordingPathHelper.GetCameraFolderName(camera.Display.DisplayName, camera.Id),
                    camera);
            }

            var files = Directory.EnumerateFiles(recordingPath, "*.*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Where(f => !RecordingPathHelper.IsInThumbnailsFolder(f, recordingPath))
                .Where(f => !activeFilePaths.Contains(f));

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var relativePath = ToRelativePath(file, recordingPath);

                var folderName = RecordingPathHelper.GetCameraFolderFromPath(file, recordingPath) ?? string.Empty;
                var cameraId = Guid.Empty;
                var cameraName = string.Empty;
                if (camerasByFolderName.TryGetValue(folderName, out var matched))
                {
                    cameraId = matched.Id;
                    cameraName = matched.Display.DisplayName;
                }

                if (parameters.CameraId is not null && cameraId != parameters.CameraId)
                {
                    continue;
                }

                // The name carries the start time (in UTC on the server); the
                // file's creation time changes when it is moved or copied.
                var startedAt = RecordingPathHelper.TryParseTimestamp(fileInfo.Name, out var timestamp)
                    ? new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Unspecified), TimeSpan.Zero)
                    : new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero);

                var thumbnail = FindRelativeThumbnail(file, recordingPath);

                activeRecordings.Add(new Recording(
                    Id: RecordingMappingExtensions.CreateId(relativePath),
                    CameraId: cameraId,
                    CameraName: cameraName,
                    FilePath: relativePath,
                    StartedAt: startedAt,
                    Duration: string.Empty,
                    FileSizeBytes: fileInfo.Length,
                    HasThumbnail: thumbnail is not null,
                    ThumbnailPath: thumbnail));
            }
        }

        var sorted = activeRecordings
            .OrderByDescending(r => r.StartedAt)
            .ToList();

        return Task.FromResult(ListRecordingsResult.Ok(sorted));
    }

    /// <summary>
    /// Gets a path the way clients use it: relative to the recording root,
    /// with forward slashes. A file outside the root keeps its full path.
    /// </summary>
    private static string ToRelativePath(
        string filePath,
        string recordingPath)
    {
        if (string.IsNullOrEmpty(recordingPath) || !Path.IsPathRooted(filePath))
        {
            return filePath;
        }

        var relative = Path.GetRelativePath(recordingPath, filePath);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return filePath;
        }

        return relative.Replace('\\', '/');
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

        var relative = ToRelativePath(thumbnail, recordingPath);
        return Path.IsPathRooted(relative) ? null : relative;
    }
}