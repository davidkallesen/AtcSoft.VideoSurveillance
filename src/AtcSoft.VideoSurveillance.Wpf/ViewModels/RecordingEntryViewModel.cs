namespace AtcSoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// Adapts the API Recording model for display in the recordings browser.
/// </summary>
public sealed class RecordingEntryViewModel
{
    private readonly Recording recording;
    private readonly Uri apiBaseUrl;

    public RecordingEntryViewModel(
        Recording recording,
        Uri apiBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(apiBaseUrl);

        this.recording = recording;
        this.apiBaseUrl = apiBaseUrl;
    }

    public Guid Id => recording.Id;

    public Guid CameraId => recording.CameraId;

    public string CameraName => recording.CameraName ?? string.Empty;

    public string FileName => System.IO.Path.GetFileName(recording.FilePath);

    public DateTimeOffset RecordingTime => recording.StartedAt;

    public long FileSizeBytes => recording.FileSizeBytes ?? 0;

    public TimeSpan Duration => ParseIsoDuration(recording.Duration);

    public bool HasThumbnail => recording.HasThumbnail ?? false;

    public Uri PlaybackUrl
        => BuildFileUrl(recording.FilePath);

    /// <summary>
    /// Gets where the thumbnail is fetched from, or <c>null</c> when the
    /// recording has none.
    /// </summary>
    public Uri? ThumbnailUrl
        => string.IsNullOrEmpty(recording.ThumbnailPath)
            ? null
            : BuildFileUrl(recording.ThumbnailPath);

    public string FormattedFileSize => FormatFileSize(FileSizeBytes);

    public string FormattedDuration => FormatDuration(Duration);

    public string FormattedRecordingTime
        => RecordingTime.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    private Uri BuildFileUrl(string relativePath)
        => new($"{apiBaseUrl.GetLeftPart(UriPartial.Authority)}/recordings-files/{AtcSoft.VideoSurveillance.Helpers.RecordingPathHelper.EscapePathSegments(relativePath)}");

    private static TimeSpan ParseIsoDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration))
        {
            return TimeSpan.Zero;
        }

        try
        {
            return System.Xml.XmlConvert.ToTimeSpan(duration);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;

        return bytes switch
        {
            >= gb => $"{bytes / (double)gb:F2} GB",
            >= mb => $"{bytes / (double)mb:F2} MB",
            >= kb => $"{bytes / (double)kb:F2} KB",
            _ => $"{bytes} B",
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);
    }
}