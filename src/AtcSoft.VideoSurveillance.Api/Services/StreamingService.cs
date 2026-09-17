namespace Linksoft.VideoSurveillance.Api.Services;

/// <summary>
/// Manages per-camera FFmpeg transcoding processes that convert RTSP streams
/// to HLS segments for browser consumption.
/// </summary>
public sealed partial class StreamingService : IDisposable
{
    // A client that drops its socket without calling StopStream leaves the
    // viewer count > 0 and the FFmpeg transcoder running. With CPU-heavy
    // libx264 transcoding per camera, even a few orphaned streams can
    // saturate a multi-camera server.
    //
    // 60s inactivity with a 30s client heartbeat gives 30s of margin — a
    // single dropped heartbeat (Wi-Fi blip, GC pause) won't kill the stream.
    // The sweep cadence lives in HlsStreamReaperService (the hosted worker that
    // drives ReapIdleSessions); worst-case lag from drop to reap is ~70s.
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(60);

    private readonly ICameraStorageService storage;
    private readonly ILogger<StreamingService> logger;
    private readonly ConcurrentDictionary<Guid, StreamSession> sessions = new();

    // connectionId → set of cameraIds that connection started. Used by the
    // SignalR hub's OnDisconnectedAsync to actively reap streams the
    // disconnecting client owned without waiting for the inactivity timer.
    private readonly ConcurrentDictionary<string, HashSet<Guid>> ownership =
        new(StringComparer.Ordinal);

    private readonly Lock ownershipLock = new();
    private readonly string hlsOutputRoot;
    private bool disposed;

    public StreamingService(
        ICameraStorageService storage,
        ILogger<StreamingService> logger)
    {
        this.storage = storage;
        this.logger = logger;

        hlsOutputRoot = Path.Combine(Path.GetTempPath(), "linksoft-hls");
        Directory.CreateDirectory(hlsOutputRoot);
    }

    /// <summary>
    /// Gets the HLS output directory root path.
    /// </summary>
    public string HlsOutputRoot => hlsOutputRoot;

    /// <summary>
    /// Starts HLS streaming for a camera. Returns the playlist path.
    /// If already streaming, increments the viewer count and returns existing path.
    /// </summary>
    /// <param name="cameraId">Camera to start streaming.</param>
    /// <param name="connectionId">
    /// Optional SignalR connection identifier; when provided, the stream is
    /// registered as owned by that connection so a subsequent
    /// <see cref="OnConnectionDisconnected"/> can immediately decrement the
    /// viewer count without waiting for the inactivity timer.
    /// </param>
    public string StartStream(
        Guid cameraId,
        string? connectionId = null)
    {
        var session = sessions.GetOrAdd(cameraId, id => CreateSession(id));

        session.IncrementViewers();
        session.TouchActivity();

        if (!string.IsNullOrEmpty(connectionId))
        {
            RegisterOwnership(connectionId, cameraId);
        }

        return session.PlaylistPath;
    }

    /// <summary>
    /// Drops the per-stream viewer count for every camera the given
    /// connection started. Called by <c>SurveillanceHub.OnDisconnectedAsync</c>
    /// so a closing browser tab releases its FFmpeg transcoders within
    /// seconds instead of waiting for the inactivity timeout.
    /// </summary>
    public void OnConnectionDisconnected(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
        {
            return;
        }

        Guid[] cameraIds;
        lock (ownershipLock)
        {
            if (!ownership.TryRemove(connectionId, out var set))
            {
                return;
            }

            cameraIds = [.. set];
        }

        foreach (var cameraId in cameraIds)
        {
            StopStream(cameraId);
        }
    }

    private void RegisterOwnership(
        string connectionId,
        Guid cameraId)
    {
        lock (ownershipLock)
        {
            var set = ownership.GetOrAdd(connectionId, _ => []);
            set.Add(cameraId);
        }
    }

    private void RemoveOwnership(
        string connectionId,
        Guid cameraId)
    {
        lock (ownershipLock)
        {
            if (ownership.TryGetValue(connectionId, out var set))
            {
                set.Remove(cameraId);
                if (set.Count == 0)
                {
                    ownership.TryRemove(connectionId, out _);
                }
            }
        }
    }

