namespace Linksoft.VideoSurveillance.Wpf.Core.Models;

/// <summary>
/// Represents a recorded video file entry.
/// </summary>
public class RecordingEntry
{
    /// <summary>
    /// Gets the full file path of the recording.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets the name of the camera that made the recording.
    /// </summary>
    public required string CameraName { get; init; }

    /// <summary>
    /// Gets the file name without path.
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// Gets the date and time when the recording was made.
    /// </summary>
    public DateTime RecordingTime { get; init; }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Gets or sets the duration of the recording.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets the formatted file size string.
    /// </summary>
    public string FormattedFileSize => FormatFileSize(FileSizeBytes);

    /// <summary>
    /// Gets the formatted duration string.
    /// </summary>
    public string FormattedDuration => FormatDuration(Duration);

    /// <summary>
    /// Gets the formatted recording time string.
    /// </summary>
    public string FormattedRecordingTime
        => RecordingTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture);

    /// <summary>
    /// Gets the thumbnail file path (same as video path but with .png extension).
    /// </summary>
    public string ThumbnailPath => Path.ChangeExtension(FilePath, ".png");

    /// <summary>
    /// Gets a value indicating whether a thumbnail file exists for this recording.
    /// </summary>
    public bool HasThumbnail => File.Exists(ThumbnailPath);

    /// <summary>
    /// Gets the thumbnail image, or null if the thumbnail doesn't exist.
    /// This property safely loads the image and handles missing files gracefully.
    /// </summary>
    public BitmapImage? ThumbnailImage
    {
        get
        {
            var path = ThumbnailPath;
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
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