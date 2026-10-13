namespace AtcSoft.VideoSurveillance.Api.Webhooks;

public sealed partial class WebhookRegistry
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The configured webhook receiver {Receiver} has no usable address and gets no events")]
    private partial void LogInvalidEndpoint(string receiver);

    [LoggerMessage(Level = LogLevel.Error, Message = "The webhook receivers could not be saved to {Path}")]
    private partial void LogSaveFailed(string path);
}