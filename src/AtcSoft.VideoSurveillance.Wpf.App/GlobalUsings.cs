global using System;
global using System.Collections.Generic;
global using System.Collections.ObjectModel;
global using System.ComponentModel;
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.Globalization;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Runtime.InteropServices;
global using System.Security;
global using System.Text.Json;
global using System.Text.Json.Serialization;
global using System.Windows;
global using System.Windows.Input;
global using System.Windows.Threading;

global using Atc;
global using Atc.Wpf.Components.Dialogs;
global using Atc.Wpf.Components.Notifications;
global using Atc.Wpf.DependencyObjects;
global using Atc.Wpf.Forms.Dialogs;
global using Atc.Wpf.Notifications;
global using Atc.Wpf.Theming.Helpers;
global using Atc.XamlToolkit.Controls.Attributes;
global using Atc.XamlToolkit.Diagnostics;
global using Atc.XamlToolkit.Mvvm;

global using Linksoft.VideoEngine;
global using Linksoft.VideoEngine.DirectX;

global using Linksoft.VideoSurveillance.Services;
global using Linksoft.VideoSurveillance.Wpf.App.Models;
global using Linksoft.VideoSurveillance.Wpf.App.Services;
global using Linksoft.VideoSurveillance.Wpf.Core;
global using Linksoft.VideoSurveillance.Wpf.Core.Dialogs;
global using Linksoft.VideoSurveillance.Wpf.Core.Resources;
global using Linksoft.VideoSurveillance.Wpf.Core.Services;
global using Linksoft.VideoSurveillance.Wpf.Models;
global using Linksoft.VideoSurveillance.Wpf.Services;
global using Linksoft.VideoSurveillance.Wpf.ViewModels;

global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Win32;

global using Serilog;
global using Serilog.Events;

global using VideoSurveillance.Generated;
global using VideoSurveillance.Generated.Cameras.Models;

global using ApplicationHelper = Linksoft.VideoSurveillance.Helpers.ApplicationHelper;
global using IApplicationSettingsService = Linksoft.VideoSurveillance.Wpf.Core.Services.IApplicationSettingsService;