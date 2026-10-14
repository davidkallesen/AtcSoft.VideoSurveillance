global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Globalization;
global using System.Text.Json;
global using System.Text.Json.Serialization;

global using Atc.Hosting;
global using AtcSoft.VideoEngine;
global using AtcSoft.VideoSurveillance.Api.Domain.ApiHandlers;
global using AtcSoft.VideoSurveillance.Api.Domain.Events;
global using AtcSoft.VideoSurveillance.Api.Domain.Mapping;
global using AtcSoft.VideoSurveillance.Api.Domain.Webhooks;
global using AtcSoft.VideoSurveillance.Api.Hubs;
global using AtcSoft.VideoSurveillance.Api.Services;
global using AtcSoft.VideoSurveillance.Api.Webhooks;
global using AtcSoft.VideoSurveillance.Enums;
global using AtcSoft.VideoSurveillance.Events;
global using AtcSoft.VideoSurveillance.Helpers;
global using AtcSoft.VideoSurveillance.Models;
global using AtcSoft.VideoSurveillance.Models.Settings;
global using AtcSoft.VideoSurveillance.Services;

global using Microsoft.AspNetCore.OutputCaching;
global using Microsoft.AspNetCore.SignalR;
global using Microsoft.AspNetCore.StaticFiles;
global using Microsoft.Extensions.FileProviders;
global using Microsoft.Extensions.Primitives;
global using Scalar.AspNetCore;
global using Serilog;
global using VideoSurveillance;
global using VideoSurveillance.Generated.Extensions;
global using VideoSurveillance.Generated.Hubs;

global using CameraSwapRequestedEvent = VideoSurveillance.Generated.Models.CameraSwapRequestedEvent;
global using HubSwapDirection = VideoSurveillance.Generated.Models.SwapDirection;
global using StreamStartedEvent = VideoSurveillance.Generated.Models.StreamStartedEvent;
global using WebhookPayload = VideoSurveillance.Generated.Models.WebhookPayload;
global using WebhookPayloadData = VideoSurveillance.Generated.Models.WebhookPayloadData;