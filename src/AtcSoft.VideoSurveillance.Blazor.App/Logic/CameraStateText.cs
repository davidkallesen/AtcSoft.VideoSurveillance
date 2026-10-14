namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// The connection state in a hub event (<c>ConnectionStateChanged</c>).
/// </summary>
public static class CameraStateText
{
    /// <summary>
    /// Gets the camera state from the event's text, or <see langword="null"/>
    /// when the event has none. The hub writes the server's C# name
    /// (<c>Connected</c>), a listed value in another casing; a state the
    /// client does not know yet is kept as it is.
    /// </summary>
    public static CameraConnectionState? Parse(string? newState)
        => string.IsNullOrEmpty(newState)
            ? null
            : new CameraConnectionState(newState);
}