namespace Linksoft.CameraWall.Wpf.Services;

/// <summary>
/// Service for automatically cleaning up old recordings and snapshots.
/// </summary>
// Not auto-registered via [Registration]: the App registers this explicitly so
// the SAME singleton instance is exposed both as IMediaCleanupService (for the
// UI: events, IsRunning, manual RunCleanupAsync) and as an IHostedService (for
// the Generic Host to drive its periodic DoWorkAsync loop and graceful stop).
public partial class MediaCleanupService : BackgroundServiceBase<MediaCleanupService>, IMediaCleanupService
{
    private static readonly string[] RecordingExtensions = [".mp4", ".mkv", ".avi"];
    private static readonly string[] SnapshotExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    private readonly IApplicationSettingsService settingsService;
    private readonly IRecordingService recordingService;
    private readonly Lock lockObject = new();
    private bool isRunning;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaCleanupService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="settingsService">The application settings service.</param>
    /// <param name="recordingService">The recording service, used to identify and skip files that are currently being written to.</param>
    public MediaCleanupService(
        ILogger<MediaCleanupService> logger,
        IApplicationSettingsService settingsService,
        IRecordingService recordingService)
        : base(logger, new DefaultBackgroundServiceOptions
        {
            ServiceName = nameof(MediaCleanupService),
            StartupDelaySeconds = 5,
            RepeatIntervalSeconds = 6 * 60 * 60,
        })
    {
        this.settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        this.recordingService = recordingService ?? throw new ArgumentNullException(nameof(recordingService));
    }

    /// <inheritdoc/>
    public event EventHandler<MediaCleanupCompletedEventArgs>? CleanupCompleted;

    /// <inheritdoc/>
    public bool IsRunning
    {
        get
        {
            lock (lockObject)
            {
                return isRunning;
            }
        }
    }

