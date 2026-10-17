// MA0048 (file name must match type name) is suppressed via NoWarn in the
// .csproj — Aspire convention uses AppHost.cs for the top-level program.
var builder = DistributedApplication.CreateBuilder(args);

// The clients find the server through services__api__* (Blazor.App Program.cs,
// Wpf.App App.xaml.cs), so each reference keeps the name "api" whatever the
// resource is called in the dashboard.
const string apiReferenceName = "api";

var api = builder
    .AddProject<Projects.AtcSoft_VideoSurveillance_Api>("VideoSurveillance-Api")
    .WithHttpEndpoint(port: 5000, name: "public");

builder
    .AddProject<Projects.AtcSoft_VideoSurveillance_Blazor_App>("VideoSurveillance-BlazorApp")
    .WithReference(api, apiReferenceName)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder
    .AddProject<Projects.AtcSoft_VideoSurveillance_Wpf_App>("VideoSurveillance-WpfApp")
    .WithReference(api, apiReferenceName)
    .WaitFor(api);

builder
    .AddProject<Projects.AtcSoft_CameraWall_Wpf_App>("CameraWall-WpfApp");

await builder
    .Build()
    .RunAsync()
    .ConfigureAwait(false);
