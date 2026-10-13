namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// Background service that keeps open the cameras the server has work for,
/// and owns their pipelines:
/// a camera recorded on connect (<see cref="RecordingSettings.EnableRecordingOnConnect"/>)
/// is recorded as soon as it plays; a camera recorded on motion
/// (<see cref="RecordingSettings.EnableRecordingOnMotion"/>) is watched for
/// motion, and a motion records it on the same pipeline for as long as it
/// lasts. Both respect per-camera overrides.
/// </summary>
public sealed partial class CameraConnectionService : BackgroundServiceBase<CameraConnectionService>
{
    // A watched camera is not recording most of the time, so "not recording"
    // cannot tell that its pipeline died. Silence can: a camera that plays
    // sends packets all the time.
    private static readonly TimeSpan WatchedCameraSilenceLimit = TimeSpan.FromSeconds(30);

    private readonly ICameraStorageService storageService;
    private readonly IApplicationSettingsService settingsService;
    private readonly IRecordingService recordingService;
    private readonly IMediaPipelineFactory pipelineFactory;
    private readonly IUsbCameraLifecycleCoordinator usbLifecycle;
    private readonly IMotionDetectionService motionDetectionService;
    private readonly CameraConnectionTypeAutoDetector? connectionTypeAutoDetector;
    private readonly ILiveStreamStatus? liveStreams;
    private readonly ICameraConnectionStateSink? connectionStates;
    private readonly ConcurrentDictionary<Guid, IMediaPipeline> managedPipelines = new();
    private readonly ConcurrentDictionary<Guid, BackoffState> backoffs = new();

    public CameraConnectionService(
        ILogger<CameraConnectionService> logger,
        CameraConnectionServiceOptions options,
        IBackgroundServiceHealthService healthService,
        ICameraStorageService storageService,
        IApplicationSettingsService settingsService,
        IRecordingService recordingService,
        IMediaPipelineFactory pipelineFactory,
        IUsbCameraLifecycleCoordinator usbLifecycle,
        IMotionDetectionService motionDetectionService,
        CameraConnectionTypeAutoDetector? connectionTypeAutoDetector = null,
        ILiveStreamStatus? liveStreams = null,
        ICameraConnectionStateSink? connectionStates = null)
        : base(logger, options, healthService)
    {
        this.storageService = storageService;
        this.settingsService = settingsService;
        this.recordingService = recordingService;
        this.pipelineFactory = pipelineFactory;
        this.usbLifecycle = usbLifecycle;
        this.motionDetectionService = motionDetectionService;
        this.connectionTypeAutoDetector = connectionTypeAutoDetector;
        this.liveStreams = liveStreams;
        this.connectionStates = connectionStates;

        this.motionDetectionService.MotionDetected += OnMotionDetected;

        // Subscribe before Start so we never miss the first event the
        // watcher emits after subscription. Start is idempotent.
        this.usbLifecycle.StateChanged += OnUsbLifecycleChanged;
        this.usbLifecycle.Start();

        healthService.SetMaxStalenessInSeconds(
            ServiceName,
            BackgroundServiceHealthHelper.StalenessFor(options));
    }

    /// <summary>
    /// Gets the pipeline this service keeps open for a camera.
    /// </summary>
    /// <param name="cameraId">The camera.</param>
    /// <returns>The pipeline, or <see langword="null"/> when the camera is not kept open.</returns>
    public IMediaPipeline? FindPipeline(Guid cameraId)
        => managedPipelines.GetValueOrDefault(cameraId);

