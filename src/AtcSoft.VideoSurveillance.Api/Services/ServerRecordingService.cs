namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// Server-side implementation of <see cref="IRecordingService"/> using FFmpeg media pipelines.
/// </summary>
public sealed partial class ServerRecordingService : IRecordingService, ICameraConnectionStateSink, IDisposable
{
    // Mirrors WPF's CameraTile stream-stale threshold — long enough to absorb
    // a slow keyframe interval, short enough to recover within one CCM tick.
    private const int StalePacketThresholdSeconds = 15;

    private static readonly string[] RecordingFileExtensions = [".mp4", ".mkv", ".avi"];

    private readonly IApplicationSettingsService settingsService;
    private readonly ICameraStorageService cameraStorageService;
    private readonly ILogger<ServerRecordingService> logger;
    private readonly IThumbnailGeneratorService? thumbnailService;
    private readonly TimeProvider timeProvider;

    // Cameras whose recording runs on a pipeline it does not own: a motion
    // recording uses the pipeline that watches the camera, and leaves it
    // running when it stops.
    private readonly ConcurrentDictionary<Guid, byte> borrowedPipelines = new();

    // When each camera's last motion recording stopped, for the cooldown.
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> motionRecordingStops = new();
    private readonly ConcurrentDictionary<Guid, RecordingSession> sessions = new();
    private readonly ConcurrentDictionary<Guid, IMediaPipeline> pipelines = new();

    // Per-camera pipeline ConnectionStateChanged handlers, kept so we can
    // unsubscribe at StopRecording time. Subscribing once at StartRecording
    // gives the broadcaster a single aggregated event to listen to (vs.
    // subscribing per-pipeline at every creation site).
    private readonly ConcurrentDictionary<Guid, EventHandler<ConnectionStateChangedEventArgs>> connectionHandlers = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerRecordingService"/> class.
    /// </summary>
    /// <param name="settingsService">The application settings.</param>
    /// <param name="cameraStorageService">The camera storage.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="thumbnailService">
    /// Makes a thumbnail for each recording segment. Optional: a host that
    /// cannot draw thumbnails (not Windows) records without them.
    /// </param>
    /// <param name="timeProvider">The clock; the system's when not given.</param>
    public ServerRecordingService(
        IApplicationSettingsService settingsService,
        ICameraStorageService cameraStorageService,
        ILogger<ServerRecordingService> logger,
        IThumbnailGeneratorService? thumbnailService = null,
        TimeProvider? timeProvider = null)
    {
        this.settingsService = settingsService;
        this.cameraStorageService = cameraStorageService;
        this.logger = logger;
        this.thumbnailService = thumbnailService;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;

    /// <summary>
    /// Raised when the connection state of an actively-recording camera's
    /// pipeline changes. Server-only — used by SurveillanceEventBroadcaster
    /// to forward connection events over SignalR. Cameras that aren't
    /// recording (e.g. one-shot snapshot pipelines) are not represented.
    /// </summary>
    public event Action<Guid, ConnectionState>? CameraConnectionStateChanged;

    /// <inheritdoc/>
    public void Report(
        Guid cameraId,
        ConnectionState state)
        => CameraConnectionStateChanged?.Invoke(cameraId, state);

    /// <inheritdoc/>
    public RecordingState GetRecordingState(Guid cameraId)
        => sessions.TryGetValue(cameraId, out var session) ? session.State : RecordingState.Idle;

    /// <inheritdoc/>
    public RecordingSession? GetSession(Guid cameraId)
        => sessions.GetValueOrDefault(cameraId);

    /// <inheritdoc/>
    public bool StartRecording(
        CameraConfiguration camera,
        IMediaPipeline pipeline)
        => StartRecording(camera, pipeline, startedByMotion: false);

    private bool StartRecording(
        CameraConfiguration camera,
        IMediaPipeline pipeline,
        bool startedByMotion)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(pipeline);

        if (sessions.ContainsKey(camera.Id))
        {
            return false;
        }

        if (!ReclaimDiskSpaceIfNeeded())
        {
            LogDiskSpaceGateFailed(camera.Display.DisplayName);
            return false;
        }

        var filePath = GenerateRecordingFilename(camera, GetEffectiveRecordingFormat(camera));

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        pipeline.StartRecording(filePath);

        var session = new RecordingSession(camera.Id, camera.Display.DisplayName, filePath, isManualRecording: !startedByMotion);

        if (!sessions.TryAdd(camera.Id, session))
        {
            // Lost the race — a concurrent StartRecording already registered
            // this camera. Clean up the pipeline we started and bail.
            try
            {
                pipeline.StopRecording();
            }
            catch (Exception ex)
            {
                LogStopPipelineFailed(ex, camera.Id);
            }

            // A borrowed pipeline is its owner's to close.
            if (!startedByMotion)
            {
                try
                {
                    pipeline.Dispose();
                }
                catch (Exception ex)
                {
                    LogDisposePipelineFailed(ex, camera.Id);
                }
            }

            return false;
        }

        pipelines[camera.Id] = pipeline;
        if (startedByMotion)
        {
            borrowedPipelines[camera.Id] = 0;
        }

        // Forward subsequent pipeline connection-state changes onto the
        // aggregated event so the broadcaster (and any other observer) sees
        // disconnect/reconnect transitions without subscribing per-pipeline.
        var capturedId = camera.Id;
        EventHandler<ConnectionStateChangedEventArgs> handler = (_, e) =>
            CameraConnectionStateChanged?.Invoke(capturedId, e.NewState);
        pipeline.ConnectionStateChanged += handler;
        connectionHandlers[camera.Id] = handler;

        // Synthetic "Connected" so subscribers that join after the pipeline
        // has already reached Connected (the typical case — StartRecording
        // is called *after* the connection wait succeeds) still see the
        // current state. Without this, the Blazor live view would show
        // "Disconnected" until the next genuine state change.
        CameraConnectionStateChanged?.Invoke(camera.Id, ConnectionState.Connected);

        StartThumbnail(camera, pipeline, filePath);

        RaiseStateChanged(camera.Id, RecordingState.Idle, session.State, filePath);
        LogRecordingStarted(camera.Display.DisplayName, camera.Id, filePath);

        return true;
    }

