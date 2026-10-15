namespace AtcSoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// Sends the session's access token with every call to the server. A token
/// about to run out is renewed first; a read the server refused for its token
/// (401) is sent once more after a renewal. The sign-in calls go without one.
/// </summary>
public sealed class BearerTokenHandler(BrowserSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IsSignInCall(request))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (session.NeedsRenewal)
        {
            await session.RenewAsync(cancellationToken).ConfigureAwait(false);
        }

        var sentWith = Attach(request);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || sentWith is null || request.Content is not null)
        {
            return response;
        }

        if (!await session.RenewAsync(cancellationToken, staleToken: sentWith).ConfigureAwait(false))
        {
            return response;
        }

        response.Dispose();
        using var again = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers.Where(h => !string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)))
        {
            again.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        Attach(again);
        return await base.SendAsync(again, cancellationToken).ConfigureAwait(false);
    }

    // The calls that sign in, renew or sign out carry no access token.
    private static bool IsSignInCall(HttpRequestMessage request)
        => request.RequestUri?.AbsolutePath is { } path &&
           path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase) &&
           !path.StartsWith("/auth/me", StringComparison.OrdinalIgnoreCase);

    private string? Attach(HttpRequestMessage request)
    {
        var token = session.AccessToken;
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return token;
    }
}