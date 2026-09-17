using CoreAdvancedSettings = Linksoft.VideoSurveillance.Models.Settings.AdvancedSettings;
using CoreCameraDisplayAppSettings = Linksoft.VideoSurveillance.Models.Settings.CameraDisplayAppSettings;
using CoreConnectionAppSettings = Linksoft.VideoSurveillance.Models.Settings.ConnectionAppSettings;
using CoreGeneralSettings = Linksoft.VideoSurveillance.Models.Settings.GeneralSettings;
using CoreMotionDetectionSettings = Linksoft.VideoSurveillance.Models.Settings.MotionDetectionSettings;
using CorePerformanceSettings = Linksoft.VideoSurveillance.Models.Settings.PerformanceSettings;
using CoreRecordingSettings = Linksoft.VideoSurveillance.Models.Settings.RecordingSettings;

namespace Linksoft.VideoSurveillance.Api.Domain.Mapping;

internal static class SettingsMappingExtensions
{
    public static AppSettings ToApiModel(
        CoreGeneralSettings general,
        CoreCameraDisplayAppSettings cameraDisplay,
        CoreConnectionAppSettings connection,
        CorePerformanceSettings performance,
        CoreMotionDetectionSettings motionDetection,
        CoreRecordingSettings recording,
        CoreAdvancedSettings advanced)
        => new(
            ThemeBase: ParseThemeBase(general.ThemeBase),
            ThemeAccent: general.ThemeAccent,
            Language: general.Language,
            ConnectOnStartup: general.ConnectCamerasOnStartup,
            StartMaximized: general.StartMaximized,
            ShowOverlayTitle: cameraDisplay.ShowOverlayTitle,
            ShowOverlayDescription: cameraDisplay.ShowOverlayDescription,
            ShowOverlayTime: cameraDisplay.ShowOverlayTime,
            ShowOverlayConnectionStatus: cameraDisplay.ShowOverlayConnectionStatus,
            OverlayOpacity: cameraDisplay.OverlayOpacity,
            OverlayPosition: ParseOverlayPosition(cameraDisplay.OverlayPosition),
            AllowDragAndDropReorder: cameraDisplay.AllowDragAndDropReorder,
            AutoSaveLayoutChanges: cameraDisplay.AutoSaveLayoutChanges,
            SnapshotPath: cameraDisplay.SnapshotPath,
            DefaultProtocol: ParseDefaultProtocol(connection.DefaultProtocol),
            DefaultPort: connection.DefaultPort,
            ConnectionTimeoutSeconds: connection.ConnectionTimeoutSeconds,
            ReconnectDelaySeconds: connection.ReconnectDelaySeconds,
            AutoReconnectOnFailure: connection.AutoReconnectOnFailure,
            ShowNotificationOnDisconnect: connection.ShowNotificationOnDisconnect,
            ShowNotificationOnReconnect: connection.ShowNotificationOnReconnect,
            PlayNotificationSound: connection.PlayNotificationSound,
            VideoQuality: ParseVideoQuality(performance.VideoQuality),
            HardwareAcceleration: performance.HardwareAcceleration,
            LowLatencyMode: performance.LowLatencyMode,
            BufferDurationMs: performance.BufferDurationMs,
            RtspTransport: ParseRtspTransport(performance.RtspTransport),
            MaxLatencyMs: performance.MaxLatencyMs,
            MotionSensitivity: motionDetection.Sensitivity,
            MinimumChangePercent: motionDetection.MinimumChangePercent,
            AnalysisFrameRate: motionDetection.AnalysisFrameRate,
            AnalysisWidth: motionDetection.AnalysisWidth,
            AnalysisHeight: motionDetection.AnalysisHeight,
            PostMotionDurationSeconds: motionDetection.PostMotionDurationSeconds,
            CooldownSeconds: motionDetection.CooldownSeconds,
            BoundingBoxShowInGrid: motionDetection.BoundingBox.ShowInGrid,
            BoundingBoxShowInFullScreen: motionDetection.BoundingBox.ShowInFullScreen,
            BoundingBoxColor: motionDetection.BoundingBox.Color,
            BoundingBoxThickness: motionDetection.BoundingBox.Thickness,
            BoundingBoxMinArea: motionDetection.BoundingBox.MinArea,
            BoundingBoxPadding: motionDetection.BoundingBox.Padding,
            BoundingBoxSmoothing: motionDetection.BoundingBox.Smoothing,
            RecordingPath: recording.RecordingPath,
            RecordingFormat: ParseRecordingFormat(recording.RecordingFormat),
            EnableRecordingOnMotion: recording.EnableRecordingOnMotion,
            EnableRecordingOnConnect: recording.EnableRecordingOnConnect,
            EnableHourlySegmentation: recording.EnableHourlySegmentation,
            MaxRecordingDurationMinutes: recording.MaxRecordingDurationMinutes,
            ThumbnailTileCount: recording.ThumbnailTileCount,
            EnableTimelapse: recording.EnableTimelapse,
            TimelapseInterval: recording.TimelapseInterval,
            CleanupSchedule: ParseCleanupSchedule(recording.Cleanup.Schedule),
            RecordingRetentionDays: recording.Cleanup.RecordingRetentionDays,
            CleanupIncludeSnapshots: recording.Cleanup.IncludeSnapshots,
            SnapshotRetentionDays: recording.Cleanup.SnapshotRetentionDays,
            PlaybackShowFilename: recording.PlaybackOverlay.ShowFilename,
            PlaybackFilenameColor: recording.PlaybackOverlay.FilenameColor,
            PlaybackShowTimestamp: recording.PlaybackOverlay.ShowTimestamp,
            PlaybackTimestampColor: recording.PlaybackOverlay.TimestampColor,
            EnableDebugLogging: advanced.EnableDebugLogging,
            LogPath: advanced.LogPath);