    /// <inheritdoc />
    public override Task DoWorkAsync(CancellationToken stoppingToken)
    {
        // Reap any session whose pipeline has died — including ones started
        // via REST or SignalR which CCM doesn't track in managedPipelines.
        // Without this, a single network blip on an API-started recording
        // leaves the session "Recording" forever with no recovery.
        recordingService.ReapInactiveSessions();

        var cameras = storageService.GetAllCameras();
        var appDefault = settingsService.Recording.EnableRecordingOnConnect;

        foreach (var camera in cameras)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // The pipeline was rotated when it was created. A rotation set
            // through the API afterwards reaches it here, so snapshots follow
            // at once and recordings from the next segment.
            if (managedPipelines.TryGetValue(camera.Id, out var running))
            {
                running.SetRotation(camera.Display.Rotation);
            }

            // Skip USB cameras whose physical device is currently
            // unplugged. Reconnecting is impossible until the watcher
            // reports the device back, so polling here would just
            // reproduce the ~1 M attempts/year anti-pattern in a more
            // expensive way (we'd actually try to open the dshow
            // source and burn FFmpeg time on each tick).
            if (usbLifecycle.IsUnplugged(camera.Id))
            {
                if (managedPipelines.ContainsKey(camera.Id))
                {
                    if (recordingService.IsRecording(camera.Id))
                    {
                        try
                        {
                            recordingService.StopRecording(camera.Id);
                        }
                        catch (Exception ex)
                        {
                            LogStopRecordingShutdownError(ex, camera.Id);
                        }
                    }

                    ScheduleDeferredDisposal(camera.Id);
                }

                continue;
            }

            // Skip cameras still inside their backoff window so a
            // persistently dead camera doesn't generate ~1 M failed attempts
            // per year.
            if (backoffs.TryGetValue(camera.Id, out var bs)
                && DateTime.UtcNow < bs.NextAttemptUtc)
            {
                continue;
            }

            try
            {
                var recordOnConnect = RecordingPolicyHelper.ShouldRecordOnConnect(camera, appDefault);
                var watchForMotion = ShouldWatchForMotion(camera);

                // Motion recording was turned off: the camera is no longer watched.
                if (!watchForMotion && motionDetectionService.IsDetectionActive(camera.Id))
                {
                    StopWatching(camera.Id);
                }

                if (!recordOnConnect)
                {
                    if (watchForMotion)
                    {
                        EnsureWatching(camera);
                    }
                    else
                    {
                        // Nothing left to do with the camera. A recording
                        // someone started on it is theirs and is left alone.
                        if (managedPipelines.ContainsKey(camera.Id) && !recordingService.IsRecording(camera.Id))
                        {
                            ReleaseWatchedCamera(camera.Id);
                        }

                        LogRecordingOnConnectDisabled(camera.Id, camera.Display.DisplayName);
                    }

                    continue;
                }

                // Drop stale managedPipelines references whose underlying
                // pipeline is no longer producing output. Two paths reach
                // this state:
                //   1. The pipeline died but recordingService still has the
                //      session (we tell it to stop, which disposes).
                //   2. The session was already cleared (by the reaper at the
                //      top of this tick, or an external Stop) but CCM's
                //      reference lingers. Without dropping it, the
                //      TryStartRecordingForPipeline branch below would keep
                //      hitting the disposed pipeline (FramesDecoded == 0)
                //      and never recreate it.
                if (managedPipelines.TryGetValue(camera.Id, out var existingPipeline) &&
                    !existingPipeline.IsRecordingActive)
                {
                    LogDeadPipelineDetected(camera.Id, camera.Display.DisplayName);

                    if (recordingService.IsRecording(camera.Id))
                    {
                        recordingService.StopRecording(camera.Id);
                    }

                    managedPipelines.TryRemove(camera.Id, out _);
                }

                if (recordingService.IsRecording(camera.Id))
                {
                    continue;
                }

                // Pipeline already created — try to start recording if player is now playing
                if (managedPipelines.TryGetValue(camera.Id, out var pending))
                {
                    TryStartRecordingForPipeline(camera, pending);
                    continue;
                }

                LogStartingRecordingOnConnect(camera.Id, camera.Display.DisplayName);

                var pipeline = pipelineFactory.Create(camera);
                managedPipelines[camera.Id] = pipeline;

                // Open() is non-blocking — start recording when the player reaches Playing state
                var capturedCamera = camera;
                pipeline.ConnectionStateChanged += (_, e) =>
                    OnManagedPipelineConnected(capturedCamera, e);
            }
            catch (Exception ex)
            {
                LogStartRecordingFailed(ex, camera.Id, camera.Display.DisplayName);
                RecordFailure(camera);
                RemoveAndDisposePipeline(camera.Id);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gracefully stops all managed recordings before the service shuts down.
    /// This runs during the hosted-service shutdown phase which has the full
    /// shutdown timeout (30 s by default), unlike Dispose which runs during
    /// DI container teardown when time may be limited.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        StopAllManagedRecordings();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        usbLifecycle.StateChanged -= OnUsbLifecycleChanged;
        motionDetectionService.MotionDetected -= OnMotionDetected;

        // Pipelines should already be stopped by StopAsync; dispose cleans up remaining resources
        foreach (var kvp in managedPipelines)
        {
            motionDetectionService.StopDetection(kvp.Key);

            try
            {
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                LogDisposePipelineError(ex, kvp.Key);
            }
        }

        managedPipelines.Clear();
        base.Dispose();
    }

    private bool ShouldWatchForMotion(CameraConfiguration camera)
        => MotionDetectionPolicy.ShouldRecordOnMotion(camera, settingsService.Recording.EnableRecordingOnMotion);

    /// <summary>
    /// Keeps a camera that is recorded on motion open. Its pipeline is not
    /// recording most of the time, so it is checked by whether the camera
    /// still sends anything.
    /// </summary>
    private void EnsureWatching(CameraConfiguration camera)
    {
        // Windows gives a USB camera to one program at a time. Opened a
        // second time it connects and then delivers nothing, so a camera
        // someone is watching live is left to them until they are done.
        if (camera.Connection.Source == CameraSource.Usb && liveStreams?.IsStreaming(camera.Id) == true)
        {
            return;
        }

        if (managedPipelines.TryGetValue(camera.Id, out var watching))
        {
            var lastPacket = watching.LastPacketUtc;
            if (lastPacket != DateTime.MinValue && DateTime.UtcNow - lastPacket > WatchedCameraSilenceLimit)
            {
                LogDeadPipelineDetected(camera.Id, camera.Display.DisplayName);
                ReleaseWatchedCamera(camera.Id);
                return;
            }

            // The state change was missed (it can come before the handler is
            // attached): a camera that plays is watched from here.
            if (watching.FramesDecoded > 0 && !motionDetectionService.IsDetectionActive(camera.Id))
            {
                StartWatching(camera, watching);
            }

            return;
        }

        LogStartingMotionWatch(camera.Id, camera.Display.DisplayName);

        var pipeline = pipelineFactory.Create(camera);
        managedPipelines[camera.Id] = pipeline;

        var capturedCamera = camera;
        pipeline.ConnectionStateChanged += (_, e) =>
            OnManagedPipelineConnected(capturedCamera, e);
    }

    private void StartWatching(
        CameraConfiguration camera,
        IMediaPipeline pipeline)
    {
        try
        {
            motionDetectionService.StartDetection(
                camera.Id,
                pipeline,
                MotionDetectionPolicy.Resolve(camera, settingsService.MotionDetection));
        }
        catch (Exception ex)
        {
            LogStartMotionWatchFailed(ex, camera.Id, camera.Display.DisplayName);
        }
    }

    /// <summary>
    /// Stops watching a camera for motion, and the recording a motion
    /// started. A recording started any other way is left running.
    /// </summary>
    private void StopWatching(Guid cameraId)
    {
        motionDetectionService.StopDetection(cameraId);
        StopMotionRecording(cameraId);
    }

    private void StopMotionRecording(Guid cameraId)
    {
        try
        {
            if (recordingService.GetSession(cameraId) is { IsManualRecording: false })
            {
                recordingService.StopRecording(cameraId);
            }
        }
        catch (Exception ex)
        {
            LogStopRecordingShutdownError(ex, cameraId);
        }
    }

    /// <summary>
    /// Stops watching a camera and closes its pipeline.
    /// </summary>
    private void ReleaseWatchedCamera(Guid cameraId)
    {
        StopWatching(cameraId);
        ScheduleDeferredDisposal(cameraId);
        connectionStates?.Report(cameraId, ConnectionState.Disconnected);
    }

    /// <summary>
    /// A motion on a camera that is only recorded on motion starts a
    /// recording on the pipeline that watches it; the end of the motion
    /// stops it. Runs on the detector's thread.
    /// </summary>
    private void OnMotionDetected(
        object? sender,
        MotionDetectedEventArgs e)
    {
        if (!managedPipelines.TryGetValue(e.CameraId, out var pipeline))
        {
            return;
        }

        if (!e.IsMotionActive)
        {
            StopMotionRecording(e.CameraId);
            return;
        }

        try
        {
            var camera = storageService.GetCameraById(e.CameraId);
            if (camera is null ||
                !ShouldWatchForMotion(camera) ||
                RecordingPolicyHelper.ShouldRecordOnConnect(camera, settingsService.Recording.EnableRecordingOnConnect))
            {
                return;
            }

            recordingService.TriggerMotionRecording(camera, pipeline);
        }
        catch (Exception ex)
        {
            LogStartMotionRecordingFailed(ex, e.CameraId);
        }
    }

    /// <summary>
    /// Reacts to USB hot-plug transitions surfaced by
    /// <see cref="IUsbCameraLifecycleCoordinator"/>. Unplug → tear
    /// down the active pipeline + recording so resources are freed
    /// promptly. Replug → clear the backoff entry so the next
    /// <see cref="DoWorkAsync"/> tick attempts the camera fresh, no
    /// matter how long it was gone.
    /// </summary>
    private void OnUsbLifecycleChanged(
        object? sender,
        UsbCameraLifecycleChangedEventArgs e)
    {
        switch (e.Phase)
        {
            case UsbCameraLifecyclePhase.Unplugged:
                LogUsbCameraUnplugged(e.CameraId, e.Device.FriendlyName);

                if (recordingService.IsRecording(e.CameraId))
                {
                    try
                    {
                        recordingService.StopRecording(e.CameraId);
                    }
                    catch (Exception ex)
                    {
                        LogStopRecordingShutdownError(ex, e.CameraId);
                    }
                }

                if (managedPipelines.ContainsKey(e.CameraId))
                {
                    ScheduleDeferredDisposal(e.CameraId);
                }

                break;

            case UsbCameraLifecyclePhase.Replugged:
                // Clearing the backoff is the trigger that lets the
                // next DoWorkAsync tick re-attempt the camera. Without
                // this, an unplug while in the middle of a long
                // backoff window would survive the replug.
                backoffs.TryRemove(e.CameraId, out _);
                LogUsbCameraReplugged(e.CameraId, e.Device.FriendlyName);
                break;
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The pipeline is disposed right after it is taken out of the dictionary.")]
    private void StopAllManagedRecordings()
    {
        foreach (var cameraId in managedPipelines.Keys.ToList())
        {
            motionDetectionService.StopDetection(cameraId);

            try
            {
                LogStoppingManagedRecording(cameraId);
                recordingService.StopRecording(cameraId);
            }
            catch (Exception ex)
            {
                LogStopRecordingShutdownError(ex, cameraId);
            }

            // recordingService.StopRecording closes the pipeline of a
            // recording that owns it, but not the one a motion recording
            // borrowed, nor that of a camera that was only watched. Closing
            // it again does no harm.
            if (managedPipelines.TryRemove(cameraId, out var pipeline))
            {
                try
                {
                    pipeline.Dispose();
                }
                catch (Exception ex)
                {
                    LogDisposePipelineError(ex, cameraId);
                }
            }
        }
    }

    private void TryStartRecordingForPipeline(
        CameraConfiguration camera,
        IMediaPipeline pipeline)
    {
        // FramesDecoded > 0 means the player is playing and decoding — safe to start recording
        if (pipeline.FramesDecoded <= 0)
        {
            return;
        }

        try
        {
            if (recordingService.StartRecording(camera, pipeline))
            {
                LogRecordingOnConnectStartedFallback(camera.Id, camera.Display.DisplayName);
                return;
            }

            // Another path (REST/SignalR) won the race and is already
            // recording. Our pipeline isn't owned by the recording service
            // — drop it so we don't leak two demuxers per camera.
            ScheduleDeferredDisposal(camera.Id);
        }
        catch (Exception ex)
        {
            LogStartRecordingFallbackFailed(ex, camera.Id, camera.Display.DisplayName);
        }
    }

    /// <summary>
    /// Handles pipeline state changes. Runs on the demux thread — must NOT
    /// call <see cref="RemoveAndDisposePipeline"/> synchronously because
    /// Dispose → Join would deadlock (self-join on the demux thread).
    /// </summary>
    private void OnManagedPipelineConnected(
        CameraConfiguration camera,
        ConnectionStateChangedEventArgs e)
    {
        if (e.NewState == ConnectionState.Connected)
        {
            // Successful connect — clear any prior backoff so the next
            // disconnect starts fresh from the base delay. If there was a
            // prior failure streak, surface it so soak operators can see how
            // many attempts the recovery took.
            if (backoffs.TryRemove(camera.Id, out var clearedBackoff))
            {
                LogReconnected(camera.Display.DisplayName, camera.Id, clearedBackoff.ConsecutiveFailures);
            }

            LogCameraConnected(camera.Id, camera.Display.DisplayName);

            // Implicit connection-type detection (once per camera, fills "not set" only).
            _ = connectionTypeAutoDetector?.OnCameraConnectedAsync(camera);

            if (!managedPipelines.TryGetValue(camera.Id, out var pipeline))
            {
                return;
            }

            if (ShouldWatchForMotion(camera))
            {
                StartWatching(camera, pipeline);
            }

            // A camera that is only watched is not recorded until it moves.
            if (!RecordingPolicyHelper.ShouldRecordOnConnect(camera, settingsService.Recording.EnableRecordingOnConnect))
            {
                connectionStates?.Report(camera.Id, ConnectionState.Connected);
                return;
            }

            if (recordingService.IsRecording(camera.Id))
            {
                return;
            }

            try
            {
                if (recordingService.StartRecording(camera, pipeline))
                {
                    LogRecordingOnConnectStarted(camera.Id, camera.Display.DisplayName);
                    return;
                }

                // Race lost: another caller started this camera between the
                // IsRecording check above and here. Our pipeline isn't owned
                // by the recording service — schedule disposal (deferred to
                // the ThreadPool because we're on the demux thread).
                ScheduleDeferredDisposal(camera.Id);
            }
            catch (Exception ex)
            {
                LogStartRecordingAfterConnectFailed(ex, camera.Id, camera.Display.DisplayName);

                // Defer disposal — this handler runs on the demux thread,
                // and Dispose joins that thread (self-join deadlock).
                ScheduleDeferredDisposal(camera.Id);
            }
        }
        else if (e.NewState == ConnectionState.Disconnected)
        {
            LogCameraDisconnected(camera.Id, camera.Display.DisplayName);
        }
        else if (e.NewState == ConnectionState.Error)
        {
            LogPipelineConnectionFailed(camera.Id, camera.Display.DisplayName);
            RecordFailure(camera);
            StopWatching(camera.Id);
            ScheduleDeferredDisposal(camera.Id);

            // A recorded camera's state is reported by its recording. One
            // that was only watched has nobody else to say it is gone.
            if (!RecordingPolicyHelper.ShouldRecordOnConnect(camera, settingsService.Recording.EnableRecordingOnConnect))
            {
                connectionStates?.Report(camera.Id, ConnectionState.Disconnected);
            }
        }
    }

    // Bumps the camera's consecutive-failure count and schedules the next
    // attempt using capped exponential backoff; called from the demux
    // thread on Error events. The camera reference (rather than just the ID)
    // is needed so the log line can include the friendly name without an
    // extra storage lookup on the hot path.
    private void RecordFailure(CameraConfiguration camera)
    {
        var updated = backoffs.AddOrUpdate(
            camera.Id,
            _ => new BackoffState
            {
                ConsecutiveFailures = 1,
                NextAttemptUtc = DateTime.UtcNow + ReconnectBackoff.ComputeDelay(1),
            },
            (_, prev) =>
            {
                var next = prev.ConsecutiveFailures + 1;
                return new BackoffState
                {
                    ConsecutiveFailures = next,
                    NextAttemptUtc = DateTime.UtcNow + ReconnectBackoff.ComputeDelay(next),
                };
            });

        var backoffSeconds = (int)(updated.NextAttemptUtc - DateTime.UtcNow).TotalSeconds;
        LogBackoffScheduled(
            camera.Display.DisplayName,
            camera.Id,
            updated.ConsecutiveFailures,
            backoffSeconds,
            updated.NextAttemptUtc);
    }

    private sealed class BackoffState
    {
        public int ConsecutiveFailures { get; init; }

        public DateTime NextAttemptUtc { get; init; }
    }

    [SuppressMessage(
        "Usage",
        "CA2000:Dispose objects before losing scope",
        Justification = "Pipeline is disposed immediately after removal from the dictionary, which ensures no further references and prevents self-join deadlocks on the demux thread.")]
    private void RemoveAndDisposePipeline(Guid cameraId)
    {
        if (managedPipelines.TryRemove(cameraId, out var pipeline))
        {
            // The detector must not be left asking a closed pipeline for pictures.
            motionDetectionService.StopDetection(cameraId);

            try
            {
                pipeline.Dispose();
            }
            catch (Exception ex)
            {
                LogDisposeDeadPipelineError(ex, cameraId);
            }
        }
    }

    /// <summary>
    /// Removes the pipeline from the managed dictionary and disposes it on a
    /// ThreadPool thread. This prevents self-join deadlocks when called from
    /// the pipeline's demux thread (via the ConnectionStateChanged event).
    /// </summary>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Pipeline is unconditionally disposed asynchronously on the ThreadPool inside the queued work item.")]
    private void ScheduleDeferredDisposal(Guid cameraId)
    {
        if (managedPipelines.TryRemove(cameraId, out var pipeline))
        {
            // The detector must not be left asking a closed pipeline for pictures.
            motionDetectionService.StopDetection(cameraId);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    pipeline.Dispose();
                }
                catch (Exception ex)
                {
                    LogDisposePipelineDeferredError(ex, cameraId);
                }
            });
        }
    }
}