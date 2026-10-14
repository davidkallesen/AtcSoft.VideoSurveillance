namespace AtcSoft.VideoSurveillance.Api.Host;

/// <summary>
/// Runs the real server in-process against a data folder of its own, so the
/// tests go through HTTP, routing, serialization and the real services
/// without touching the machine's own server data.
/// </summary>
public sealed class ApiHostFixture : IDisposable
{
    /// <summary>
    /// The secret the server signs its webhooks with in the host tests.
    /// </summary>
    public const string WebhookSecret = "host-test-secret";

    private readonly Lazy<WebApplicationFactory<Program>> factory;

    public ApiHostFixture()
    {
        DataPath = Path.Combine(Path.GetTempPath(), "atcsoft-api-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataPath);

        // Read by the server before its host is built (logging set-up) and
        // again by its configuration.
        Environment.SetEnvironmentVariable("Storage__DataPath", DataPath);

        // The server posts its camera events here.
        Webhooks = new WebhookReceiver();
        Environment.SetEnvironmentVariable("Webhooks__Endpoints__0__Url", Webhooks.Address);
        Environment.SetEnvironmentVariable("Webhooks__Endpoints__0__Secret", WebhookSecret);

        factory = new Lazy<WebApplicationFactory<Program>>(() => new WebApplicationFactory<Program>());
    }

    /// <summary>
    /// Gets the folder the server keeps its settings, cameras and media in.
    /// </summary>
    public string DataPath { get; }

    /// <summary>
    /// Gets the receiver of the server's webhooks.
    /// </summary>
    public WebhookReceiver Webhooks { get; }

    /// <summary>
    /// Gets the folder recordings are stored in when the settings are untouched.
    /// </summary>
    public string DefaultRecordingPath => Path.Combine(DataPath, "recordings");

    /// <summary>
    /// Gets a client for the running server. The server needs the FFmpeg
    /// shared libraries to start, so a machine without them skips the test.
    /// </summary>
    public HttpClient CreateClient()
    {
        Assert.SkipWhen(
            AtcSoft.VideoEngine.VideoEngineBootstrap.FindFFmpegPath() is null,
            "FFmpeg shared libraries are not installed.");

        var client = factory.Value.CreateClient();

        // Never let a test write into the machine's own server data: if the
        // server did not take the data folder, stop here.
        if (!File.Exists(Path.Combine(DataPath, "settings.json")))
        {
            client.Dispose();
            throw new InvalidOperationException(
                $"The server did not start in the test data folder '{DataPath}'; refusing to run host tests against its real data.");
        }

        return client;
    }

    /// <summary>
    /// Gets a service of the running server.
    /// </summary>
    /// <typeparam name="T">The service.</typeparam>
    public T GetService<T>()
        where T : notnull
    {
        // Makes sure the server is running in the test data folder.
        using var client = CreateClient();

        return factory.Value.Services.GetRequiredService<T>();
    }

    /// <summary>
    /// Sends one request to the running server, shaped on its context: for
    /// what a client cannot set, such as the address it calls from.
    /// </summary>
    public async Task<HttpContext> SendAsync(Action<HttpContext> configure)
    {
        // Makes sure the server is running in the test data folder.
        using var client = CreateClient();

        return await factory.Value.Server.SendAsync(configure);
    }

    /// <summary>
    /// Gets a SignalR connection to the running server's hub.
    /// </summary>
    public HubConnection CreateHubConnection()
    {
        // Makes sure the server is running in the test data folder.
        using var client = CreateClient();

        return new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Value.Server.BaseAddress, "hubs/surveillance"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => factory.Value.Server.CreateHandler();
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                })
            .Build();
    }

    public void Dispose()
    {
        if (factory.IsValueCreated)
        {
            factory.Value.Dispose();
        }

        Environment.SetEnvironmentVariable("Storage__DataPath", null);
        Environment.SetEnvironmentVariable("Webhooks__Endpoints__0__Url", null);
        Environment.SetEnvironmentVariable("Webhooks__Endpoints__0__Secret", null);
        Webhooks.DisposeAsync().AsTask().GetAwaiter().GetResult();

        try
        {
            Directory.Delete(DataPath, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a temp folder left behind is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}