    /// <summary>
    /// Refreshes the last-activity timestamp for a stream so it isn't reaped
    /// for inactivity. Clients should call this periodically (e.g. once per
    /// HLS playlist poll) so a dropped connection eventually triggers reap.
    /// </summary>
    public void Heartbeat(Guid cameraId)
    {
        if (sessions.TryGetValue(cameraId, out var session))
        {
            session.TouchActivity();
        }
    }

    /// <summary>
    /// Decrements the viewer count for a camera stream.
    /// Stops the FFmpeg process when no viewers remain.
    /// </summary>
    /// <param name="cameraId">Camera to stop streaming.</param>
    /// <param name="connectionId">
    /// Optional SignalR connection identifier; when provided, removes this
    /// connection's claim on the stream so it isn't reaped twice when the
    /// connection later disconnects.
    /// </param>
    public void StopStream(
        Guid cameraId,
        string? connectionId = null)
    {
        if (!string.IsNullOrEmpty(connectionId))
        {
            RemoveOwnership(connectionId, cameraId);
        }

        if (!sessions.TryGetValue(cameraId, out var session))
        {
            return;
        }

        var remaining = session.DecrementViewers();

        if (remaining <= 0 && sessions.TryRemove(cameraId, out var removed))
        {
            removed.Dispose();
            LogHlsStreamStopped(cameraId);
        }
    }

    /// <summary>
    /// Gets whether a camera is currently streaming.
    /// </summary>
    public bool IsStreaming(Guid cameraId)
        => sessions.ContainsKey(cameraId);

    /// <summary>
    /// Gets the current viewer count for a camera stream.
    /// </summary>
    public int GetViewerCount(Guid cameraId)
        => sessions.TryGetValue(cameraId, out var session)
            ? session.ViewerCount
            : 0;

    /// <summary>
    /// Gets the HLS playlist path for a camera, or null if not streaming.
    /// </summary>
    public string? GetPlaylistPath(Guid cameraId)
        => sessions.TryGetValue(cameraId, out var session)
            ? session.PlaylistPath
            : null;

    /// <summary>
    /// Gets whether the FFmpeg process for a camera has exited (crashed or finished).
    /// </summary>
    public bool HasProcessExited(Guid cameraId)
        => sessions.TryGetValue(cameraId, out var session) && session.HasExited;

    /// <summary>
    /// Gets recent FFmpeg stderr output for a camera session.
    /// </summary>
    public string GetProcessError(Guid cameraId)
        => sessions.TryGetValue(cameraId, out var session)
            ? session.GetRecentErrors()
            : string.Empty;

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        foreach (var session in sessions.Values)
        {
            session.Dispose();
        }

