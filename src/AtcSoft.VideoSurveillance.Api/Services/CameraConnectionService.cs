namespace Linksoft.VideoSurveillance.Api.Services;

/// <summary>
/// Background service that proactively connects cameras and starts recording
/// when <see cref="RecordingSettings.EnableRecordingOnConnect"/> is enabled
/// (respecting per-camera overrides).
/// </summary>
public sealed partial class CameraConnectionService : BackgroundServiceBase<CameraConnectionService>
{
    private readonly ICameraStorageService storageService;
    private readonly IApplicationSettingsService settingsService;
    private readonly IRecordingService recordingService;
    private readonly IMediaPipelineFactory pipelineFactory;
    private readonly IUsbCameraLifecycleCoordinator usbLifecycle;
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
        IUsbCameraLifecycleCoordinator usbLifecycle)
        : base(logger, options, healthService)
    {
        this.storageService = storageService;
        this.settingsService = settingsService;
        this.recordingService = recordingService;
        this.pipelineFactory = pipelineFactory;
        this.usbLifecycle = usbLifecycle;

        // Subscribe before Start so we never miss the first event the
        // watcher emits after subscription. Start is idempotent.
        this.usbLifecycle.StateChanged += OnUsbLifecycleChanged;
        this.usbLifecycle.Start();

        healthService.SetMaxStalenessInSeconds(
            ServiceName,
            BackgroundServiceHealthHelper.StalenessFor(options));
    }

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
                if (!RecordingPolicyHelper.ShouldRecordOnConnect(camera, appDefault))
                {
                    LogRecordingOnConnectDisabled(camera.Id, camera.Display.DisplayName);
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

        // Pipelines should already be stopped by StopAsync; dispose cleans up remaining resources
        foreach (var kvp in managedPipelines)
        {
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

    private void StopAllManagedRecordings()
    {
        foreach (var cameraId in managedPipelines.Keys.ToList())
        {
            try
            {
                LogStoppingManagedRecording(cameraId);
                recordingService.StopRecording(cameraId);
            }
            catch (Exception ex)
            {
                LogStopRecordingShutdownError(ex, cameraId);
            }

            // recordingService.StopRecording disposes the pipeline; drop the
            // now-disposed reference so CCM.Dispose doesn't redundantly
            // re-dispose (the call is idempotent but the iteration is wasted).
            managedPipelines.TryRemove(cameraId, out _);
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

            if (recordingService.IsRecording(camera.Id) ||
                !managedPipelines.TryGetValue(camera.Id, out var pipeline))
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
            ScheduleDeferredDisposal(camera.Id);
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