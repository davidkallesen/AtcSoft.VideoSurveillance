namespace AtcSoft.VideoSurveillance.Api.Domain.ApiHandlers.Webhooks;

/// <summary>
/// Handler business logic for the CreateWebhook operation.
/// </summary>
public sealed class CreateWebhookHandler(
    IWebhookRegistry registry) : ICreateWebhookHandler
{
    public Task<CreateWebhookResult> ExecuteAsync(
        CreateWebhookParameters parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var request = parameters.Request;
        var (outcome, webhook) = registry.Add(request.Url, request.Secret, request.Events);

        return Task.FromResult(outcome == WebhookChangeOutcome.Done && webhook is not null
            ? CreateWebhookResult.Created(webhook.ToApiModel())
            : CreateWebhookResult.BadRequest(new global::VideoSurveillance.Generated.Models.Error(Code: 400, Message: "A receiver needs an http or https address.")));
    }
}