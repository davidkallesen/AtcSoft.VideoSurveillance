namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// The connection state in a hub event (<c>ConnectionStateChanged</c>).
/// </summary>
public static class CameraStateText
{
    /// <summary>
    /// Gets the camera state from the event's text, or <see langword="null"/>
    /// when the event has none.
    /// </summary>
    public static CameraConnectionState? Parse(string? newState)
        => Enum.TryParse<CameraConnectionState>(newState, ignoreCase: true, out var state)
            ? state
            : null;
}