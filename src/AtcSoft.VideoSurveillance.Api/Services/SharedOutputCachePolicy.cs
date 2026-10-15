namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// An output cache policy for an answer that is the same for every caller,
/// signed in or not: ASP.NET Core's default policy never caches a signed-in
/// request, and every request but the sign-in is signed in. Only a 200 is kept.
/// </summary>
public sealed class SharedOutputCachePolicy(
    TimeSpan expiration,
    string tag) : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(
        OutputCacheContext context,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);

        var isRead = HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method);
        context.EnableOutputCaching = true;
        context.AllowCacheLookup = isRead;
        context.AllowCacheStorage = isRead;
        context.AllowLocking = true;
        context.ResponseExpirationTimeSpan = expiration;
        context.Tags.Add(tag);
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(
        OutputCacheContext context,
        CancellationToken cancellation)
        => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(
        OutputCacheContext context,
        CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK)
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}