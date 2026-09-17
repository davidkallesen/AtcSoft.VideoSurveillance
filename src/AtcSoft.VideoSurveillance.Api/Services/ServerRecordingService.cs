namespace Linksoft.VideoSurveillance.Api.Services;

/// <summary>
/// Server-side implementation of <see cref="IRecordingService"/> using FFmpeg media pipelines.
/// </summary>
public sealed partial class ServerRecordingService : IRecordingService, IDisposable
{
    // Mirrors WPF's CameraTile stream-stale threshold — long enough to absorb
    // a slow keyframe interval, short enough to recover within one CCM tick.
    private const int StalePacketThresholdSeconds = 15;

    private static readonly string[] RecordingFileExtensions = [".mp4", ".mkv", ".avi"];

    private readonly IApplicationSettingsService settingsService;
    private readonly ICameraStorageService cameraStorageService;
    private readonly ILogger<ServerRecordingService> logger;
    private readonly ConcurrentDictionary<Guid, RecordingSession> sessions = new();
    private readonly ConcurrentDictionary<Guid, IMediaPipeline> pipelines = new();

    // Per-camera pipeline ConnectionStateChanged handlers, kept so we can
    // unsubscribe at StopRecording time. Subscribing once at StartRecording
    // gives the broadcaster a single aggregated event to listen to (vs.
    // subscribing per-pipeline at every creation site).
    private readonly ConcurrentDictionary<Guid, EventHandler<ConnectionStateChangedEventArgs>> connectionHandlers = new();

    public ServerRecordingService(
        IApplicationSettingsService settingsService,
        ICameraStorageService cameraStorageService,
        ILogger<ServerRecordingService> logger)
    {
        this.settingsService = settingsService;
        this.cameraStorageService = cameraStorageService;
        this.logger = logger;
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
    public RecordingState GetRecordingState(Guid cameraId)
        => sessions.ContainsKey(cameraId) ? RecordingState.Recording : RecordingState.Idle;

    /// <inheritdoc/>
    public RecordingSession? GetSession(Guid cameraId)
        => sessions.GetValueOrDefault(cameraId);

    /// <inheritdoc/>
    public bool StartRecording(
        CameraConfiguration camera,
        IMediaPipeline pipeline)
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

        var session = new RecordingSession(camera.Id, camera.Display.DisplayName, filePath, isManualRecording: true);

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

            try
            {
                pipeline.Dispose();
            }
            catch (Exception ex)
            {
                LogDisposePipelineFailed(ex, camera.Id);
            }

            return false;
        }

        pipelines[camera.Id] = pipeline;

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

        RaiseStateChanged(camera.Id, RecordingState.Idle, RecordingState.Recording, filePath);
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
        if (pipelines.TryRemove(cameraId, out var pipeline))
        {
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

            try
            {
                pipeline.Dispose();
            }
            catch (Exception ex)
            {
                LogDisposePipelineFailed(ex, cameraId);
            }
        }

        // The recording lifecycle ended — let connection-state subscribers
        // know the camera is no longer being observed by us. Mirrors the
        // synthetic Connected we fired in StartRecording.
        CameraConnectionStateChanged?.Invoke(cameraId, ConnectionState.Disconnected);

        RaiseStateChanged(cameraId, RecordingState.Recording, RecordingState.Idle, session.CurrentFilePath);
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

        if (IsRecording(camera.Id))
        {
            return false;
        }

        return StartRecording(camera, pipeline);
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
                    skipPaths.Add(Path.GetFullPath(Path.ChangeExtension(session.CurrentFilePath, ".png")));
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

        var safeName = string.IsNullOrWhiteSpace(camera.Display.DisplayName)
            ? camera.Id.ToString("N")[..8]
            : string.Join("_", camera.Display.DisplayName.Split(Path.GetInvalidFileNameChars()));

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        var ext = string.IsNullOrEmpty(format) ? "mp4" : format.TrimStart('.');

        return UniqueFilename.EnsureUnique(Path.Combine(
            GetEffectiveRecordingPath(camera),
            safeName,
            $"{safeName}_{timestamp}.{ext}"));
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