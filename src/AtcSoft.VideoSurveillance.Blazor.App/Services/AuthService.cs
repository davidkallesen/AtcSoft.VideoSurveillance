namespace AtcSoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// Signs this browser in and out, and keeps the session across a page reload:
/// the refresh token is in the browser's storage, the access token only in memory.
/// </summary>
public sealed class AuthService
{
    private readonly GatewayService gateway;
    private readonly BrowserSession session;
    private readonly IJSRuntime js;

    public AuthService(
        GatewayService gateway,
        BrowserSession session,
        IJSRuntime js)
    {
        ArgumentNullException.ThrowIfNull(session);

        this.gateway = gateway;
        this.session = session;
        this.js = js;
        session.Renew = RenewAsync;
    }

    /// <summary>
    /// Takes up the session of an earlier visit, when there is one the server still has.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (session.IsSignedIn)
        {
            return;
        }

        var stored = await js.InvokeAsync<string?>("atcSession.get").ConfigureAwait(true);
        if (StoredSession.Read(stored) is { } earlier)
        {
            session.Restore(earlier.RefreshToken, earlier.UserName);
            await session.RenewAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Signs in; returns the server's reason when it refused.
    /// </summary>
    public async Task<string?> SignInAsync(
        string userName,
        string password)
    {
        var result = await gateway.SignInAsync(userName, password).ConfigureAwait(true);
        return await TakeAsync(result).ConfigureAwait(true);
    }

    /// <summary>
    /// Creates the first admin and signs them in; returns the server's reason when it refused.
    /// </summary>
    public async Task<string?> CompleteSetupAsync(
        string setupCode,
        string userName,
        string password)
    {
        var result = await gateway.CompleteSetupAsync(setupCode, userName, password).ConfigureAwait(true);
        return await TakeAsync(result).ConfigureAwait(true);
    }

    public async Task SignOutAsync()
    {
        if (session.RefreshToken is { } refreshToken)
        {
            await gateway.EndSessionAsync(refreshToken).ConfigureAwait(true);
        }

        session.Clear();
        await js.InvokeVoidAsync("atcSession.clear").ConfigureAwait(true);
    }

    private async Task<string?> TakeAsync(GatewayResult<Session> result)
    {
        if (result.Value is not { } signedIn)
        {
            return result.Problem;
        }

        session.Apply(signedIn);
        await SaveAsync(signedIn).ConfigureAwait(true);
        return null;
    }

    private async Task<bool> RenewAsync(CancellationToken cancellationToken)
    {
        if (session.RefreshToken is not { } refreshToken)
        {
            return false;
        }

        var (renewed, outcome) = await gateway.RefreshSessionAsync(refreshToken, cancellationToken).ConfigureAwait(true);
        if (renewed is null)
        {
            // Only a refused refresh token ends the stored session; a busy or absent server keeps it.
            if (SessionRenewal.ForgetsTheStoredSession(outcome))
            {
                await js.InvokeVoidAsync("atcSession.clear", cancellationToken).ConfigureAwait(true);
            }

            return false;
        }

        session.Apply(renewed);
        await SaveAsync(renewed).ConfigureAwait(true);
        return true;
    }

    private ValueTask SaveAsync(Session saved)
        => js.InvokeVoidAsync("atcSession.set", StoredSession.Write(saved.RefreshToken, saved.UserName));
}