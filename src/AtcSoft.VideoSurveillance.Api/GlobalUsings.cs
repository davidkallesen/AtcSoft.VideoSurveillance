global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Text.Json;
global using System.Text.Json.Serialization;

global using Atc.Hosting;
global using Linksoft.VideoEngine;
global using Linksoft.VideoSurveillance.Api.Hubs;
global using Linksoft.VideoSurveillance.Api.Services;
global using Linksoft.VideoSurveillance.Enums;
global using Linksoft.VideoSurveillance.Events;
global using Linksoft.VideoSurveillance.Helpers;
global using Linksoft.VideoSurveillance.Models;
global using Linksoft.VideoSurveillance.Models.Settings;
global using Linksoft.VideoSurveillance.Services;

global using Microsoft.AspNetCore.SignalR;
global using Scalar.AspNetCore;
global using Serilog;
global using VideoSurveillance;
global using VideoSurveillance.Generated.Endpoints;