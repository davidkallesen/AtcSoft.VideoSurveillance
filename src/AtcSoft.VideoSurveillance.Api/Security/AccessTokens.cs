namespace AtcSoft.VideoSurveillance.Api.Security;

/// <summary>
/// Makes and checks the server's access tokens: JSON web tokens signed with a
/// key of this server's own, kept in its data folder, made on its first start.
/// </summary>
public sealed class AccessTokens
{
    /// <summary>
    /// The issuer and audience of the tokens.
    /// </summary>
    public const string Issuer = "AtcSoft.VideoSurveillance";

    private const int KeyLength = 64;

    private readonly TimeProvider time;
    private readonly SymmetricSecurityKey key;
    private readonly JsonWebTokenHandler handler = new();

    public AccessTokens(
        string keyPath,
        TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyPath);

        this.time = time;
        key = new SymmetricSecurityKey(LoadOrCreateKey(keyPath));
        ValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Issuer,
            IssuerSigningKey = key,
            NameClaimType = CallerIdentity.NameClaim,
            RoleClaimType = CallerIdentity.RoleClaim,
            ClockSkew = TimeSpan.FromSeconds(30),
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = time.GetUtcNow().UtcDateTime;
                return (notBefore is null || notBefore <= now.AddSeconds(30)) &&
                       expires is not null && expires > now.AddSeconds(-30);
            },
        };
    }

    /// <summary>
    /// Gets how long an access token is valid.
    /// </summary>
    public TimeSpan Lifetime { get; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets what a token must be to be valid here (for the bearer handler).
    /// </summary>
    public TokenValidationParameters ValidationParameters { get; }

    /// <summary>
    /// Makes an access token for a user.
    /// </summary>
    public string Create(
        Guid userId,
        string userName,
        AccountRole role)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Issuer,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + Lifetime,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["sub"] = userId.ToString(),
                [CallerIdentity.NameClaim] = userName,
                [CallerIdentity.RoleClaim] = role.ToApiRoleName(),
            },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
    }

    private static byte[] LoadOrCreateKey(string keyPath)
    {
        if (File.Exists(keyPath) &&
            Convert.TryFromBase64String(File.ReadAllText(keyPath).Trim(), new byte[KeyLength], out var length) &&
            length == KeyLength)
        {
            return Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
        }

        var created = RandomNumberGenerator.GetBytes(KeyLength);
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
        File.WriteAllText(keyPath, Convert.ToBase64String(created));
        return created;
    }
}