        sessions.Clear();
    }

    /// <summary>
    /// Disposes any stream session whose last client activity is older than the
    /// inactivity timeout. Driven periodically by <see cref="HlsStreamReaperService"/>
    /// so an orphaned FFmpeg transcoder (client dropped its socket without
    /// calling StopStream) is torn down instead of running forever.
    /// </summary>
    public void ReapIdleSessions()
    {
        if (disposed)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - InactivityTimeout;
        foreach (var (cameraId, session) in sessions)
        {
            if (session.LastActivityUtc < cutoff)
            {
                ReapSession(cameraId);
            }
        }
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "removed is unconditionally disposed inside the try block.")]
    private void ReapSession(Guid cameraId)
    {
        if (!sessions.TryRemove(cameraId, out var removed))
        {
            return;
        }

        try
        {
            removed.Dispose();
            LogHlsStreamReaped(cameraId);
        }
        catch (Exception ex)
        {
            LogHlsStreamReapFailed(ex, cameraId);
        }
    }

    private StreamSession CreateSession(Guid cameraId)
    {
        var camera = storage.GetCameraById(cameraId)
            ?? throw new InvalidOperationException($"Camera {cameraId} not found.");

        // HLS re-streaming for USB sources requires a different ffmpeg
        // command line (-f dshow -i video=...). Tracked as a Phase 9+
        // enhancement; for now USB cameras can still be recorded and
        // snapshotted via the in-process pipeline, just not HLS-streamed
        // to the Blazor browser.
        if (camera.Connection.Source != CameraSource.Network)
        {
            throw new NotSupportedException(
                $"HLS streaming is not yet implemented for {camera.Connection.Source} cameras (camera {cameraId}).");
        }

        var outputDir = Path.Combine(hlsOutputRoot, cameraId.ToString("N"));

        // Clean stale files from previous sessions (e.g. after Aspire restart)
        if (Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }

        Directory.CreateDirectory(outputDir);

        var playlistPath = Path.Combine(outputDir, "stream.m3u8");
        var streamUri = camera.BuildUri();
        var transport = camera.Stream.RtspTransport ?? "tcp";

        // -c:v copy: pass the camera's native H.264/H.265 video stream
        // straight through to HLS without re-encoding. Modern IP cameras
        // already produce HLS-compatible video, so transcoding is pure CPU
        // waste — at 10+ viewed cameras libx264 saturates the host. HLS
        // still honours -hls_time at GOP boundaries (typically 2-4 s).
        // Audio is always re-encoded because cameras commonly use G.711
        // / PCMU which HLS / browser MSE cannot demux. Cameras with non-
        // H.264/H.265 video will fail under this default — for those, add
        // a future per-camera "force transcoding" setting.
        // hls_flags split_by_time is critical for stream-copied input
        // (-c:v copy): without it, the HLS muxer only cuts segments at
        // keyframe boundaries, so a camera with a long GOP (30s+ between
        // I-frames) won't produce its first .ts file — and therefore won't
        // produce stream.m3u8 — within the StartStream playlist-ready
        // timeout. split_by_time relaxes that to cut at hls_time boundaries
        // regardless of keyframes; the first segment may briefly fail to
        // decode until the browser finds a keyframe, but playback recovers
        // within a fraction of a second and the stream actually starts.
        var args = string.Join(
            ' ',
            "-v info",
            "-fflags nobuffer",
            "-flags low_delay",
            "-probesize 512000",
            "-analyzeduration 500000",
            $"-rtsp_transport {transport}",
            "-timeout 10000000",
            $"-i \"{streamUri}\"",
            "-c:v copy",
            "-c:a aac -b:a 64k",
            "-f hls",
            "-hls_time 2",
            "-hls_list_size 5",
            "-hls_flags delete_segments+split_by_time",
            $"\"{playlistPath}\"");

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
        };

        LogStartingFfmpeg(cameraId, args);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start FFmpeg HLS process.");

        var session = new StreamSession(process, playlistPath, outputDir);

        // Log stderr asynchronously for diagnostics
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                session.AddErrorLine(e.Data);
                LogFfmpegOutput(cameraId, e.Data);
            }
        };
        process.BeginErrorReadLine();

        LogHlsStreamStarted(cameraId, playlistPath);

        return session;
    }

    private sealed class StreamSession : IDisposable
    {
        private readonly Process process;
        private readonly string outputDir;
        private readonly List<string> errorLines = [];
        private int viewerCount;
        private long lastActivityUtcTicks;

        public StreamSession(
            Process process,
            string playlistPath,
            string outputDir)
        {
            this.process = process;
            this.outputDir = outputDir;
            PlaylistPath = playlistPath;
            lastActivityUtcTicks = DateTime.UtcNow.Ticks;
        }

        public string PlaylistPath { get; }

        public int ViewerCount => Volatile.Read(ref viewerCount);

        public bool HasExited => process.HasExited;

        public int ExitCode => process.HasExited ? process.ExitCode : -1;

        public DateTime LastActivityUtc
            => new(Interlocked.Read(ref lastActivityUtcTicks), DateTimeKind.Utc);

        public void TouchActivity()
            => Interlocked.Exchange(ref lastActivityUtcTicks, DateTime.UtcNow.Ticks);

        public void AddErrorLine(string line) => errorLines.Add(line);

        public string GetRecentErrors()
        {
            lock (errorLines)
            {
                return string.Join(Environment.NewLine, errorLines.TakeLast(10));
            }
        }

        public void IncrementViewers()
            => Interlocked.Increment(ref viewerCount);

        public int DecrementViewers()
            => Interlocked.Decrement(ref viewerCount);

        public void Dispose()
        {
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.Write('q');
                    process.StandardInput.Flush();

                    if (!process.WaitForExit(5000))
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited
            }
            finally
            {
                process.Dispose();
            }

            try
            {
                if (Directory.Exists(outputDir))
                {
                    Directory.Delete(outputDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best effort cleanup
            }
        }
    }
}