namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// Keeps who is connected and who is recording, per camera, so the counts
/// are derived from the cameras' states instead of being counted up and
/// down as events arrive. Repeated or out-of-order events cannot make the
/// counts drift.
/// </summary>
public sealed class CameraStatusBoard
{
    private const string ConnectedState = "Connected";
    private const string RecordingStatePrefix = "Recording";

    private readonly Dictionary<Guid, Status> cameras = [];

    /// <summary>
    /// Gets the number of cameras.
    /// </summary>
    public int CameraCount => cameras.Count;

    /// <summary>
    /// Gets the number of cameras that are connected.
    /// </summary>
    public int ConnectedCount => cameras.Values.Count(s => s.IsConnected);

    /// <summary>
    /// Gets the number of cameras that are recording.
    /// </summary>
    public int RecordingCount => cameras.Values.Count(s => s.IsRecording);

    /// <summary>
    /// Replaces everything with the cameras as the server lists them now.
    /// </summary>
    public void Load(
        IEnumerable<(Guid Id, bool IsConnected, bool IsRecording)> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        cameras.Clear();
        foreach (var (id, isConnected, isRecording) in current)
        {
            cameras[id] = new Status(isConnected, isRecording);
        }
    }

    /// <summary>
    /// Applies a connection state reported by the server.
    /// </summary>
    public void ApplyConnectionState(
        Guid cameraId,
        string? newState)
    {
        var status = cameras.GetValueOrDefault(cameraId);
        cameras[cameraId] = status with
        {
            IsConnected = string.Equals(newState, ConnectedState, StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Applies a recording state reported by the server. Every state that
    /// starts with "Recording" (plain, on motion, after motion) is recording.
    /// </summary>
    public void ApplyRecordingState(
        Guid cameraId,
        string? newState)
    {
        var status = cameras.GetValueOrDefault(cameraId);
        cameras[cameraId] = status with
        {
            IsRecording = IsRecordingState(newState),
        };
    }

    /// <summary>
    /// Gets a value indicating whether a camera is connected.
    /// </summary>
    public bool IsConnected(Guid cameraId)
        => cameras.GetValueOrDefault(cameraId).IsConnected;

    /// <summary>
    /// Gets a value indicating whether a camera is recording.
    /// </summary>
    public bool IsRecording(Guid cameraId)
        => cameras.GetValueOrDefault(cameraId).IsRecording;

    /// <summary>
    /// Gets a value indicating whether a recording state name means the camera is recording.
    /// </summary>
    public static bool IsRecordingState(string? state)
        => state is not null &&
           state.StartsWith(RecordingStatePrefix, StringComparison.OrdinalIgnoreCase);

    private readonly record struct Status(bool IsConnected, bool IsRecording);
}