    /// <inheritdoc/>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Pipeline is unconditionally disposed inside the TryRemove block via the explicit Dispose() call.")]
    public void StopRecording(Guid cameraId)
    {
        if (!sessions.TryRemove(cameraId, out var session))
        {
            return;
        }

        // The recording service owns the pipeline once StartRecording accepts
        // it (the dictionary entry IS the ownership). Dispose here so manual
        // Stop via REST/SignalR cleans up the underlying VideoPlayer's RTSP
        // connection, decoder thread, and GPU resources. Without this, every
        // Start/Stop cycle leaks an entire pipeline.
        // A motion recording ran on the pipeline that watches the camera:
        // that pipeline stays open, and the camera stays connected.
        var borrowed = borrowedPipelines.TryRemove(cameraId, out _);
        if (!session.IsManualRecording)
        {
            motionRecordingStops[cameraId] = timeProvider.GetUtcNow();
        }

        if (pipelines.TryRemove(cameraId, out var pipeline))
        {
            // Write the thumbnail from the frames captured so far while the
            // pipeline can still be asked for them.
            StopThumbnail(cameraId);

            // Unsubscribe BEFORE Dispose so we don't surface a tear-down
            // Disconnected event onto the aggregate (the synthetic
            // Disconnected below covers the recording-ended case).
            if (connectionHandlers.TryRemove(cameraId, out var handler))
            {
                pipeline.ConnectionStateChanged -= handler;
            }

            try
            {
                pipeline.StopRecording();
            }
            catch (Exception ex)
            {
                LogStopPipelineFailed(ex, cameraId);
            }

            if (!borrowed)
            {
                try
                {
                    pipeline.Dispose();
                }
                catch (Exception ex)
                {
                    LogDisposePipelineFailed(ex, cameraId);
                }
            }
        }

        // The recording lifecycle ended — let connection-state subscribers
        // know the camera is no longer being observed by us. Mirrors the
        // synthetic Connected we fired in StartRecording.
        if (!borrowed)
        {
            CameraConnectionStateChanged?.Invoke(cameraId, ConnectionState.Disconnected);
        }

        RaiseStateChanged(cameraId, session.State, RecordingState.Idle, session.CurrentFilePath);
        LogRecordingStopped(session.CameraName, cameraId);
    }

