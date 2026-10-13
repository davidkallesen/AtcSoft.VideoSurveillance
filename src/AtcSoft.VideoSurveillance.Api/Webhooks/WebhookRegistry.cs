namespace AtcSoft.VideoSurveillance.Api.Webhooks;

/// <summary>
/// <see cref="IWebhookRegistry"/> over two sources: the receivers in the
/// server's configuration, which stay as they are, and the ones added
/// through the API, which are kept in a file in the data folder. The
/// dispatcher asks for the receivers each time it sends, so a change takes
/// effect at once.
/// </summary>
public sealed partial class WebhookRegistry : IWebhookRegistry
{
    private static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Lock sync = new();
    private readonly string filePath;
    private readonly IWebhookSender sender;
    private readonly ILogger<WebhookRegistry> logger;
    private readonly List<Entry> configured;
    private readonly List<Entry> stored;

    // The receivers with a usable address, as the dispatcher asks for them
    // at every event. Rebuilt when a receiver is added, changed or removed.
    private IReadOnlyList<WebhookEndpoint> active = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookRegistry"/> class.
    /// </summary>
    /// <param name="filePath">The file the added receivers are kept in.</param>
    /// <param name="options">The receivers from the server's configuration.</param>
    /// <param name="sender">Sends the test event.</param>
    /// <param name="logger">The logger.</param>
    public WebhookRegistry(
        string filePath,
        WebhookOptions options,
        IWebhookSender sender,
        ILogger<WebhookRegistry> logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        ArgumentNullException.ThrowIfNull(options);

        this.filePath = filePath;
        this.sender = sender;
        this.logger = logger;

        // A client holds on to an id between two listings, so a configured
        // receiver's id follows from its place and address.
        configured =
        [
            .. options.Endpoints.Select((endpoint, index) => new Entry
            {
                Id = CreateConfiguredId(index, endpoint.Url),
                Url = endpoint.Url,
                Secret = endpoint.Secret,
                Events = [.. endpoint.Events],
                Configured = endpoint,
            }),
        ];

        foreach (var invalid in options.Endpoints.Where(e => !e.IsValid))
        {
            LogInvalidEndpoint(invalid.LogName);
        }

        stored = SafeJsonFile.TryRead<List<Entry>>(filePath, FileOptions) ?? [];
        RebuildActive();
    }

    /// <inheritdoc />
    public IReadOnlyList<WebhookRegistration> GetAll()
    {
        lock (sync)
        {
            return
            [
                .. configured.Select(e => ToRegistration(e, isEditable: false)),
                .. stored.Select(e => ToRegistration(e, isEditable: true)),
            ];
        }
    }

    /// <summary>
    /// Gets the receivers events are sent to now: every one with a usable address.
    /// </summary>
    public IReadOnlyList<WebhookEndpoint> ActiveEndpoints
    {
        get
        {
            lock (sync)
            {
                return active;
            }
        }
    }

    /// <inheritdoc />
    public (WebhookChangeOutcome Outcome, WebhookRegistration? Webhook) Add(
        string url,
        string? secret,
        IReadOnlyList<string>? events)
    {
        if (!IsUsableAddress(url))
        {
            return (WebhookChangeOutcome.InvalidAddress, null);
        }

        lock (sync)
        {
            var entry = new Entry
            {
                Id = Guid.NewGuid(),
                Url = url.Trim(),
                Secret = string.IsNullOrEmpty(secret) ? null : secret,
                Events = Tidy(events),
            };

            stored.Add(entry);
            Save();

            return (WebhookChangeOutcome.Done, ToRegistration(entry, isEditable: true));
        }
    }

