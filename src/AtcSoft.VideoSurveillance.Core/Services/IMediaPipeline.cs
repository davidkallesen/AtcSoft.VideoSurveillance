namespace Linksoft.VideoSurveillance.Services;

/// <summary>
/// Abstraction over a media playback/capture pipeline.
/// Both WPF and Server implement via Linksoft.VideoEngine (in-process FFmpeg).
/// </summary>
public interface IMediaPipeline : IDisposable
{
    /// <summary>
    /// Opens a stream from the specified URI. Network-camera shortcut
    /// equivalent to
    /// <c>Open(new SourceLocator(streamUri), settings)</c>; default
    /// implementation forwards to the source-locator overload so
    /// implementations only need to override one method.
    /// </summary>
    void Open(
        Uri streamUri,
        StreamSettings settings)
    {
        ArgumentNullException.ThrowIfNull(streamUri);
        Open(new SourceLocator(streamUri), settings);
    }

    /// <summary>
    /// Opens a source described by a <see cref="SourceLocator"/>.
    /// Implementations that support local-device sources (USB,
    /// DirectShow, V4L2) read
    /// <see cref="SourceLocator.InputFormat"/> and
    /// <see cref="SourceLocator.RawDeviceSpec"/> from the locator;
    /// implementations that only support network sources can fall back
    /// to <see cref="SourceLocator.Uri"/>.
    /// </summary>
    void Open(
        SourceLocator locator,
        StreamSettings settings);

    /// <summary>
    /// Closes the current stream.
    /// </summary>
    void Close();

    /// <summary>
    /// Starts recording the stream to a file.
    /// </summary>
    void StartRecording(string outputFilePath);

    /// <summary>
    /// Stops the current recording.
    /// </summary>
    void StopRecording();

    /// <summary>
    /// Atomically transitions the active recording to a new output file.
    /// Implementations should perform the close+open under a single lock so
    /// that packets arriving mid-switch land in either the previous or the
    /// new segment — never the close/open gap.
    /// Default implementation falls back to <see cref="StopRecording"/>
    /// followed by <see cref="StartRecording"/>; override for atomicity.
    /// </summary>
    void SwitchRecording(string newOutputFilePath)
    {
        StopRecording();
        StartRecording(newOutputFilePath);
    }

    /// <summary>
    /// Gets a value indicating whether recording is currently active.
    /// </summary>
    bool IsRecordingActive { get; }

    /// <summary>
    /// Captures a single frame from the stream as PNG image bytes.
    /// </summary>
    Task<byte[]?> CaptureFrameAsync(CancellationToken ct = default);

    /// <summary>
    /// Sets the clockwise rotation applied to live display, snapshots, and
    /// (via container metadata) any active or future recording.
    /// </summary>
    void SetRotation(CameraRotation rotation);

    /// <summary>
    /// Gets the current frames per second.
    /// </summary>
    double CurrentFps { get; }

    /// <summary>
    /// Gets the total number of frames decoded.
    /// </summary>
    long FramesDecoded { get; }

    /// <summary>
    /// Gets the UTC timestamp of the last packet successfully read from the
    /// underlying demuxer, or <see cref="DateTime.MinValue"/> if no packet has
    /// arrived yet. Used by the server-side stream-stale watchdog to detect
    /// wedged pipelines (socket open but no packets) without waiting for the
    /// VideoEngine's consecutive-read-errors threshold.
    /// </summary>
    DateTime LastPacketUtc { get; }

    /// <summary>
    /// Occurs when the connection state changes.
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
}