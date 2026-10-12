namespace AtcSoft.VideoSurveillance.Services;

public sealed partial class OnvifConnectionTypeDetector
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "ONVIF connection-type detection for {IpAddress}:{Port} returned {ConnectionType}")]
    private partial void LogDetected(string ipAddress, int port, CameraConnectionType connectionType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ONVIF {Operation} on {Uri} returned HTTP {StatusCode}")]
    private partial void LogRequestRejected(string operation, Uri uri, int statusCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "ONVIF {Operation} on {Uri} failed")]
    private partial void LogRequestFailed(Exception ex, string operation, Uri uri);
}