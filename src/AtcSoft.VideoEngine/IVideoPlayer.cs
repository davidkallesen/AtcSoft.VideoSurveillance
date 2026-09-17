namespace Linksoft.VideoEngine;

/// <summary>
/// High-level interface for a video player capable of opening streams,
/// recording, and capturing frames. Implementations may use GPU-accelerated
/// decoding/rendering (WPF) or CPU-only decoding (headless server).
/// </summary>
public interface IVideoPlayer : IDisposable
{
    /// <summary>
    /// Gets the current state of the player.
    /// </summary>
    PlayerState State { get; }

    /// <summary>
    /// Gets information about the current video stream, or <c>null</c> if no stream is open.
    /// </summary>
    VideoStreamInfo? StreamInfo { get; }

    /// <summary>
    /// Gets the current frames per second being decoded/displayed.
    /// </summary>
    double CurrentFps { get; }

    /// <summary>
    /// Gets the total number of frames decoded since the stream was opened.
    /// </summary>
    long FramesDecoded { get; }

    /// <summary>
    /// Gets the UTC timestamp of the last packet successfully read from the
    /// demuxer, or <see cref="DateTime.MinValue"/> if no packet has arrived
    /// since the most recent <see cref="Open"/>. Ticks at packet rate (typically
    /// 25–60 Hz on RTSP), making it suitable for stream-stale detection by the
    /// server reaper without waiting for the slower keyframe cadence implied by
    /// <see cref="FramesDecoded"/>.
    /// </summary>
    DateTime LastPacketUtc { get; }

    /// <summary>
    /// Gets a value indicating whether recording is currently active.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Gets the GPU accelerator used by this player, or <c>null</c> if CPU-only.
    /// </summary>
    IGpuAccelerator? GpuAccelerator { get; }

    /// <summary>
    /// Opens a stream from the specified URI.
    /// </summary>
    /// <param name="streamUri">The URI of the stream to open (rtsp://, http://, etc.).</param>
    /// <param name="options">Stream options controlling latency, transport, and decoding.</param>
    void Open(
        Uri streamUri,
        StreamOptions? options = null);

    /// <summary>
    /// Closes the current stream and releases resources.
    /// </summary>
    void Close();

    /// <summary>
    /// Starts recording the current stream to a file.
    /// </summary>
    /// <param name="outputFilePath">The output file path (extension determines container format).</param>
    void StartRecording(string outputFilePath);

    /// <summary>
    /// Stops the current recording.
    /// </summary>
    void StopRecording();

    /// <summary>
    /// Atomically transitions the active recording to a new file.
    /// Packets arriving from the demux thread between Close and Open are
    /// not silently dropped — they land in either the previous or the new
    /// segment. If no recording is currently active, behaves like
    /// <see cref="StartRecording"/>.
    /// </summary>
    void SwitchRecording(string newOutputFilePath);

    /// <summary>
    /// Captures a single frame from the stream as PNG image bytes.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>PNG-encoded bytes, or <c>null</c> if capture failed.</returns>
    Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default);

    /// <summary>
    /// Sets the clockwise rotation applied to the live video, snapshots, and
    /// (via container metadata) any active or future recording.
    /// Safe to call before <see cref="Open"/> or while playing; the next
    /// processed frame and any subsequent <see cref="StartRecording"/> reflect
    /// the new value.
    /// </summary>
    void SetRotation(VideoRotation rotation);

    /// <summary>
    /// Occurs when the player state changes.
    /// </summary>
    event EventHandler<PlayerStateChangedEventArgs>? StateChanged;
}