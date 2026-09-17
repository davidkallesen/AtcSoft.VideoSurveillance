namespace Linksoft.VideoSurveillance.Api.Services;

/// <summary>
/// Server-side periodic media cleanup. Mirrors the WPF
/// <c>MediaCleanupService</c> but runs via <see cref="BackgroundServiceBase{T}"/>
/// so it has no UI / dispatcher dependency and inherits sequential (no-overlap)
/// ticking plus exception logging. Delegates the file-system work to the shared
/// <see cref="MediaCleanupRunner"/>. The <see cref="MediaCleanupSchedule"/> is
/// re-read on every tick: <c>Disabled</c> is a no-op, <c>OnStartup</c> runs once
/// then stops the service, and <c>OnStartupAndPeriodically</c> runs each tick
/// (the first at startup, thanks to a zero startup delay).
/// </summary>
public sealed partial class ServerMediaCleanupService : BackgroundServiceBase<ServerMediaCleanupService>
{
    private static readonly string[] RecordingExtensions = [".mp4", ".mkv", ".avi"];
    private static readonly string[] SnapshotExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    private readonly IApplicationSettingsService settingsService;
    private readonly IRecordingService recordingService;

    public ServerMediaCleanupService(
        ILogger<ServerMediaCleanupService> logger,
        ServerMediaCleanupServiceOptions options,
        IBackgroundServiceHealthService healthService,
        IApplicationSettingsService settingsService,
        IRecordingService recordingService)
        : base(logger, options, healthService)
    {
        this.settingsService = settingsService;
        this.recordingService = recordingService;

        healthService.SetMaxStalenessInSeconds(
            ServiceName,
            BackgroundServiceHealthHelper.StalenessFor(options));
    }

    /// <inheritdoc />
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
            // Run-once mode: stop the loop so we don't spin a 4-hour timer
            // forever. Safe to call from within DoWorkAsync — StopAsync
            // observes the now-cancelled stopping token and returns.
            await StopAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunCleanupAsync()
    {
        var settings = settingsService.Recording.Cleanup;

        LogCleanupStarting(
            settings.RecordingRetentionDays,
            settings.IncludeSnapshots,
            settings.SnapshotRetentionDays);

        var activePaths = GetActiveRecordingPaths();

        // File-system work runs on the thread pool so a large recording tree
        // doesn't stall the host's background-service loop.
        var summary = await Task.Run(() => RunCore(settings, activePaths)).ConfigureAwait(false);

        LogCleanupCompleted(
            summary.RecordingsDeleted,
            summary.SnapshotsDeleted,
            summary.ThumbnailsDeleted,
            summary.DirectoriesRemoved,
            FormatBytes(summary.BytesFreed),
            summary.ErrorCount);

        await CheckAndReclaimDiskSpaceAsync(settingsService.Recording.RecordingPath).ConfigureAwait(false);
    }

    private async Task CheckAndReclaimDiskSpaceAsync(string recordingPath)
    {
        if (string.IsNullOrEmpty(recordingPath))
        {
            return;
        }

        var cleanup = settingsService.Recording.Cleanup;
        if (!cleanup.EnableDiskSpaceGuard)
        {
            return;
        }

        var minFreeBytes = (long)cleanup.MinFreeSpaceMb * 1024L * 1024L;

        long freeBytes;
        string driveRoot;
        try
        {
            driveRoot = Path.GetPathRoot(Path.GetFullPath(recordingPath)) ?? string.Empty;
            if (string.IsNullOrEmpty(driveRoot))
            {
                return;
            }

            freeBytes = new DriveInfo(driveRoot).AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            LogDiskSpaceCheckFailed(ex, recordingPath);
            return;
        }

        var freeMb = freeBytes / (1024.0 * 1024.0);

        if (freeBytes >= minFreeBytes)
        {
            LogDiskSpaceOk(driveRoot, freeMb);
            return;
        }

        // Below minimum — escalate to Error when critically low (≤ 25% of threshold)
        if (freeBytes <= minFreeBytes / 4)
        {
            LogDiskSpaceCritical(driveRoot, freeMb);
        }
        else
        {
            LogDiskSpaceLow(driveRoot, freeMb, cleanup.MinFreeSpaceMb);
        }

        LogDiskSpaceReclaimStarted(recordingPath, cleanup.MinFreeSpaceMb);

        var activePaths = GetActiveRecordingPaths();
        var run = await Task.Run(() => MediaCleanupRunner.ReclaimBySize(
            recordingPath,
            RecordingExtensions,
            minFreeBytes,
            activePaths,
            deleteCompanionThumbnail: true)).ConfigureAwait(false);

        foreach (var f in run.DeletedFiles)
        {
            LogDeletedFile(f);
        }

        foreach (var t in run.DeletedThumbnails)
        {
            LogDeletedThumbnail(t);
        }

        if (run.StillShort)
        {
            LogDiskSpaceReclaimStillShort(recordingPath, run.BytesFreed / (1024.0 * 1024.0));
        }
        else
        {
            LogDiskSpaceReclaimComplete(recordingPath, run.BytesFreed / (1024.0 * 1024.0), run.DeletedFiles.Count);
        }
    }