    /// <inheritdoc/>
    public bool IsRecording(Guid cameraId)
        => sessions.ContainsKey(cameraId);

    /// <inheritdoc/>
    public bool TriggerMotionRecording(
        CameraConfiguration camera,
        IMediaPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(pipeline);

        // Already recording, whoever started it: the motion is in it.
        if (sessions.TryGetValue(camera.Id, out var running))
        {
            running.LastMotionTime = timeProvider.GetUtcNow().UtcDateTime;
            return true;
        }

        // A camera that sees motion again right after a motion recording
        // waits for the cooldown, so it does not make a file every few seconds.
        if (motionRecordingStops.TryGetValue(camera.Id, out var stoppedAt))
        {
            var cooldown = MotionDetectionPolicy.Resolve(camera, settingsService.MotionDetection).CooldownSeconds;
            if (cooldown > 0 && timeProvider.GetUtcNow() - stoppedAt < TimeSpan.FromSeconds(cooldown))
            {
                return false;
            }
        }

        return StartRecording(camera, pipeline, startedByMotion: true);
    }

    /// <inheritdoc/>
    public void UpdateMotionTimestamp(Guid cameraId)
    {
        // No-op for server implementation (no post-motion timer needed)
    }

    private bool ReclaimDiskSpaceIfNeeded()
    {
        var cleanup = settingsService.Recording.Cleanup;
        if (!cleanup.EnableDiskSpaceGuard)
        {
            return true;
        }

        var recordingPath = settingsService.Recording.RecordingPath;
        if (string.IsNullOrEmpty(recordingPath))
        {
            return true;
        }

        try
        {
            var driveRoot = Path.GetPathRoot(Path.GetFullPath(recordingPath));
            if (string.IsNullOrEmpty(driveRoot))
            {
                return true;
            }

            var minFreeBytes = (long)cleanup.MinFreeSpaceMb * 1024L * 1024L;
            var freeBytes = new DriveInfo(driveRoot).AvailableFreeSpace;
            if (freeBytes >= minFreeBytes)
            {
                return true;
            }

            LogDiskSpaceLowBeforeRecording(driveRoot, freeBytes / (1024.0 * 1024.0), cleanup.MinFreeSpaceMb);

            var skipPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in sessions.Values)
            {
                if (!string.IsNullOrEmpty(session.CurrentFilePath))
                {
                    skipPaths.Add(Path.GetFullPath(session.CurrentFilePath));
                    foreach (var thumbnail in RecordingPathHelper.GetThumbnailCandidates(session.CurrentFilePath, recordingPath))
                    {
                        skipPaths.Add(Path.GetFullPath(thumbnail));
                    }
                }
            }

            var run = MediaCleanupRunner.ReclaimBySize(
                recordingPath,
                RecordingFileExtensions,
                minFreeBytes,
                skipPaths,
                deleteCompanionThumbnail: true);

            if (run.StillShort)
            {
                LogDiskSpaceReclaimStillShort(driveRoot, run.BytesFreed / (1024.0 * 1024.0));
                return false;
            }

            LogDiskSpaceReclaimComplete(run.BytesFreed / (1024.0 * 1024.0), run.DeletedFiles.Count);
            return true;
        }
        catch (Exception ex)
        {
            LogDiskSpaceReclaimFailed(ex);
            return true;
        }
    }

    /// <inheritdoc/>
    public string GenerateRecordingFilename(
        CameraConfiguration camera,
        string format)
    {
        ArgumentNullException.ThrowIfNull(camera);

        // File names keep their UTC timestamp; a day folder follows the local day.
        return UniqueFilename.EnsureUnique(RecordingPathHelper.BuildRecordingPath(
            GetEffectiveRecordingPath(camera),
            RecordingPathHelper.GetCameraFolderName(camera.Display.DisplayName, camera.Id),
            DateTime.UtcNow,
            format,
            RecordingLayoutOptions.From(settingsService.Recording)));
    }

    /// <inheritdoc/>
    public void StopAllRecordings()
    {
        foreach (var cameraId in sessions.Keys.ToList())
        {
            StopRecording(cameraId);
        }
    }

    /// <inheritdoc/>
    public bool SegmentRecording(Guid cameraId)
    {
        if (!sessions.TryGetValue(cameraId, out var session))
        {
            return false;
        }

        if (!pipelines.TryGetValue(cameraId, out var pipeline))
        {
            LogSegmentNoPipeline(cameraId);
            return false;
        }

        var camera = cameraStorageService.GetCameraById(cameraId);
        if (camera is null)
        {
            LogSegmentCameraNotFound(cameraId);
            return false;
        }

        var oldFilePath = session.CurrentFilePath;
        var newFilePath = GenerateRecordingFilename(camera, GetEffectiveRecordingFormat(camera));

        var directory = Path.GetDirectoryName(newFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        ReclaimDiskSpaceIfNeeded();

        // The old segment's thumbnail is complete; the new one starts below.
        StopThumbnail(cameraId);

        try
        {
            // Atomic switch: packets arriving from the demux thread mid-switch
            // land in either the previous segment or the new one — never the
            // close/open gap. Falls back to Stop+Start if the underlying
            // pipeline doesn't support atomic switching.
            pipeline.SwitchRecording(newFilePath);
        }
        catch (Exception ex)
        {
            LogSegmentFailed(ex, cameraId);
            return false;
        }

        var newSession = new RecordingSession(cameraId, session.CameraName, newFilePath, session.IsManualRecording)
        {
            LastMotionTime = session.LastMotionTime,
            State = session.State,
        };

        if (!sessions.TryUpdate(cameraId, newSession, session))
        {
            LogSegmentFailed(
                new InvalidOperationException("Concurrent modification during segment boundary"),
                cameraId);
            return false;
        }

        StartThumbnail(camera, pipeline, newFilePath);

        RaiseStateChanged(cameraId, session.State, RecordingState.Idle, oldFilePath);
        RaiseStateChanged(cameraId, RecordingState.Idle, newSession.State, newFilePath);

        LogSegmented(session.CameraName, cameraId, newFilePath);
        return true;
    }

    /// <inheritdoc/>
    public IReadOnlyList<RecordingSession> GetActiveSessions()
        => sessions.Values.ToList().AsReadOnly();

    /// <summary>
    /// Diagnostic snapshot of every active recording session, augmented with
    /// the underlying pipeline's IsRecordingActive flag so an operator can
    /// distinguish "recording" (session live and pipeline producing packets)
    /// from "stuck" (session live but pipeline died and reaper hasn't swept
    /// yet). Used by the /health/recordings endpoint.
    /// </summary>
    public IReadOnlyList<RecordingDiagnostics> GetDiagnostics()
    {
        var snapshot = sessions.Values.ToList();
        var diskSpaceLow = IsDiskSpaceLow();
        var result = new List<RecordingDiagnostics>(snapshot.Count);
        foreach (var session in snapshot)
        {
            var pipelineActive = pipelines.TryGetValue(session.CameraId, out var pipeline)
                && pipeline.IsRecordingActive;

            result.Add(new RecordingDiagnostics(
                session.CameraId,
                session.CameraName,
                session.CurrentFilePath,
                session.StartTime,
                session.Duration,
                pipelineActive)
            {
                DiskSpaceLow = diskSpaceLow,
            });
        }

        return result;
    }

    private bool IsDiskSpaceLow()
    {
        try
        {
            var cleanup = settingsService.Recording.Cleanup;
            if (!cleanup.EnableDiskSpaceGuard)
            {
                return false;
            }

            var recordingPath = settingsService.Recording.RecordingPath;
            var driveRoot = Path.GetPathRoot(Path.GetFullPath(recordingPath));
            if (string.IsNullOrEmpty(driveRoot))
            {
                return false;
            }

            var freeBytes = new DriveInfo(driveRoot).AvailableFreeSpace;
            var minFreeBytes = (long)cleanup.MinFreeSpaceMb * 1024L * 1024L;
            return freeBytes < minFreeBytes;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public int ReapInactiveSessions()
    {
        var reaped = 0;
        var staleThreshold = TimeSpan.FromSeconds(StalePacketThresholdSeconds);
        var nowUtc = DateTime.UtcNow;

        // Snapshot so we can mutate sessions/pipelines via StopRecording inside
        // the loop without ConcurrentDictionary enumeration surprises.
        foreach (var cameraId in sessions.Keys.ToList())
        {
            if (!sessions.TryGetValue(cameraId, out var session))
            {
                continue;
            }

            if (!pipelines.TryGetValue(cameraId, out var pipeline))
            {
                // Session without a pipeline = stale entry; clear it.
                LogReapingInactiveSession(session.CameraName, cameraId, "no pipeline registered");
                StopRecording(cameraId);
                reaped++;
                continue;
            }

            if (!pipeline.IsRecordingActive)
            {
                LogReapingInactiveSession(session.CameraName, cameraId, "pipeline IsRecordingActive=false");
                StopRecording(cameraId);
                reaped++;
                continue;
            }

            // Stream-stale watchdog: a pipeline can have IsRecordingActive=true
            // while wedged (RTP packets stop arriving but the underlying socket
            // is technically still open). The VideoEngine's consecutive-read-
            // errors detector only fires on read errors, not silent stalls, so
            // without this watchdog the session would stay "Recording" until
            // the next IsRecordingActive flip (which may never come). Skip
            // sessions that haven't seen their first packet yet (LastPacketUtc
            // == MinValue) — they're either still opening or genuinely brand
            // new; let CCM/StartRecording handle that path instead.
            var lastPacket = pipeline.LastPacketUtc;
            if (lastPacket == DateTime.MinValue)
            {
                continue;
            }

            var idleFor = nowUtc - lastPacket;
            if (idleFor > staleThreshold)
            {
                LogReapingInactiveSession(
                    session.CameraName,
                    cameraId,
                    $"no packets for {(int)idleFor.TotalSeconds}s (threshold {StalePacketThresholdSeconds}s)");
                StopRecording(cameraId);
                reaped++;
            }
        }

        if (reaped > 0)
        {
            LogReaperSwept(reaped);
        }

        return reaped;
    }

    /// <inheritdoc/>
    public bool EnforceDiskSpaceGuard()
    {
        if (sessions.IsEmpty)
        {
            return false;
        }

        if (!ReclaimDiskSpaceIfNeeded())
        {
            LogDiskSpaceGuardStoppingAllRecordings(sessions.Count);
            StopAllRecordings();
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        StopAllRecordings();
    }

    /// <summary>
    /// Starts capturing the thumbnail of a recording segment. A thumbnail is
    /// a nicety: failing to make one never stops or fails the recording.
    /// </summary>
    private void StartThumbnail(
        CameraConfiguration camera,
        IMediaPipeline pipeline,
        string videoFilePath)
    {
        if (thumbnailService is null)
        {
            return;
        }

        try
        {
            thumbnailService.StartCapture(
                camera.Id,
                camera.Display.DisplayName,
                pipeline,
                videoFilePath,
                camera.Overrides?.Recording.ThumbnailTileCount ?? settingsService.Recording.ThumbnailTileCount,
                RecordingPathHelper.BuildThumbnailPath(
                    videoFilePath,
                    GetEffectiveRecordingPath(camera),
                    RecordingLayoutOptions.From(settingsService.Recording)));
        }
        catch (Exception ex)
        {
            LogThumbnailFailed(ex, camera.Id);
        }
    }

    private void StopThumbnail(Guid cameraId)
    {
        if (thumbnailService is null)
        {
            return;
        }

        try
        {
            thumbnailService.StopCapture(cameraId);
        }
        catch (Exception ex)
        {
            LogThumbnailFailed(ex, cameraId);
        }
    }

    private string GetEffectiveRecordingPath(CameraConfiguration camera)
    {
        var overridePath = camera.Overrides?.Recording.RecordingPath;
        return !string.IsNullOrEmpty(overridePath) ? overridePath : settingsService.Recording.RecordingPath;
    }

    private string GetEffectiveRecordingFormat(CameraConfiguration camera)
    {
        var overrideFormat = camera.Overrides?.Recording.RecordingFormat;
        return !string.IsNullOrEmpty(overrideFormat) ? overrideFormat : settingsService.Recording.RecordingFormat;
    }

    private void RaiseStateChanged(
        Guid cameraId,
        RecordingState oldState,
        RecordingState newState,
        string? filePath)
    {
        RecordingStateChanged?.Invoke(this, new RecordingStateChangedEventArgs(
            cameraId,
            oldState,
            newState,
            filePath));
    }
}