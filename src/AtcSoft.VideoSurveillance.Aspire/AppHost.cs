// MA0048 (file name must match type name) is suppressed via NoWarn in the
// .csproj — Aspire convention uses AppHost.cs for the top-level program.
var builder = DistributedApplication.CreateBuilder(args);

var api = builder
    .AddProject<Projects.AtcSoft_VideoSurveillance_Api>("api")
    .WithHttpEndpoint(port: 5000, name: "public");

builder
    .AddProject<Projects.AtcSoft_VideoSurveillance_Blazor_App>("blazor")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder
    .AddProject<Projects.AtcSoft_VideoSurveillance_Wpf_App>("wpf")
    .WithReference(api)
    .WaitFor(api);

builder
    .AddProject<Projects.AtcSoft_CameraWall_Wpf_App>("CameraWall");

await builder
    .Build()
    .RunAsync()
    .ConfigureAwait(false);