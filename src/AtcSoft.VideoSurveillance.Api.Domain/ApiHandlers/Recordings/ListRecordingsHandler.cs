namespace Linksoft.VideoSurveillance.Api.Domain.ApiHandlers.Recordings;

/// <summary>
/// Handler business logic for the ListRecordings operation.
/// </summary>
public sealed class ListRecordingsHandler(
    IRecordingService recordingService,
    IApplicationSettingsService settingsService,
    ICameraStorageService cameraStorageService) : IListRecordingsHandler
{
    private static readonly string[] VideoExtensions = [".mp4", ".mkv"];

    public Task<ListRecordingsResult> ExecuteAsync(
        ListRecordingsParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var cameras = cameraStorageService.GetAllCameras();
        var cameraNames = cameras.ToDictionary(c => c.Id, c => c.Display.DisplayName);

        // Active recording sessions
        var sessions = recordingService.GetActiveSessions();
        var activeRecordings = sessions
            .Where(s => parameters.CameraId is null || s.CameraId == parameters.CameraId)
            .Select(s => new Recording(
                Id: s.CameraId,
                CameraId: s.CameraId,
                CameraName: cameraNames.GetValueOrDefault(s.CameraId, string.Empty),
                FilePath: s.CurrentFilePath,
                StartedAt: new DateTimeOffset(s.StartTime, TimeSpan.Zero),
                Duration: s.Duration.ToString("c"),
                FileSizeBytes: 0,
                HasThumbnail: false))
            .ToList();

        // Historical recordings from filesystem
        var recordingPath = settingsService.Recording.RecordingPath;
        if (Directory.Exists(recordingPath))
        {
            var activeFilePaths = new HashSet<string>(
                sessions.Select(s => s.CurrentFilePath),
                StringComparer.OrdinalIgnoreCase);

            // Files are stored under {RecordingPath}/{safeName}/, where
            // safeName is derived from camera.Display.DisplayName (mirrors
            // ServerRecordingService.GenerateRecordingFilename). Build a
            // lookup so we can resolve cameraId by the file's parent folder
            // — the previous "file.Contains(camera.Id.ToString())" approach
            // never matched because filenames don't contain the GUID, so
            // every historical recording was reported with cameraId=Guid.Empty.
            var camerasBySafeName = cameras.ToDictionary(
                GetCameraFolderName,
                c => c,
                StringComparer.OrdinalIgnoreCase);

            var files = Directory.EnumerateFiles(recordingPath, "*.*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Where(f => !activeFilePaths.Contains(f));

            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                var relativePath = Path.GetRelativePath(recordingPath, file).Replace('\\', '/');

                var folderName = Path.GetFileName(Path.GetDirectoryName(file)) ?? string.Empty;
                var cameraId = Guid.Empty;
                var cameraName = string.Empty;
                if (camerasBySafeName.TryGetValue(folderName, out var matched))
                {
                    cameraId = matched.Id;
                    cameraName = matched.Display.DisplayName;
                }

                if (parameters.CameraId is not null && cameraId != parameters.CameraId)
                {
                    continue;
                }

                activeRecordings.Add(new Recording(
                    Id: Guid.NewGuid(),
                    CameraId: cameraId,
                    CameraName: cameraName,
                    FilePath: relativePath,
                    StartedAt: new DateTimeOffset(fileInfo.CreationTimeUtc, TimeSpan.Zero),
                    Duration: string.Empty,
                    FileSizeBytes: fileInfo.Length,
                    HasThumbnail: false));
            }
        }

        var sorted = activeRecordings
            .OrderByDescending(r => r.StartedAt)
            .ToList();

        return Task.FromResult(ListRecordingsResult.Ok(sorted));
    }

    // Must match the safeName logic in ServerRecordingService.GenerateRecordingFilename.
    // If the two ever diverge, historical recordings will lose their camera
    // association again. A shared helper would prevent this drift but we keep
    // it inline here to avoid introducing a Core dependency for one transformation.
    private static string GetCameraFolderName(
        Linksoft.VideoSurveillance.Models.CameraConfiguration camera)
        => string.IsNullOrWhiteSpace(camera.Display.DisplayName)
            ? camera.Id.ToString("N")[..8]
            : string.Join("_", camera.Display.DisplayName.Split(Path.GetInvalidFileNameChars()));
}