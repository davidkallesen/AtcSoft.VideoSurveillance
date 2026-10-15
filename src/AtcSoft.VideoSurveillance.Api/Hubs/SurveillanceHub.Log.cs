namespace AtcSoft.VideoSurveillance.Api.Hubs;

public sealed partial class SurveillanceHub
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Client connected: {ConnectionId}")]
    private partial void LogClientConnected(string connectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client disconnected: {ConnectionId}")]
    private partial void LogClientDisconnected(string connectionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to start stream for camera {CameraId}")]
    private partial void LogStartStreamFailed(Exception ex, Guid cameraId);
}