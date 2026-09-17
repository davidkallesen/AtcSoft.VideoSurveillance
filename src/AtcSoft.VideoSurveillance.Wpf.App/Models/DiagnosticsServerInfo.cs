namespace Linksoft.VideoSurveillance.Wpf.App.Models;

public sealed class DiagnosticsServerInfo
{
    public string ApiBaseAddress { get; set; } = string.Empty;

    public string HubConnectionState { get; set; } = string.Empty;

    public int ConnectedCameras { get; set; }

    public int ActiveRecordings { get; set; }

    /// <summary>
    /// <see langword="null"/> when the gateway query failed; the JSON
    /// reader can then distinguish "no cameras" (empty array) from
    /// "couldn't reach the server" (null).
    /// </summary>
    public string? GatewayErrorMessage { get; set; }
}