namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// A camera's and the hub's connection state in words, in the chosen
/// language. A state this client does not know yet is shown as the server
/// writes it, with a capital.
/// </summary>
public static class ConnectionStateText
{
    public static string ForCamera(CameraConnectionState? state)
        => state?.Value switch
        {
            null or "" => Translations.Unknown,
            CameraConnectionState.Names.Connected => Translations.Connected,
            CameraConnectionState.Names.Connecting => Translations.Connecting,
            CameraConnectionState.Names.Disconnected => Translations.Disconnected,
            CameraConnectionState.Names.Reconnecting => Translations.Reconnecting,
            CameraConnectionState.Names.Error => Translations.Error,
            CameraConnectionState.Names.DeviceUnplugged => Translations.UsbDeviceUnplugged,
            var value => string.Concat(value[..1].ToUpperInvariant(), value[1..]),
        };

    /// <summary>
    /// Gets the hub connection's own state (SignalR's <c>HubConnectionState</c> name) in words.
    /// </summary>
    public static string ForHub(string state)
        => state switch
        {
            "Connected" => Translations.Connected,
            "Connecting" => Translations.Connecting,
            "Disconnected" => Translations.Disconnected,
            "Reconnecting" => Translations.Reconnecting,
            _ => state,
        };
}