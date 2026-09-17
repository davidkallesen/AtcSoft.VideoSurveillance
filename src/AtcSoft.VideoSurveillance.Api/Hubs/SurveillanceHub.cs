namespace Linksoft.VideoSurveillance.Api.Hubs;

/// <summary>
/// SignalR hub for real-time surveillance events.
/// Broadcasts camera connection changes, recording state, and motion detection events.
/// Provides client-to-server methods for camera control.
/// </summary>
/// <remarks>
/// Server-to-client events (broadcast by <see cref="SurveillanceEventBroadcaster"/>):
/// - ConnectionStateChanged { CameraId, NewState, Timestamp }
/// - MotionDetected { CameraId, IsMotionActive, BoundingBoxes[], AnalysisWidth, AnalysisHeight }
/// - RecordingStateChanged { CameraId, NewState, OldState, FilePath }
/// </remarks>
public sealed partial class SurveillanceHub : Hub
{
    private readonly ICameraStorageService storage;
    private readonly IRecordingService recordingService;
    private readonly IMediaPipelineFactory pipelineFactory;
    private readonly StreamingService streamingService;
    private readonly ILogger<SurveillanceHub> logger;

    public SurveillanceHub(
        ICameraStorageService storage,
        IRecordingService recordingService,
        IMediaPipelineFactory pipelineFactory,
        StreamingService streamingService,
        ILogger<SurveillanceHub> logger)
    {
        this.storage = storage;
        this.recordingService = recordingService;
        this.pipelineFactory = pipelineFactory;
        this.streamingService = streamingService;
        this.logger = logger;
    }

    /// <summary>
    /// Called when a new client connects to the hub.
    /// </summary>
    public override Task OnConnectedAsync()
    {
        LogClientConnected(Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    /// <summary>
    /// Called when a client disconnects from the hub. Actively reaps any HLS
    /// streams this connection started so their CPU-heavy FFmpeg transcoders
    /// are stopped within seconds; otherwise a closed-tab client would let
    /// its streams run until the StreamingService inactivity timer expires.
    /// </summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var connectionId = Context.ConnectionId;
        LogClientDisconnected(connectionId);
        streamingService.OnConnectionDisconnected(connectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Starts recording for a camera.
    /// </summary>
    public async Task StartRecording(Guid cameraId)
    {
        var camera = storage.GetCameraById(cameraId);
        if (camera is null)
        {
            await Clients.Caller
                .SendAsync("Error", $"Camera {cameraId} not found.")
                .ConfigureAwait(false);
            return;
        }

        if (recordingService.IsRecording(cameraId))
        {
            await Clients.Caller
                .SendAsync("Error", $"Camera {cameraId} is already recording.")
                .ConfigureAwait(false);
            return;
        }

        var pipeline = pipelineFactory.Create(camera);
        var started = recordingService.StartRecording(camera, pipeline);

        // StartRecording returns false on a concurrent-start race (another
        // caller already created the session). The recording service did
        // NOT take ownership of our pipeline in that case — dispose it here
        // so the RTSP connection / decoder thread / GPU don't leak.
        if (!started)
        {
            pipeline.Dispose();
        }

        LogRecordingResult(started ? "started" : "failed to start", cameraId);
    }

    /// <summary>
    /// Stops recording for a camera.
    /// </summary>
    public Task StopRecording(Guid cameraId)
    {
        recordingService.StopRecording(cameraId);

        LogRecordingStopped(cameraId);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Swaps two adjacent cameras in the current layout.
    /// </summary>
    public Task SwapCameras(
        Guid cameraId,
        SwapDirection direction)
        => Clients.All.SendAsync(
            "CameraSwapRequested",
            new { CameraId = cameraId, Direction = direction.ToString() });

    /// <summary>
    /// Starts HLS streaming for a camera. Returns the playlist URL.
    /// Cancellation comes from <see cref="HubCallerContext.ConnectionAborted"/>
    /// directly rather than via a method parameter — SignalR's JSON binder
    /// treats a parameter-level CancellationToken as a required client
    /// argument and rejects the invocation with "Invocation provides 1
    /// argument(s) but target expects 2" before the method body runs.
    /// </summary>
    /// <param name="cameraId">The camera to stream.</param>
    public async Task StartStream(Guid cameraId)
    {
        var cancellationToken = Context.ConnectionAborted;
        var connectionId = Context.ConnectionId;
        try
        {
            var playlistPath = streamingService.StartStream(cameraId, connectionId);

            // Wait for FFmpeg to create the playlist and first segment (up to 60 seconds).
            // 60s gives a comfortable margin for cameras with long GOPs and slow RTSP
            // handshakes; the StreamingService FFmpeg invocation now uses split_by_time
            // so segments cut at hls_time boundaries instead of keyframes, but a slow
            // camera or network can still take noticeably longer than the 30s we used
            // to bound here.
            var timeout = TimeSpan.FromSeconds(60);
            var start = DateTime.UtcNow;
            var playlistDir = Path.GetDirectoryName(playlistPath)!;

            while (!File.Exists(playlistPath)
                   || !Directory.EnumerateFiles(playlistDir, "*.ts").Any())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    streamingService.StopStream(cameraId, connectionId);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (streamingService.HasProcessExited(cameraId))
                {
                    var errors = streamingService.GetProcessError(cameraId);
                    streamingService.StopStream(cameraId, connectionId);
                    throw new InvalidOperationException(
                        $"FFmpeg exited unexpectedly. Output: {errors}");
                }

                if (DateTime.UtcNow - start > timeout)
                {
                    var errors = streamingService.GetProcessError(cameraId);
                    streamingService.StopStream(cameraId, connectionId);
                    throw new TimeoutException(
                        $"FFmpeg did not produce the HLS playlist within {timeout.TotalSeconds:F0}s. " +
                        "The most common cause is a very long camera GOP (keyframe interval) — " +
                        "reduce it in the camera's encoder settings if possible. " +
                        $"Last FFmpeg output: {errors}");
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            var relativePath = Path.GetRelativePath(
                streamingService.HlsOutputRoot,
                playlistPath).Replace('\\', '/');

            await Clients.Caller
                .SendAsync(
                    "StreamStarted",
                    new { CameraId = cameraId, PlaylistUrl = $"/streams/{relativePath}" },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogStartStreamFailed(ex, cameraId);
            throw new HubException($"Failed to start stream: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops HLS streaming for a camera.
    /// </summary>
    public Task StopStream(Guid cameraId)
    {
        streamingService.StopStream(cameraId, Context.ConnectionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Refreshes the last-activity timestamp for a stream so the inactivity
    /// reaper does not stop it. Clients should call this periodically (e.g.
    /// every 30 s) while the user is watching the stream.
    /// </summary>
    public Task StreamHeartbeat(Guid cameraId)
    {
        streamingService.Heartbeat(cameraId);
        return Task.CompletedTask;
    }
}