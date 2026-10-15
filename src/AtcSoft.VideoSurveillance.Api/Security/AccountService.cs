namespace AtcSoft.VideoSurveillance.Api.Security;

/// <summary>
/// <see cref="IAccountService"/> over a file in the server's data folder. The
/// passwords are kept as hashes (<see cref="PasswordHasher{TUser}"/>) and the
/// refresh tokens as SHA-256 hashes, so the file gives neither away. While the
/// server has no user, a one-time setup code lies next to it.
/// </summary>
public sealed partial class AccountService : IAccountService
{
    /// <summary>
    /// How many characters the setup code has.
    /// </summary>
    public const int SetupCodeLength = 24;

    // Letters and digits that cannot be taken for one another.
    private const string SetupCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly Lock sync = new();
    private readonly ServerDataPaths paths;
    private readonly TimeProvider time;
    private readonly ILogger<AccountService> logger;
    private readonly PasswordHasher<StoredUser> hasher = new();
    private readonly StoredAccounts file;

    public AccountService(
        ServerDataPaths paths,
        TimeProvider time,
        ILogger<AccountService> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);

        this.paths = paths;
        this.time = time;
        this.logger = logger;
        Tokens = new AccessTokens(paths.SigningKeyPath, time);
        file = SafeJsonFile.TryRead<StoredAccounts>(paths.AccountsPath, FileOptions) ?? new StoredAccounts();

        if (file.Users.Count == 0)
        {
            EnsureSetupCode();
        }
    }

    /// <summary>
    /// Gets the server's access tokens.
    /// </summary>
    public AccessTokens Tokens { get; }

    /// <inheritdoc />
    public bool IsSetupRequired
    {
        get
        {
            lock (sync)
            {
                return file.Users.Count == 0;
            }
        }
    }

    /// <inheritdoc />
    public (SetupOutcome Outcome, AccountSession? Session) CompleteSetup(
        string setupCode,
        string userName,
        string password)
    {
        lock (sync)
        {
            if (file.Users.Count > 0)
            {
                return (SetupOutcome.AlreadySetUp, null);
            }

            var expected = ReadSetupCode();
            if (expected is null || !FixedTimeEquals(expected, setupCode.Trim().ToUpperInvariant()))
            {
                LogWrongSetupCode();
                return (SetupOutcome.WrongCode, null);
            }

            var user = new StoredUser
            {
                Id = Guid.NewGuid(),
                UserName = userName.Trim(),
                Role = AccountRole.Admin,
                CreatedAt = time.GetUtcNow(),
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            file.Users.Add(user);
            var session = StartSession(user);
            Save();

            TryDelete(paths.SetupCodePath);
            LogSetupCompleted(user.UserName);
            return (SetupOutcome.Done, session);
        }
    }

    /// <inheritdoc />
    public AccountSession? SignIn(
        string userName,
        string password)
    {
        lock (sync)
        {
            var user = file.Users.Find(u => string.Equals(u.UserName, userName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (user is null)
            {
                // As long as for a known user, so the time does not tell which.
                hasher.VerifyHashedPassword(new StoredUser(), DummyHash, password);
                LogSignInRefused();
                return null;
            }

            var verified = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            if (verified == PasswordVerificationResult.Failed)
            {
                LogSignInRefused();
                return null;
            }

            if (verified == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = hasher.HashPassword(user, password);
            }

            var session = StartSession(user);
            Save();
            return session;
        }
    }

    /// <inheritdoc />
    public AccountSession? Refresh(string refreshToken)
    {
        lock (sync)
        {
            var now = time.GetUtcNow();
            var hash = HashToken(refreshToken);
            var stored = file.Sessions.Find(s => string.Equals(s.TokenHash, hash, StringComparison.Ordinal));
            if (stored is null)
            {
                return null;
            }

            // Used once: the old token goes, whether it is still valid or not.
            file.Sessions.Remove(stored);
            var user = file.Users.Find(u => u.Id == stored.UserId);
            if (user is null || stored.ExpiresAt <= now)
            {
                Save();
                return null;
            }

            var session = StartSession(user);
            Save();
            return session;
        }
    }

    /// <inheritdoc />
    public void SignOut(string refreshToken)
    {
        lock (sync)
        {
            var hash = HashToken(refreshToken);
            if (file.Sessions.RemoveAll(s => string.Equals(s.TokenHash, hash, StringComparison.Ordinal)) > 0)
            {
                Save();
            }
        }
    }

    private static string DummyHash { get; } = new PasswordHasher<StoredUser>().HashPassword(new StoredUser(), "no such user");

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedTimeEquals(
        string expected,
        string given)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The server has a user now; a code left behind opens nothing.
        }
    }

    // Called under the lock.
    private AccountSession StartSession(StoredUser user)
    {
        var now = time.GetUtcNow();
        file.Sessions.RemoveAll(s => s.ExpiresAt <= now);

        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        file.Sessions.Add(new StoredSession
        {
            UserId = user.Id,
            TokenHash = HashToken(refreshToken),
            ExpiresAt = now + SessionLifetime,
        });

        return new AccountSession(
            Tokens.Create(user.Id, user.UserName, user.Role),
            Tokens.Lifetime,
            refreshToken,
            user.UserName,
            user.Role);
    }

    private void EnsureSetupCode()
    {
        if (ReadSetupCode() is null)
        {
            var code = new string(RandomNumberGenerator.GetItems(SetupCodeAlphabet.AsSpan(), SetupCodeLength));
            Directory.CreateDirectory(Path.GetDirectoryName(paths.SetupCodePath)!);
            File.WriteAllText(paths.SetupCodePath, code);
        }

        // Where the code is, never the code itself.
        LogSetupRequired(paths.SetupCodePath);
    }

    private string? ReadSetupCode()
    {
        try
        {
            var code = File.ReadAllText(paths.SetupCodePath).Trim().ToUpperInvariant();
            return code.Length == SetupCodeLength ? code : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Save()
    {
        if (!SafeJsonFile.TryWrite(paths.AccountsPath, file, FileOptions))
        {
            LogSaveFailed(paths.AccountsPath);
        }
    }

    // What the file holds.
    private sealed class StoredAccounts
    {
        public List<StoredUser> Users { get; set; } = [];

        public List<StoredSession> Sessions { get; set; } = [];
    }

    private sealed class StoredUser
    {
        public Guid Id { get; set; }

        public string UserName { get; set; } = string.Empty;

        public AccountRole Role { get; set; }

        public string PasswordHash { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class StoredSession
    {
        public Guid UserId { get; set; }

        // The SHA-256 hash of the refresh token, never the token itself.
        public string TokenHash { get; set; } = string.Empty;

        public DateTimeOffset ExpiresAt { get; set; }
    }
}