    public static void ApplyToCore(
        this AppSettings api,
        CoreGeneralSettings general,
        CoreCameraDisplayAppSettings cameraDisplay,
        CoreConnectionAppSettings connection,
        CorePerformanceSettings performance,
        CoreMotionDetectionSettings motionDetection,
        CoreRecordingSettings recording,
        CoreAdvancedSettings advanced)
    {
        if (api.ThemeBase is not null)
        {
            general.ThemeBase = api.ThemeBase.Value.ToString();
        }

        if (!string.IsNullOrEmpty(api.ThemeAccent))
        {
            general.ThemeAccent = api.ThemeAccent;
        }

        if (!string.IsNullOrEmpty(api.Language))
        {
            general.Language = api.Language;
        }

        general.ConnectCamerasOnStartup = api.ConnectOnStartup;
        general.StartMaximized = api.StartMaximized;

        cameraDisplay.ShowOverlayTitle = api.ShowOverlayTitle;
        cameraDisplay.ShowOverlayDescription = api.ShowOverlayDescription;
        cameraDisplay.ShowOverlayTime = api.ShowOverlayTime;
        cameraDisplay.ShowOverlayConnectionStatus = api.ShowOverlayConnectionStatus;
        cameraDisplay.OverlayOpacity = api.OverlayOpacity;

        if (api.OverlayPosition is not null)
        {
            cameraDisplay.OverlayPosition = ToCoreOverlayPosition(api.OverlayPosition.Value);
        }

        cameraDisplay.AllowDragAndDropReorder = api.AllowDragAndDropReorder;
        cameraDisplay.AutoSaveLayoutChanges = api.AutoSaveLayoutChanges;

        if (!string.IsNullOrEmpty(api.SnapshotPath))
        {
            cameraDisplay.SnapshotPath = api.SnapshotPath;
        }

        if (api.DefaultProtocol is not null)
        {
            connection.DefaultProtocol = ToCoreProtocol(api.DefaultProtocol.Value);
        }

        if (api.DefaultPort > 0)
        {
            connection.DefaultPort = api.DefaultPort;
        }

        if (api.ConnectionTimeoutSeconds > 0)
        {
            connection.ConnectionTimeoutSeconds = api.ConnectionTimeoutSeconds;
        }

        if (api.ReconnectDelaySeconds > 0)
        {
            connection.ReconnectDelaySeconds = api.ReconnectDelaySeconds;
        }

        connection.AutoReconnectOnFailure = api.AutoReconnectOnFailure;
        connection.ShowNotificationOnDisconnect = api.ShowNotificationOnDisconnect;
        connection.ShowNotificationOnReconnect = api.ShowNotificationOnReconnect;
        connection.PlayNotificationSound = api.PlayNotificationSound;

        if (api.VideoQuality is not null)
        {
            performance.VideoQuality = api.VideoQuality.Value.ToString();
        }

        performance.HardwareAcceleration = api.HardwareAcceleration;
        performance.LowLatencyMode = api.LowLatencyMode;

        if (api.BufferDurationMs > 0)
        {
            performance.BufferDurationMs = api.BufferDurationMs;
        }

        if (api.RtspTransport is not null)
        {
            performance.RtspTransport = api.RtspTransport.Value.ToString().ToLowerInvariant();
        }

        if (api.MaxLatencyMs > 0)
        {
            performance.MaxLatencyMs = api.MaxLatencyMs;
        }

        if (api.MotionSensitivity > 0)
        {
            motionDetection.Sensitivity = api.MotionSensitivity;
        }

        if (api.MinimumChangePercent > 0)
        {
            motionDetection.MinimumChangePercent = api.MinimumChangePercent;
        }

        if (api.AnalysisFrameRate > 0)
        {
            motionDetection.AnalysisFrameRate = api.AnalysisFrameRate;
        }

        if (api.AnalysisWidth > 0)
        {
            motionDetection.AnalysisWidth = api.AnalysisWidth;
        }

        if (api.AnalysisHeight > 0)
        {
            motionDetection.AnalysisHeight = api.AnalysisHeight;
        }

        if (api.PostMotionDurationSeconds > 0)
        {
            motionDetection.PostMotionDurationSeconds = api.PostMotionDurationSeconds;
        }

        if (api.CooldownSeconds > 0)
        {
            motionDetection.CooldownSeconds = api.CooldownSeconds;
        }

        motionDetection.BoundingBox.ShowInGrid = api.BoundingBoxShowInGrid;
        motionDetection.BoundingBox.ShowInFullScreen = api.BoundingBoxShowInFullScreen;

        if (!string.IsNullOrEmpty(api.BoundingBoxColor))
        {
            motionDetection.BoundingBox.Color = api.BoundingBoxColor;
        }

        if (api.BoundingBoxThickness > 0)
        {
            motionDetection.BoundingBox.Thickness = api.BoundingBoxThickness;
        }

        if (api.BoundingBoxMinArea > 0)
        {
            motionDetection.BoundingBox.MinArea = api.BoundingBoxMinArea;
        }

        if (api.BoundingBoxPadding >= 0)
        {
            motionDetection.BoundingBox.Padding = api.BoundingBoxPadding;
        }

        if (api.BoundingBoxSmoothing >= 0)
        {
            motionDetection.BoundingBox.Smoothing = api.BoundingBoxSmoothing;
        }

        if (!string.IsNullOrEmpty(api.RecordingPath))
        {
            recording.RecordingPath = api.RecordingPath;
        }

        if (api.RecordingFormat is not null)
        {
            recording.RecordingFormat = api.RecordingFormat.Value.ToString().ToLowerInvariant();
        }

        recording.EnableRecordingOnMotion = api.EnableRecordingOnMotion;
        recording.EnableRecordingOnConnect = api.EnableRecordingOnConnect;
        recording.EnableHourlySegmentation = api.EnableHourlySegmentation;

        if (api.MaxRecordingDurationMinutes > 0)
        {
            recording.MaxRecordingDurationMinutes = api.MaxRecordingDurationMinutes;
        }

        if (api.ThumbnailTileCount > 0)
        {
            recording.ThumbnailTileCount = api.ThumbnailTileCount;
        }

        recording.EnableTimelapse = api.EnableTimelapse;

        if (!string.IsNullOrEmpty(api.TimelapseInterval))
        {
            recording.TimelapseInterval = api.TimelapseInterval;
        }

        if (api.CleanupSchedule is not null)
        {
            recording.Cleanup.Schedule = ToCoreCleanupSchedule(api.CleanupSchedule.Value);
        }

        if (api.RecordingRetentionDays > 0)
        {
            recording.Cleanup.RecordingRetentionDays = api.RecordingRetentionDays;
        }

        recording.Cleanup.IncludeSnapshots = api.CleanupIncludeSnapshots;

        if (api.SnapshotRetentionDays > 0)
        {
            recording.Cleanup.SnapshotRetentionDays = api.SnapshotRetentionDays;
        }

        recording.PlaybackOverlay.ShowFilename = api.PlaybackShowFilename;

        if (!string.IsNullOrEmpty(api.PlaybackFilenameColor))
        {
            recording.PlaybackOverlay.FilenameColor = api.PlaybackFilenameColor;
        }

        recording.PlaybackOverlay.ShowTimestamp = api.PlaybackShowTimestamp;

        if (!string.IsNullOrEmpty(api.PlaybackTimestampColor))
        {
            recording.PlaybackOverlay.TimestampColor = api.PlaybackTimestampColor;
        }

        advanced.EnableDebugLogging = api.EnableDebugLogging;

        if (!string.IsNullOrEmpty(api.LogPath))
        {
            advanced.LogPath = api.LogPath;
        }
    }

