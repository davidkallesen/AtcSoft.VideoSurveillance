namespace Linksoft.VideoSurveillance.Models.Settings;

/// <summary>
/// Application-level recording settings.
/// </summary>
public class RecordingSettings
{
    public string RecordingPath { get; set; } = ApplicationPaths.DefaultRecordingsPath;

    public string RecordingFormat { get; set; } = "mkv";

    public VideoTranscodeCodec TranscodeVideoCodec { get; set; }

    public bool EnableRecordingOnMotion { get; set; }

    public bool EnableRecordingOnConnect { get; set; }

    public MediaCleanupSettings Cleanup { get; set; } = new();

    public PlaybackOverlaySettings PlaybackOverlay { get; set; } = new();

    public bool EnableHourlySegmentation { get; set; } = true;

    public int MaxRecordingDurationMinutes { get; set; } = 60;

    public int ThumbnailTileCount { get; set; } = 4;

    public bool EnableTimelapse { get; set; }

    public string TimelapseInterval { get; set; } = "5m";

    /// <inheritdoc />
    public override string ToString()
        => $"RecordingSettings {{ Path='{RecordingPath}', Format='{RecordingFormat}' }}";
}