    /// <inheritdoc />
    public (WebhookChangeOutcome Outcome, WebhookRegistration? Webhook) Update(
        Guid id,
        string url,
        string? secret,
        IReadOnlyList<string>? events)
    {
        lock (sync)
        {
            if (configured.Exists(e => e.Id == id))
            {
                return (WebhookChangeOutcome.NotEditable, null);
            }

            var entry = stored.Find(e => e.Id == id);
            if (entry is null)
            {
                return (WebhookChangeOutcome.NotFound, null);
            }

            if (!IsUsableAddress(url))
            {
                return (WebhookChangeOutcome.InvalidAddress, null);
            }

            entry.Url = url.Trim();
            entry.Events = Tidy(events);

            // The secret is never sent out, so a client that edits the
            // address has none to send back: nothing given keeps it.
            if (secret is not null)
            {
                entry.Secret = secret.Length == 0 ? null : secret;
            }

            Save();

            return (WebhookChangeOutcome.Done, ToRegistration(entry, isEditable: true));
        }
    }

    /// <inheritdoc />
    public WebhookChangeOutcome Delete(Guid id)
    {
        lock (sync)
        {
            if (configured.Exists(e => e.Id == id))
            {
                return WebhookChangeOutcome.NotEditable;
            }

            if (stored.RemoveAll(e => e.Id == id) == 0)
            {
                return WebhookChangeOutcome.NotFound;
            }

            Save();
            return WebhookChangeOutcome.Done;
        }
    }

    /// <inheritdoc />
    public async Task<bool?> SendTestAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        WebhookEndpoint? endpoint;
        lock (sync)
        {
            var entry = configured.Concat(stored).FirstOrDefault(e => e.Id == id);
            endpoint = entry is null ? null : ToEndpoint(entry);
        }

        if (endpoint is null)
        {
            return null;
        }

        if (!endpoint.IsValid)
        {
            return false;
        }

        var test = new WebhookEvent(
            WebhookEventTypes.Test,
            Guid.Empty,
            string.Empty,
            DateTimeOffset.UtcNow,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["message"] = "This is a test event. Nothing happened on a camera.",
            });

        return await sender.SendAsync(endpoint, test, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsUsableAddress(string? url)
        => new WebhookEndpoint { Url = url?.Trim() ?? string.Empty }.IsValid;

    // Trimmed, without blanks, and each event once (whatever its case).
    private static List<string> Tidy(IReadOnlyList<string>? events)
        => events is null
            ? []
            : [.. events
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static Guid CreateConfiguredId(
        int index,
        string url)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"configured-webhook:{index}:{url}")));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static WebhookRegistration ToRegistration(
        Entry entry,
        bool isEditable)
        => new(
            entry.Id,
            entry.Url,
            [.. entry.Events],
            HasSecret: !string.IsNullOrEmpty(entry.Secret),
            isEditable);

    private static WebhookEndpoint ToEndpoint(Entry entry)
        => entry.Configured ?? new WebhookEndpoint
        {
            Url = entry.Url,
            Secret = entry.Secret,
            Events = [.. entry.Events],
        };

    // Must be called while holding the lock (or from the constructor).
    private void RebuildActive()
        => active = [.. configured.Concat(stored).Select(ToEndpoint).Where(e => e.IsValid)];

    // Must be called while holding the lock.
    private void Save()
    {
        RebuildActive();

        if (!SafeJsonFile.TryWrite(filePath, stored, FileOptions))
        {
            LogSaveFailed(filePath);
        }
    }

    [SuppressMessage(
        "Design",
        "CA1056:URI properties should not be strings",
        Justification = "As it is kept in the file.")]
    [SuppressMessage(
        "Usage",
        "CA2227:Collection properties should be read only",
        Justification = "Set when the file is read.")]
    private sealed class Entry
    {
        public Guid Id { get; set; }

        public string Url { get; set; } = string.Empty;

        public string? Secret { get; set; }

        public List<string> Events { get; set; } = [];

        /// <summary>
        /// Gets the endpoint from the configuration this entry stands for;
        /// null for a receiver that was added through the API.
        /// </summary>
        [JsonIgnore]
        public WebhookEndpoint? Configured { get; init; }
    }
}