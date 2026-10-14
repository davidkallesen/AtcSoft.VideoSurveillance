using CoreConnectionState = AtcSoft.VideoSurveillance.Enums.ConnectionState;
using CoreSwapDirection = AtcSoft.VideoSurveillance.Enums.SwapDirection;
using CoreUsbCameraLifecyclePhase = AtcSoft.VideoSurveillance.Enums.UsbCameraLifecyclePhase;

namespace AtcSoft.VideoSurveillance.Api.Domain.Mapping;

/// <summary>
/// Turns the server's events into the hub's events, as the specification
/// declares them: the same models and values as the REST API.
/// </summary>
public static class HubEventMapping
{
    public static ConnectionStateChangedEvent ToHubEvent(
        Guid cameraId,
        CoreConnectionState state,
        DateTimeOffset timestamp)
        => new(
            CameraId: cameraId,
            NewState: state.ToApiConnectionState(),
            Timestamp: timestamp);

    public static RecordingStateChangedEvent ToHubEvent(
        this RecordingStateChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        return new RecordingStateChangedEvent(
            CameraId: e.CameraId,
            NewState: e.NewState.ToApiRecordingState(),
            OldState: e.OldState.ToApiRecordingState(),
            FilePath: e.FilePath,
            Timestamp: new DateTimeOffset(e.Timestamp, TimeSpan.Zero));
    }

    public static MotionDetectedEvent ToHubEvent(this MotionDetectedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        return new MotionDetectedEvent(
            CameraId: e.CameraId,
            IsMotionActive: e.IsMotionActive,
            ChangePercentage: e.ChangePercentage,
            BoundingBoxes: [.. e.BoundingBoxes.Select(b => new MotionBox(X: b.X, Y: b.Y, Width: b.Width, Height: b.Height))],
            AnalysisWidth: e.AnalysisWidth,
            AnalysisHeight: e.AnalysisHeight,
            Timestamp: new DateTimeOffset(e.Timestamp, TimeSpan.Zero));
    }

    public static UsbCameraLifecycleEvent ToHubEvent(
        this UsbCameraLifecycleChangedEventArgs e,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(e);

        var phase = e.Phase == CoreUsbCameraLifecyclePhase.Replugged
            ? UsbCameraLifecyclePhase.Replugged
            : UsbCameraLifecyclePhase.Unplugged;

        return new UsbCameraLifecycleEvent(
            CameraId: e.CameraId,
            Phase: phase,
            DeviceId: e.Device.DeviceId,
            FriendlyName: e.Device.FriendlyName,
            Timestamp: timestamp);
    }

    public static CoreSwapDirection ToCoreSwapDirection(
        this SwapDirection direction)
        => direction == SwapDirection.Right ? CoreSwapDirection.Right : CoreSwapDirection.Left;

    public static SwapDirection ToApiSwapDirection(
        this CoreSwapDirection direction)
        => direction == CoreSwapDirection.Right ? SwapDirection.Right : SwapDirection.Left;
}