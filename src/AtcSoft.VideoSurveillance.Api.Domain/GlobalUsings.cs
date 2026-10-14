global using System;
global using System.Diagnostics.CodeAnalysis;
global using System.Globalization;
global using System.Threading;
global using System.Threading.Tasks;

global using AtcSoft.VideoSurveillance.Api.Domain.Mapping;
global using AtcSoft.VideoSurveillance.Api.Domain.Webhooks;
global using AtcSoft.VideoSurveillance.Helpers;
global using AtcSoft.VideoSurveillance.Services;

global using Microsoft.AspNetCore.Mvc;

global using VideoSurveillance.Generated.Cameras.Handlers;
global using VideoSurveillance.Generated.Cameras.Models;
global using VideoSurveillance.Generated.Cameras.Parameters;
global using VideoSurveillance.Generated.Cameras.Results;
global using VideoSurveillance.Generated.Devices.Handlers;
global using VideoSurveillance.Generated.Devices.Models;
global using VideoSurveillance.Generated.Devices.Results;
global using VideoSurveillance.Generated.Layouts.Handlers;
global using VideoSurveillance.Generated.Layouts.Models;
global using VideoSurveillance.Generated.Layouts.Parameters;
global using VideoSurveillance.Generated.Layouts.Results;
global using VideoSurveillance.Generated.Recordings.Handlers;
global using VideoSurveillance.Generated.Recordings.Models;
global using VideoSurveillance.Generated.Recordings.Parameters;
global using VideoSurveillance.Generated.Recordings.Results;
global using VideoSurveillance.Generated.Settings.Handlers;
global using VideoSurveillance.Generated.Settings.Models;
global using VideoSurveillance.Generated.Settings.Parameters;
global using VideoSurveillance.Generated.Settings.Results;
global using VideoSurveillance.Generated.Webhooks.Handlers;
global using VideoSurveillance.Generated.Webhooks.Models;
global using VideoSurveillance.Generated.Webhooks.Parameters;
global using VideoSurveillance.Generated.Webhooks.Results;