    private static AppSettingsThemeBase? ParseThemeBase(string? themeBase)
    {
        if (string.IsNullOrEmpty(themeBase))
        {
            return null;
        }

        return string.Equals(themeBase, "Light", StringComparison.OrdinalIgnoreCase)
            ? AppSettingsThemeBase.Light
            : AppSettingsThemeBase.Dark;
    }

    private static AppSettingsRecordingFormat? ParseRecordingFormat(
        string? format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return null;
        }

        return format.ToUpperInvariant() switch
        {
            "MKV" => AppSettingsRecordingFormat.Mkv,
            "AVI" => AppSettingsRecordingFormat.Avi,
            _ => AppSettingsRecordingFormat.Mp4,
        };
    }

    private static AppSettingsOverlayPosition? ParseOverlayPosition(
        Linksoft.VideoSurveillance.Enums.OverlayPosition position)
        => position switch
        {
            Linksoft.VideoSurveillance.Enums.OverlayPosition.TopRight => AppSettingsOverlayPosition.TopRight,
            Linksoft.VideoSurveillance.Enums.OverlayPosition.BottomLeft => AppSettingsOverlayPosition.BottomLeft,
            Linksoft.VideoSurveillance.Enums.OverlayPosition.BottomRight => AppSettingsOverlayPosition.BottomRight,
            _ => AppSettingsOverlayPosition.TopLeft,
        };

    private static Linksoft.VideoSurveillance.Enums.OverlayPosition ToCoreOverlayPosition(
        AppSettingsOverlayPosition position)
        => position switch
        {
            AppSettingsOverlayPosition.TopRight => Linksoft.VideoSurveillance.Enums.OverlayPosition.TopRight,
            AppSettingsOverlayPosition.BottomLeft => Linksoft.VideoSurveillance.Enums.OverlayPosition.BottomLeft,
            AppSettingsOverlayPosition.BottomRight => Linksoft.VideoSurveillance.Enums.OverlayPosition.BottomRight,
            _ => Linksoft.VideoSurveillance.Enums.OverlayPosition.TopLeft,
        };

    private static AppSettingsDefaultProtocol? ParseDefaultProtocol(
        Linksoft.VideoSurveillance.Enums.CameraProtocol protocol)
        => protocol switch
        {
            Linksoft.VideoSurveillance.Enums.CameraProtocol.Http => AppSettingsDefaultProtocol.Http,
            Linksoft.VideoSurveillance.Enums.CameraProtocol.Https => AppSettingsDefaultProtocol.Https,
            _ => AppSettingsDefaultProtocol.Rtsp,
        };

    private static Linksoft.VideoSurveillance.Enums.CameraProtocol ToCoreProtocol(
        AppSettingsDefaultProtocol protocol)
        => protocol switch
        {
            AppSettingsDefaultProtocol.Http => Linksoft.VideoSurveillance.Enums.CameraProtocol.Http,
            AppSettingsDefaultProtocol.Https => Linksoft.VideoSurveillance.Enums.CameraProtocol.Https,
            _ => Linksoft.VideoSurveillance.Enums.CameraProtocol.Rtsp,
        };

    private static AppSettingsVideoQuality? ParseVideoQuality(string? quality)
    {
        if (string.IsNullOrEmpty(quality))
        {
            return null;
        }

        return quality.ToUpperInvariant() switch
        {
            "LOW" => AppSettingsVideoQuality.Low,
            "MEDIUM" => AppSettingsVideoQuality.Medium,
            "HIGH" => AppSettingsVideoQuality.High,
            "ULTRA" => AppSettingsVideoQuality.Ultra,
            _ => AppSettingsVideoQuality.Auto,
        };
    }

    private static AppSettingsRtspTransport? ParseRtspTransport(
        string? transport)
    {
        if (string.IsNullOrEmpty(transport))
        {
            return null;
        }

        return string.Equals(transport, "udp", StringComparison.OrdinalIgnoreCase)
            ? AppSettingsRtspTransport.Udp
            : AppSettingsRtspTransport.Tcp;
    }

    private static AppSettingsCleanupSchedule? ParseCleanupSchedule(
        Linksoft.VideoSurveillance.Enums.MediaCleanupSchedule schedule)
        => schedule switch
        {
            Linksoft.VideoSurveillance.Enums.MediaCleanupSchedule.OnStartup => AppSettingsCleanupSchedule.OnStartup,
            Linksoft.VideoSurveillance.Enums.MediaCleanupSchedule.OnStartupAndPeriodically => AppSettingsCleanupSchedule.OnStartupAndPeriodically,
            _ => AppSettingsCleanupSchedule.Disabled,
        };

    private static Linksoft.VideoSurveillance.Enums.MediaCleanupSchedule ToCoreCleanupSchedule(
        AppSettingsCleanupSchedule schedule)
        => schedule switch
        {
            AppSettingsCleanupSchedule.OnStartup => Linksoft.VideoSurveillance.Enums.MediaCleanupSchedule.OnStartup,
            AppSettingsCleanupSchedule.OnStartupAndPeriodically => Linksoft.VideoSurveillance.Enums.MediaCleanupSchedule.OnStartupAndPeriodically,
            _ => Linksoft.VideoSurveillance.Enums.MediaCleanupSchedule.Disabled,
        };
}