namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// Gateway service - sign-in and the signed-in user's own password. A refusal
/// is answered with the server's reason, not an exception: the dialogs show it.
/// </summary>
public sealed partial class GatewayService
{
    /// <summary>
    /// Gets whether the server still needs its first admin; <see langword="null"/>
    /// when it could not be reached.
    /// </summary>
    public async Task<bool?> IsSetupRequiredAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await getSetupStatusEndpoint
                .ExecuteAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return result.IsOk
                ? result.OkContent.SetupRequired
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<(Session? Session, string? Problem)> CompleteSetupAsync(
        string setupCode,
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var parameters = new CompleteSetupParameters(Request: new CompleteSetupRequest(setupCode, userName, password));
        var result = await completeSetupEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? (result.OkContent, null)
            : (null, ProblemText.From(result));
    }

    public async Task<(Session? Session, string? Problem)> SignInAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var parameters = new CreateSessionParameters(Request: new SignInRequest(userName, password));
        var result = await createSessionEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsOk
            ? (result.OkContent, null)
            : (null, ProblemText.From(result));
    }

    /// <summary>
    /// Renews a session; <see langword="null"/> when the server refused (sign in
    /// again) or could not be reached.
    /// </summary>
    public async Task<Session?> RefreshSessionAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parameters = new RefreshSessionParameters(Request: new RefreshSessionRequest(refreshToken));
            var result = await refreshSessionEndpoint
                .ExecuteAsync(parameters, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return result.IsOk
                ? result.OkContent
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task EndSessionAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await endSessionEndpoint
                .ExecuteAsync(new EndSessionParameters(Request: new RefreshSessionRequest(refreshToken)), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // The session runs out on the server by itself.
        }
    }

    /// <summary>
    /// Changes the signed-in user's own password; returns the server's reason when it refused.
    /// </summary>
    public async Task<string?> ChangeOwnPasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var parameters = new UpdateOwnPasswordParameters(Request: new ChangePasswordRequest(currentPassword, newPassword));
        var result = await updateOwnPasswordEndpoint
            .ExecuteAsync(parameters, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result.IsNoContent
            ? null
            : ProblemText.From(result);
    }
}