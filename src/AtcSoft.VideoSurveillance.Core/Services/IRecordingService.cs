namespace Linksoft.VideoSurveillance.Services;

/// <summary>
/// Service for managing camera recording sessions.
/// </summary>
public interface IRecordingService
{
    event EventHandler<RecordingStateChangedEventArgs>? RecordingStateChanged;

    RecordingState GetRecordingState(Guid cameraId);

    RecordingSession? GetSession(Guid cameraId);

    bool StartRecording(
        CameraConfiguration camera,
        IMediaPipeline pipeline);

    void StopRecording(Guid cameraId);

    bool IsRecording(Guid cameraId);

    bool TriggerMotionRecording(
        CameraConfiguration camera,
        IMediaPipeline pipeline);

    void UpdateMotionTimestamp(Guid cameraId);

    string GenerateRecordingFilename(
        CameraConfiguration camera,
        string format);

    void StopAllRecordings();

    bool SegmentRecording(Guid cameraId);

    IReadOnlyList<RecordingSession> GetActiveSessions();

    /// <summary>
    /// Stops any active recording session whose underlying pipeline is no
    /// longer producing output (e.g. RTSP connection died, decoder exited).
    /// Implementations release the dead pipeline and mark the session
    /// stopped so a re-attempt can start a fresh recording.
    /// Returns the number of sessions reaped.
    /// </summary>
    int ReapInactiveSessions();

    /// <summary>
    /// Checks disk space mid-recording. Attempts to reclaim space; if the
    /// drive is still below the guard threshold after reclaim, stops all
    /// active recordings to prevent disk exhaustion.
    /// Returns true if recordings were stopped; false otherwise.
    /// No-op (returns false) when the disk-space guard is disabled or no
    /// recordings are active.
    /// </summary>
    bool EnforceDiskSpaceGuard();
}