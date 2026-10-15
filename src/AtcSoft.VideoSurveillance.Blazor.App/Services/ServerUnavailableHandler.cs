namespace AtcSoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// Turns "the server could not be reached" into an answer. A failed
/// connection, a timeout and a call the resilience handler gave up on all
/// arrive as exceptions; the pages are written for calls that come back
/// without a result, and an exception that escapes one takes the page down
/// with the browser's "unhandled error" bar.
/// </summary>
public sealed class ServerUnavailableHandler : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsServerUnavailable(ex, cancellationToken))
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
                ReasonPhrase = Translations.ServerCouldNotBeReached,
            };
        }
    }

    // A call the page itself cancelled (it was left, or disposed) stays cancelled.
    private static bool IsServerUnavailable(
        Exception exception,
        CancellationToken cancellationToken)
        => exception switch
        {
            HttpRequestException => true,
            Polly.ExecutionRejectedException => true,
            TimeoutException => true,
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            _ => false,
        };
}