    private CleanupSummary RunCore(
        MediaCleanupSettings settings,
        HashSet<string> activePaths)
    {
        var summary = new CleanupSummary();

        var recordingPath = settingsService.Recording.RecordingPath;
        if (!string.IsNullOrEmpty(recordingPath))
        {
            var run = MediaCleanupRunner.CleanDirectory(
                recordingPath,
                RecordingExtensions,
                DateTime.Now.AddDays(-settings.RecordingRetentionDays),
                activePaths,
                deleteCompanionThumbnail: true);

            foreach (var f in run.DeletedFiles)
            {
                LogDeletedFile(f);
            }

            foreach (var t in run.DeletedThumbnails)
            {
                LogDeletedThumbnail(t);
            }

            summary.RecordingsDeleted += run.DeletedFiles.Count;
            summary.ThumbnailsDeleted += run.DeletedThumbnails.Count;
            summary.BytesFreed += run.BytesFreed;
            ApplyErrors(run.Errors, summary);

            var emptyDirs = MediaCleanupRunner.RemoveEmptyDirectoriesBelow(recordingPath);
            ApplyEmptyDirectoriesResult(emptyDirs, summary);
        }

        if (!settings.IncludeSnapshots)
        {
            return summary;
        }

        var snapshotPath = settingsService.CameraDisplay.SnapshotPath;
        if (string.IsNullOrEmpty(snapshotPath))
        {
            return summary;
        }

        var snapshotRun = MediaCleanupRunner.CleanDirectory(
            snapshotPath,
            SnapshotExtensions,
            DateTime.Now.AddDays(-settings.SnapshotRetentionDays),
            activePaths,
            deleteCompanionThumbnail: false);

        foreach (var f in snapshotRun.DeletedFiles)
        {
            LogDeletedFile(f);
        }

        summary.SnapshotsDeleted += snapshotRun.DeletedFiles.Count;
        summary.BytesFreed += snapshotRun.BytesFreed;
        ApplyErrors(snapshotRun.Errors, summary);

        var snapshotEmpty = MediaCleanupRunner.RemoveEmptyDirectoriesBelow(snapshotPath);
        ApplyEmptyDirectoriesResult(snapshotEmpty, summary);

        return summary;
    }

    private void ApplyEmptyDirectoriesResult(
        MediaCleanupDirectoryResult run,
        CleanupSummary summary)
    {
        foreach (var dir in run.RemovedDirectories)
        {
            LogRemovedEmptyDirectory(dir);
        }

        summary.DirectoriesRemoved += run.RemovedDirectories.Count;

        foreach (var error in run.Errors)
        {
            LogDirectoryError(error.Exception, error.Path);
        }

        summary.ErrorCount += run.Errors.Count;
    }

    private void ApplyErrors(
        IReadOnlyList<MediaCleanupRunError> errors,
        CleanupSummary summary)
    {
        foreach (var error in errors)
        {
            LogFileError(error.Exception, error.Path);
        }

        summary.ErrorCount += errors.Count;
    }

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
                }
            }
        }
        catch (Exception ex)
        {
            LogActiveSessionLookupFailed(ex);
        }

        return set;
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

    private sealed class CleanupSummary
    {
        public int RecordingsDeleted { get; set; }

        public int SnapshotsDeleted { get; set; }

        public int ThumbnailsDeleted { get; set; }

        public int DirectoriesRemoved { get; set; }

        public long BytesFreed { get; set; }

        public int ErrorCount { get; set; }
    }
}