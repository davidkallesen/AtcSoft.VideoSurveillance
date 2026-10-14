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

// Register HttpClient factory with named client for the API, with the
// resilience the specification declares (x-retry-*): two minutes per attempt
// for the calls that are slow by nature, and only reads are repeated.
builder.Services
    .AddHttpClient("VideoSurveillance-ApiClient", client =>
    {
        client.BaseAddress = new Uri(apiBaseAddress);
    })
    .AddHttpMessageHandler(() => new ServerUnavailableHandler())
    .AddApiResilience(ResiliencePolicies.Standard);

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

// The server's event stream (GET /events), one reader per page that runs it
builder.Services.AddTransient<EventStreamService>();

await builder
    .Build()
    .RunAsync()
    .ConfigureAwait(false);