    /// <summary>
    /// Periodic cleanup tick driven by the host. The <see cref="MediaCleanupSchedule"/>
    /// is re-read each pass: <c>Disabled</c> is a no-op, <c>OnStartup</c> runs
    /// once then stops the service, and <c>OnStartupAndPeriodically</c> runs on
    /// every tick (the first at startup, via the short startup delay).
    /// </summary>
    /// <param name="stoppingToken">The stopping token.</param>
    public override async Task DoWorkAsync(CancellationToken stoppingToken)
    {
        var schedule = settingsService.Recording.Cleanup.Schedule;
        if (schedule == MediaCleanupSchedule.Disabled)
        {
            return;
        }

        await RunCleanupAsync().ConfigureAwait(false);

        if (schedule == MediaCleanupSchedule.OnStartup)
        {
            await StopAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<MediaCleanupResult> RunCleanupAsync()
    {
        lock (lockObject)
        {
            if (isRunning)
            {
                LogCleanupAlreadyInProgress();
                return new MediaCleanupResult();
            }

            isRunning = true;
        }

        try
        {
            var settings = settingsService.Recording.Cleanup;
            var result = new MediaCleanupResult();

            LogCleanupStarting(
                settings.RecordingRetentionDays,
                settings.IncludeSnapshots,
                settings.SnapshotRetentionDays);

            // Snapshot active recording paths so an in-progress recording is
            // never deleted by the cleanup pass (sharing-violation IOException
            // on Windows; orphaned undeletable file otherwise).
            var activePaths = GetActiveRecordingPaths();

            // Run the actual file-system work on the thread pool — keeps the
            // dispatcher responsive when scanning very large recording trees.
            await Task.Run(() =>
                RunCleanupCore(settings, activePaths, result)).ConfigureAwait(false);

            LogCleanupCompleted(
                result.RecordingsDeleted,
                result.SnapshotsDeleted,
                result.ThumbnailsDeleted,
                result.DirectoriesRemoved,
                FormatBytes(result.BytesFreed),
                result.ErrorCount);

            OnCleanupCompleted(result);

            // Disk-space guard runs after age-based cleanup so the reclaim pass
            // benefits from any files already deleted above.
            await ReclaimBySpaceAsync().ConfigureAwait(false);

            return result;
        }
        finally
        {
            lock (lockObject)
            {
                isRunning = false;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<MediaCleanupResult> RunDiskSpaceReclaimAsync()
    {
        lock (lockObject)
        {
            if (isRunning)
            {
                LogCleanupAlreadyInProgress();
                return new MediaCleanupResult();
            }

            isRunning = true;
        }

        try
        {
            return await ReclaimBySpaceAsync().ConfigureAwait(false);
        }
        finally
        {
            lock (lockObject)
            {
                isRunning = false;
            }
        }
    }

    private async Task<MediaCleanupResult> ReclaimBySpaceAsync()
    {
        var cleanup = settingsService.Recording.Cleanup;
        if (!cleanup.EnableDiskSpaceGuard)
        {
            return new MediaCleanupResult();
        }

        var recordingPath = settingsService.Recording.RecordingPath;
        if (string.IsNullOrEmpty(recordingPath))
        {
            return new MediaCleanupResult();
        }

        var minFreeBytes = (long)cleanup.MinFreeSpaceMb * 1024L * 1024L;

        try
        {
            var driveRoot = Path.GetPathRoot(Path.GetFullPath(recordingPath));
            if (string.IsNullOrEmpty(driveRoot))
            {
                return new MediaCleanupResult();
            }

            var freeBytes = new DriveInfo(driveRoot).AvailableFreeSpace;
            if (freeBytes >= minFreeBytes)
            {
                LogDiskSpaceOk(driveRoot, freeBytes / (1024.0 * 1024.0), cleanup.MinFreeSpaceMb);
                return new MediaCleanupResult();
            }

            LogDiskSpaceLow(driveRoot, freeBytes / (1024.0 * 1024.0), cleanup.MinFreeSpaceMb);
        }
        catch (Exception ex)
        {
            LogDiskSpaceCheckFailed(ex, recordingPath);
            return new MediaCleanupResult();
        }

        var activePaths = GetActiveRecordingPaths();

        var run = await Task.Run(() => MediaCleanupRunner.ReclaimBySize(
            recordingPath,
            RecordingExtensions,
            minFreeBytes,
            activePaths,
            deleteCompanionThumbnail: true)).ConfigureAwait(false);

        foreach (var f in run.DeletedFiles)
        {
            LogDeletedOldMediaFile(f);
        }

        foreach (var t in run.DeletedThumbnails)
        {
            LogDeletedThumbnail(t);
        }

        var result = new MediaCleanupResult
        {
            RecordingsDeleted = run.DeletedFiles.Count,
            ThumbnailsDeleted = run.DeletedThumbnails.Count,
            BytesFreed = run.BytesFreed,
            ErrorCount = run.Errors.Count,
        };

        if (run.StillShort)
        {
            LogDiskSpaceReclaimStillShort(recordingPath, run.BytesFreed / (1024.0 * 1024.0));
        }
        else
        {
            LogDiskSpaceReclaimComplete(recordingPath, run.BytesFreed / (1024.0 * 1024.0), run.DeletedFiles.Count);
        }

        OnCleanupCompleted(result);
        return result;
    }

    private void RunCleanupCore(
        MediaCleanupSettings settings,
        HashSet<string> activePaths,
        MediaCleanupResult result)
    {
        var recordingPath = settingsService.Recording.RecordingPath;
        if (!string.IsNullOrEmpty(recordingPath))
        {
            var run = MediaCleanupRunner.CleanDirectory(
                recordingPath,
                RecordingExtensions,
                DateTime.Now.AddDays(-settings.RecordingRetentionDays),
                activePaths,
                deleteCompanionThumbnail: true);

            ApplyRecordingRunResult(run, result);

            var emptyDirs = MediaCleanupRunner.RemoveEmptyDirectoriesBelow(recordingPath);
            ApplyEmptyDirectoriesResult(emptyDirs, result);
        }

        if (!settings.IncludeSnapshots)
        {
            return;
        }

        var snapshotPath = settingsService.CameraDisplay.SnapshotPath;
        if (string.IsNullOrEmpty(snapshotPath))
        {
            return;
        }

        var snapshotRun = MediaCleanupRunner.CleanDirectory(
            snapshotPath,
            SnapshotExtensions,
            DateTime.Now.AddDays(-settings.SnapshotRetentionDays),
            activePaths,
            deleteCompanionThumbnail: false);

        ApplySnapshotRunResult(snapshotRun, result);

        var snapshotEmpty = MediaCleanupRunner.RemoveEmptyDirectoriesBelow(snapshotPath);
        ApplyEmptyDirectoriesResult(snapshotEmpty, result);
    }

    private void ApplyRecordingRunResult(
        MediaCleanupRunResult run,
        MediaCleanupResult result)
    {
        foreach (var file in run.DeletedFiles)
        {
            LogDeletedOldMediaFile(file);
        }

        foreach (var thumb in run.DeletedThumbnails)
        {
            LogDeletedThumbnail(thumb);
        }

        result.RecordingsDeleted += run.DeletedFiles.Count;
        result.ThumbnailsDeleted += run.DeletedThumbnails.Count;
        result.BytesFreed += run.BytesFreed;
        ApplyErrors(run.Errors, result);
    }

    private void ApplySnapshotRunResult(
        MediaCleanupRunResult run,
        MediaCleanupResult result)
    {
        foreach (var file in run.DeletedFiles)
        {
            LogDeletedOldMediaFile(file);
        }

        result.SnapshotsDeleted += run.DeletedFiles.Count;
        result.BytesFreed += run.BytesFreed;
        ApplyErrors(run.Errors, result);
    }

    private void ApplyEmptyDirectoriesResult(
        MediaCleanupDirectoryResult run,
        MediaCleanupResult result)
    {
        foreach (var dir in run.RemovedDirectories)
        {
            LogRemovedEmptyDirectory(dir);
        }

        result.DirectoriesRemoved += run.RemovedDirectories.Count;
        foreach (var error in run.Errors)
        {
            switch (error.Exception)
            {
                case UnauthorizedAccessException uae:
                    LogAccessDeniedRemovingDirectory(uae, error.Path);
                    break;
                default:
                    LogCouldNotRemoveDirectory(error.Exception, error.Path);
                    break;
            }
        }

        result.ErrorCount += run.Errors.Count;
    }

    private void ApplyErrors(
        IReadOnlyList<MediaCleanupRunError> errors,
        MediaCleanupResult result)
    {
        foreach (var error in errors)
        {
            switch (error.Exception)
            {
                case UnauthorizedAccessException uae:
                    LogAccessDeniedDeletingFile(uae, error.Path);
                    break;
                case IOException ioe:
                    LogFailedToDeleteFile(ioe, error.Path);
                    break;
                default:
                    LogFailedToDeleteFile(error.Exception, error.Path);
                    break;
            }
        }

        result.ErrorCount += errors.Count;
    }

    // Builds an unordered, case-insensitive set of file paths currently being
    // written by the recording service. Used to skip in-flight files during
    // cleanup so we never call File.Delete on a handle the muxer still owns.
    private HashSet<string> GetActiveRecordingPaths()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var session in recordingService.GetActiveSessions())
            {
                if (!string.IsNullOrEmpty(session.CurrentFilePath))
                {
                    set.Add(Path.GetFullPath(session.CurrentFilePath));

                    // Also protect the live thumbnail companion file
                    var thumbnail = Path.ChangeExtension(session.CurrentFilePath, ".png");
                    set.Add(Path.GetFullPath(thumbnail));
                }
            }
        }
        catch (Exception ex)
        {
            // If we can't enumerate sessions, fail safe by treating no files as active
            // — but at least don't take down the cleanup pass.
            LogActiveSessionLookupFailed(ex);
        }

        return set;
    }

    private void OnCleanupCompleted(MediaCleanupResult result)
    {
        CleanupCompleted?.Invoke(this, new MediaCleanupCompletedEventArgs(result));
    }

    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        return bytes switch
        {
            >= GB => $"{bytes / (double)GB:F2} GB",
            >= MB => $"{bytes / (double)MB:F2} MB",
            >= KB => $"{bytes / (double)KB:F2} KB",
            _ => $"{bytes} bytes",
        };
    }
}