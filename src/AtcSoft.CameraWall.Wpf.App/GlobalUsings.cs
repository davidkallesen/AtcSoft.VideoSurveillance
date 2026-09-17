global using System;
global using System.Collections.ObjectModel;
global using System.ComponentModel;
global using System.Diagnostics;
global using System.Globalization;
global using System.IO;
global using System.Linq;
global using System.Text.Json;
global using System.Windows;
global using System.Windows.Input;
global using System.Windows.Threading;

global using Atc;
global using Atc.DependencyInjection;
global using Atc.Helpers;
global using Atc.Wpf.Components.Dialogs;
global using Atc.Wpf.Components.Notifications;
global using Atc.Wpf.Forms.Dialogs;
global using Atc.Wpf.Notifications;
global using Atc.Wpf.Theming.Helpers;
global using Atc.Wpf.Translation;
global using Atc.XamlToolkit.Diagnostics;
global using Atc.XamlToolkit.Mvvm;

global using AtcSoft.CameraWall.Wpf.SplashScreens;

global using AtcSoft.VideoEngine;
global using AtcSoft.VideoEngine.DirectX;

global using AtcSoft.VideoSurveillance.Models.Settings;

global using AtcSoft.VideoSurveillance.Wpf.Core;
global using AtcSoft.VideoSurveillance.Wpf.Core.Events;
global using AtcSoft.VideoSurveillance.Wpf.Core.Helpers;
global using AtcSoft.VideoSurveillance.Wpf.Core.Models;
global using AtcSoft.VideoSurveillance.Wpf.Core.Resources;
global using AtcSoft.VideoSurveillance.Wpf.Core.Services;
global using AtcSoft.VideoSurveillance.Wpf.Core.UserControls;

global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;

global using Serilog;
global using Serilog.Events;

global using ApplicationHelper = AtcSoft.VideoSurveillance.Helpers.ApplicationHelper;
global using IMediaCleanupService = AtcSoft.VideoSurveillance.Services.IMediaCleanupService;
global using IRecordingSegmentationService = AtcSoft.VideoSurveillance.Services.IRecordingSegmentationService;
global using IUsbCameraEnumerator = AtcSoft.VideoSurveillance.Services.IUsbCameraEnumerator;
global using IUsbCameraWatcher = AtcSoft.VideoSurveillance.Services.IUsbCameraWatcher;
global using MediaCleanupService = AtcSoft.CameraWall.Wpf.Services.MediaCleanupService;
global using NullUsbCameraEnumerator = AtcSoft.VideoSurveillance.Services.NullUsbCameraEnumerator;
global using NullUsbCameraWatcher = AtcSoft.VideoSurveillance.Services.NullUsbCameraWatcher;
global using RecordingSegmentationService = AtcSoft.CameraWall.Wpf.Services.RecordingSegmentationService;