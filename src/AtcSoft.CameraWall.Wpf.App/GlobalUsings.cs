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

global using Linksoft.CameraWall.Wpf.SplashScreens;

global using Linksoft.VideoEngine;
global using Linksoft.VideoEngine.DirectX;

global using Linksoft.VideoSurveillance.Models.Settings;

global using Linksoft.VideoSurveillance.Wpf.Core;
global using Linksoft.VideoSurveillance.Wpf.Core.Events;
global using Linksoft.VideoSurveillance.Wpf.Core.Helpers;
global using Linksoft.VideoSurveillance.Wpf.Core.Models;
global using Linksoft.VideoSurveillance.Wpf.Core.Resources;
global using Linksoft.VideoSurveillance.Wpf.Core.Services;
global using Linksoft.VideoSurveillance.Wpf.Core.UserControls;

global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;

global using Serilog;
global using Serilog.Events;

global using ApplicationHelper = Linksoft.VideoSurveillance.Helpers.ApplicationHelper;
global using IMediaCleanupService = Linksoft.VideoSurveillance.Services.IMediaCleanupService;
global using IRecordingSegmentationService = Linksoft.VideoSurveillance.Services.IRecordingSegmentationService;
global using IUsbCameraEnumerator = Linksoft.VideoSurveillance.Services.IUsbCameraEnumerator;
global using IUsbCameraWatcher = Linksoft.VideoSurveillance.Services.IUsbCameraWatcher;
global using MediaCleanupService = Linksoft.CameraWall.Wpf.Services.MediaCleanupService;
global using NullUsbCameraEnumerator = Linksoft.VideoSurveillance.Services.NullUsbCameraEnumerator;
global using NullUsbCameraWatcher = Linksoft.VideoSurveillance.Services.NullUsbCameraWatcher;
global using RecordingSegmentationService = Linksoft.CameraWall.Wpf.Services.RecordingSegmentationService;