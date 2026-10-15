namespace AtcSoft.VideoSurveillance.Api.Hubs;

/// <summary>
/// SignalR hub for real-time surveillance events and live streams.
/// </summary>
/// <remarks>
/// The hub is declared in the specification (<c>x-signalr-hubs</c>): the events
/// (<see cref="ISurveillanceHubClient"/>, most of them sent by
/// <see cref="SurveillanceEventBroadcaster"/>) and the methods
/// (<see cref="ISurveillanceHub"/>) and what they carry are generated from it.
/// </remarks>
public sealed partial class SurveillanceHub : Hub<ISurveillanceHubClient>, ISurveillanceHub
{
    private readonly StreamingService streamingService;
    private readonly ILogger<SurveillanceHub> logger;

    public SurveillanceHub(
        StreamingService streamingService,
        ILogger<SurveillanceHub> logger)
    {
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
                        string.IsNullOrWhiteSpace(errors)
                            ? "The camera could not be opened."
                            : $"The stream ended before it had started. Output: {errors}");
                }

                if (DateTime.UtcNow - start > timeout)
                {
                    var errors = streamingService.GetProcessError(cameraId);
                    streamingService.StopStream(cameraId, connectionId);
                    throw new TimeoutException(
                        $"The stream did not produce its HLS playlist within {timeout.TotalSeconds:F0}s. " +
                        "The most common cause is a very long camera GOP (keyframe interval) — " +
                        "reduce it in the camera's encoder settings if possible. " +
                        $"Last output: {errors}");
                }

                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }

            var relativePath = Path.GetRelativePath(
                streamingService.HlsOutputRoot,
                playlistPath).Replace('\\', '/');

            await Clients.Caller
                .StreamStarted(new StreamStartedEvent(cameraId, $"/streams/{relativePath}"))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogStartStreamFailed(ex, cameraId);
            throw new HubException($"Failed to start stream: {ex.Message}", ex);
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