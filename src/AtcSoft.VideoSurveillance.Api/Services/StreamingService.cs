namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// Manages per-camera FFmpeg processes that turn a camera's stream, or a USB
/// camera's pictures, into HLS segments for browser consumption.
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

        var outputDir = Path.Combine(hlsOutputRoot, cameraId.ToString("N"));

        // Clean stale files from previous sessions (e.g. after Aspire restart)
        if (Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }

        Directory.CreateDirectory(outputDir);

        var playlistPath = Path.Combine(outputDir, "stream.m3u8");
        var args = HlsStreamCommand.BuildArguments(camera, playlistPath);

        var psi = new ProcessStartInfo
        {
            FileName = HlsStreamCommand.FindTool(VideoEngineBootstrap.FindFFmpegPath()),
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true,
        };

        // The command line and FFmpeg's own output both hold the camera's
        // password; neither goes into the log, or to a client, as it is.
        LogStartingFfmpeg(cameraId, HlsStreamCommand.Redact(args, camera));

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start FFmpeg HLS process.");

        var session = new StreamSession(process, playlistPath, outputDir);

        // Log stderr asynchronously for diagnostics
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                var line = HlsStreamCommand.Redact(e.Data, camera);
                session.AddErrorLine(line);
                LogFfmpegOutput(cameraId, line);
            }
        };
        process.BeginErrorReadLine();

        LogHlsStreamStarted(cameraId, playlistPath);

        return session;
    }

    private sealed class StreamSession : IDisposable
    {
        // Only the last lines are ever asked for; FFmpeg writes several a
        // second for as long as the stream runs.
        private const int MaxErrorLines = 50;

        private readonly Process process;
        private readonly string outputDir;
        private readonly Queue<string> errorLines = new();
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

        public void AddErrorLine(string line)
        {
            lock (errorLines)
            {
                errorLines.Enqueue(line);
                while (errorLines.Count > MaxErrorLines)
                {
                    errorLines.Dequeue();
                }
            }
        }

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