namespace AtcSoft.VideoSurveillance.Api.Domain.Security;

/// <summary>
/// The server's users and their sessions.
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Gets a value indicating whether the server has no user yet, so the first
    /// admin is created with the setup code from the data folder.
    /// </summary>
    bool IsSetupRequired { get; }

    /// <summary>
    /// Creates the first admin, when the code is the one in the data folder and
    /// the server has no user yet, and signs them in.
    /// </summary>
    (SetupOutcome Outcome, AccountSession? Session) CompleteSetup(
        string setupCode,
        string userName,
        string password);

    /// <summary>
    /// Signs a user in; <see langword="null"/> when the name or the password is
    /// not right (which, is not said).
    /// </summary>
    AccountSession? SignIn(
        string userName,
        string password);

    /// <summary>
    /// Renews a session for its refresh token, which cannot be used again;
    /// <see langword="null"/> when the token is not known or has run out.
    /// </summary>
    AccountSession? Refresh(string refreshToken);

    /// <summary>
    /// Ends the session a refresh token belongs to; one that is gone is no error.
    /// </summary>
    void SignOut(string refreshToken);
}