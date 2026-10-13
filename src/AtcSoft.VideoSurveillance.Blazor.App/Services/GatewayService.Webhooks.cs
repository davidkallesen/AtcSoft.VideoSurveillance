namespace AtcSoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// Gateway service - Webhooks operations using generated endpoints.
/// </summary>
public sealed partial class GatewayService
{
    public async Task<Webhook[]?> GetWebhooksAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await listWebhooksEndpoint
            .ExecuteAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? result.OkContent.ToArray()
            : null;
    }

    public async Task<Webhook?> CreateWebhookAsync(
        CreateWebhookRequest request,
        CancellationToken cancellationToken = default)
    {
        var parameters = new CreateWebhookParameters(Request: request);
        var result = await createWebhookEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsCreated
            ? result.CreatedContent
            : null;
    }

    public async Task<Webhook?> UpdateWebhookAsync(
        Guid webhookId,
        UpdateWebhookRequest request,
        CancellationToken cancellationToken = default)
    {
        var parameters = new UpdateWebhookParameters(WebhookId: webhookId, Request: request);
        var result = await updateWebhookEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? result.OkContent
            : null;
    }

    public async Task<bool> DeleteWebhookAsync(
        Guid webhookId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DeleteWebhookParameters(WebhookId: webhookId);
        var result = await deleteWebhookEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsNoContent;
    }

    /// <summary>
    /// Sends a test event to a receiver.
    /// </summary>
    /// <returns>Whether the receiver took it; null when the server could not be asked.</returns>
    public async Task<bool?> TestWebhookAsync(
        Guid webhookId,
        CancellationToken cancellationToken = default)
    {
        var parameters = new TestWebhookParameters(WebhookId: webhookId);
        var result = await testWebhookEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? result.OkContent.Delivered
            : null;
    }
}