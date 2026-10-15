namespace AtcSoft.VideoSurveillance.Api.Domain.Security;

/// <summary>
/// A signed-in session: the access token to send, and the refresh token that renews it.
/// </summary>
public sealed record AccountSession(
    string AccessToken,
    TimeSpan AccessTokenLifetime,
    string RefreshToken,
    string UserName,
    AccountRole Role);