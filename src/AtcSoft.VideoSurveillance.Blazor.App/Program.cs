var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure HttpClient with API base address
// For Aspire: uses services__api__https__0 or services__api__http__0 environment variable
// For standalone: uses ApiBaseAddress from appsettings.json or falls back to default
var apiBaseAddress = builder.Configuration["services:api:https:0"]
    ?? builder.Configuration["services:api:http:0"]
    ?? builder.Configuration["ApiBaseAddress"]
    ?? "http://localhost:5000";

// Register HttpClient factory with named client for the API
builder.Services
    .AddHttpClient("VideoSurveillance-ApiClient", client =>
    {
        client.BaseAddress = new Uri(apiBaseAddress);
    })
    .AddStandardResilienceHandler(options =>
    {
        // Some calls are slow by nature: starting a recording waits for the
        // camera to connect, and saving a changed folder layout moves the
        // recordings before it answers. The defaults (10 s per attempt)
        // would give up on them and try again.
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);

        // Only reads are repeated. Repeating a save, a start or a delete
        // that did reach the server does it twice.
        options.Retry.DisableForUnsafeHttpMethods();
    });

// Register all generated endpoints (includes IHttpMessageFactory registration)
builder.Services.AddVideoSurveillanceEndpoints();

// Register MudBlazor services
builder.Services.AddMudServices();

// Register Gateway service with API base URL for static file URL building
builder.Services.AddScoped(sp =>
{
    var svc = ActivatorUtilities.CreateInstance<GatewayService>(sp);
    svc.ApiBaseUrl = apiBaseAddress;
    return svc;
});

// Register Surveillance Hub service for real-time updates
builder.Services.AddScoped(_ => new SurveillanceHubService(apiBaseAddress));

await builder
    .Build()
    .RunAsync()
    .ConfigureAwait(false);