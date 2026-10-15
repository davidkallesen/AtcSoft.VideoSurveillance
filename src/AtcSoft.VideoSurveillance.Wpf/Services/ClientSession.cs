namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// The signed-in session of this client: the access token every call sends,
/// and the refresh token that renews it.
/// </summary>
public sealed class ClientSession(TimeProvider time) : IDisposable
{
    // A token this close to running out is renewed before a call.
    private static readonly TimeSpan RenewAhead = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim renewing = new(1, 1);
    private DateTimeOffset expiresAt;

    /// <summary>
    /// Raised when the user signs in, the session is renewed, or it ends.
    /// </summary>
    public event Action? Changed;

    public string? AccessToken { get; private set; }

    public string? RefreshToken { get; private set; }

    public string? UserName { get; private set; }

    public UserRole? Role { get; private set; }

    public bool IsSignedIn
        => AccessToken is not null;

    public bool IsAdmin
        => Role?.Value == UserRole.Names.Admin;

    /// <summary>
    /// Gets a value indicating whether the access token is about to run out.
    /// </summary>
    public bool NeedsRenewal
        => RefreshToken is not null && (AccessToken is null || time.GetUtcNow() >= expiresAt - RenewAhead);

    /// <summary>
    /// Gets or sets how the session is renewed (the auth service sets it).
    /// </summary>
    public Func<CancellationToken, Task<bool>>? Renew { get; set; }

    public void Apply(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        AccessToken = session.AccessToken;
        RefreshToken = session.RefreshToken;
        UserName = session.UserName;
        Role = session.Role;
        expiresAt = time.GetUtcNow() + TimeSpan.FromSeconds(session.ExpiresInSeconds);
        Changed?.Invoke();
    }

    /// <summary>
    /// Keeps a refresh token from an earlier visit, to be renewed into a session.
    /// </summary>
    public void Restore(
        string refreshToken,
        string? userName)
    {
        AccessToken = null;
        RefreshToken = refreshToken;
        UserName = userName;
    }

    public void Clear()
    {
        AccessToken = null;
        RefreshToken = null;
        UserName = null;
        Role = null;
        Changed?.Invoke();
    }

    public void Dispose()
        => renewing.Dispose();

    /// <summary>
    /// Waits until the client is signed in (the event stream waits so).
    /// </summary>
    public async Task WaitUntilSignedInAsync(
        CancellationToken cancellationToken)
    {
        if (IsSignedIn)
        {
            return;
        }

        var signedIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged()
        {
            if (IsSignedIn)
            {
                signedIn.TrySetResult();
            }
        }

        Changed += OnChanged;
        try
        {
            OnChanged();
            await signedIn.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Changed -= OnChanged;
        }
    }

    /// <summary>
    /// Renews the session once, however many calls ask at the same time; a
    /// session that cannot be renewed ends.
    /// </summary>
    /// <param name="cancellationToken">Ends the wait.</param>
    /// <param name="staleToken">The token a call was refused with; already renewed past it, nothing is done.</param>
    public async Task<bool> RenewAsync(
        CancellationToken cancellationToken,
        string? staleToken = null)
    {
        var acquired = false;
        try
        {
            await renewing.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;

            if (staleToken is not null && AccessToken is not null && !string.Equals(staleToken, AccessToken, StringComparison.Ordinal))
            {
                return true;
            }

            if (staleToken is null && !NeedsRenewal)
            {
                return IsSignedIn;
            }

            if (Renew is null || RefreshToken is null || !await Renew(cancellationToken).ConfigureAwait(false))
            {
                Clear();
                return false;
            }

            return true;
        }
        finally
        {
            if (acquired)
            {
                renewing.Release();
            }
        }
    }
}