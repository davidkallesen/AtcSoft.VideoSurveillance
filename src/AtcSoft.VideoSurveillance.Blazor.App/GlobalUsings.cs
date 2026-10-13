global using System.Diagnostics.CodeAnalysis;
global using System.Globalization;
global using System.Text.Json;

global using Atc.Rest.Api.SourceGenerator;

global using AtcSoft.VideoSurveillance.Blazor.App;
global using AtcSoft.VideoSurveillance.Blazor.App.Logic;
global using AtcSoft.VideoSurveillance.Blazor.App.Services;

global using Microsoft.AspNetCore.Components;
global using Microsoft.AspNetCore.Components.Web;
global using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
global using Microsoft.AspNetCore.SignalR.Client;
global using Microsoft.Extensions.Http.Resilience;

global using MudBlazor;
global using MudBlazor.Services;

global using VideoSurveillance.Generated;
global using VideoSurveillance.Generated.Cameras.Client;
global using VideoSurveillance.Generated.Cameras.Endpoints.Interfaces;
global using VideoSurveillance.Generated.Cameras.Models;
global using VideoSurveillance.Generated.Layouts.Client;
global using VideoSurveillance.Generated.Layouts.Endpoints.Interfaces;
global using VideoSurveillance.Generated.Layouts.Models;
global using VideoSurveillance.Generated.Recordings.Client;
global using VideoSurveillance.Generated.Recordings.Endpoints.Interfaces;
global using VideoSurveillance.Generated.Recordings.Models;
global using VideoSurveillance.Generated.Settings.Client;
global using VideoSurveillance.Generated.Settings.Endpoints.Interfaces;
global using VideoSurveillance.Generated.Settings.Models;
global using VideoSurveillance.Generated.Webhooks.Client;
global using VideoSurveillance.Generated.Webhooks.Endpoints.Interfaces;
global using VideoSurveillance.Generated.Webhooks.Models;