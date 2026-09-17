global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Text.Json;
global using System.Text.Json.Serialization;

global using Atc.Hosting;
global using AtcSoft.VideoEngine;
global using AtcSoft.VideoSurveillance.Api.Hubs;
global using AtcSoft.VideoSurveillance.Api.Services;
global using AtcSoft.VideoSurveillance.Enums;
global using AtcSoft.VideoSurveillance.Events;
global using AtcSoft.VideoSurveillance.Helpers;
global using AtcSoft.VideoSurveillance.Models;
global using AtcSoft.VideoSurveillance.Models.Settings;
global using AtcSoft.VideoSurveillance.Services;

global using Microsoft.AspNetCore.SignalR;
global using Scalar.AspNetCore;
global using Serilog;
global using VideoSurveillance;
global using VideoSurveillance.Generated.Endpoints;