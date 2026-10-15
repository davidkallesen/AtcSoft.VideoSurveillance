namespace AtcSoft.VideoSurveillance.Api.Security;

/// <summary>
/// Writes every change a caller made into the <see cref="IAuditLog"/>: a request
/// that is not a read, to one of the API's operations, that succeeded. The
/// sign-in operations write their own entries (with the name that was given),
/// and watching a stream is no change.
/// </summary>
public sealed class AuditMiddleware(
    RequestDelegate next,
    IAuditLog audit)
{
    // Endpoint names, as the generated endpoints name them (the operationId in Pascal case).
    private static readonly HashSet<string> NotRecorded = new(StringComparer.Ordinal)
    {
        "CompleteSetup",
        "CreateSession",
        "RefreshSession",
        "EndSession",
        "RenewStreamLease",
        "DeleteStreamLease",
    };

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await next(context).ConfigureAwait(false);

        if (HttpMethods.IsGet(context.Request.Method) ||
            HttpMethods.IsHead(context.Request.Method) ||
            HttpMethods.IsOptions(context.Request.Method) ||
            context.Response.StatusCode >= StatusCodes.Status400BadRequest ||
            context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName is not { Length: > 0 } name ||
            NotRecorded.Contains(name))
        {
            return;
        }

        var caller = CallerIdentity.From(context.User);
        audit.Record(
            caller?.UserName ?? "anonymous",
            char.ToLowerInvariant(name[0]) + name[1..],
            context.Request.Path + context.Request.QueryString);
    }
}