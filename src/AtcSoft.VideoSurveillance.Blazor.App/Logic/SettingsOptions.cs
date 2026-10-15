namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// The choices the settings page offers. They are the same choices, in the
/// same order, as the desktop apps' settings dialog, so a value picked in
/// one client is one the others can show.
/// </summary>
public static class SettingsOptions
{
    public static IReadOnlyList<(string Value, string Label)> VideoQualities { get; } =
    [
        ("Auto", "Auto (source quality)"),
        ("1080p", "1080p"),
        ("720p", "720p"),
        ("480p", "480p"),
        ("360p", "360p"),
    ];

    public static IReadOnlyList<(double Value, string Label)> OverlayOpacities { get; } =
        [.. Enumerable.Range(0, 11).Select(i => (i / 10.0, $"{i * 10}%"))];

    public static IReadOnlyList<(int Value, string Label)> MotionSensitivities { get; } =
    [
        (10, "Low"),
        (30, "Medium"),
        (50, "High"),
        (70, "Very high"),
    ];

    public static IReadOnlyList<(string Value, string Label)> AnalysisResolutions { get; } =
    [
        ("320x240", "320 x 240 (low CPU)"),
        ("480x360", "480 x 360 (balanced)"),
        ("640x480", "640 x 480 (better detection)"),
        ("800x600", "800 x 600 (high CPU)"),
        ("960x720", "960 x 720 (very high CPU)"),
    ];

    public static IReadOnlyList<(int Value, string Label)> PostMotionDurations { get; } =
    [
        (5, "5 seconds"),
        (10, "10 seconds"),
        (15, "15 seconds"),
        (30, "30 seconds"),
        (60, "1 minute"),
    ];

    public static IReadOnlyList<(int Value, string Label)> BoundingBoxThicknesses { get; } =
        [.. Enumerable.Range(1, 5).Select(i => (i, $"{i} px"))];

    public static IReadOnlyList<(int Value, string Label)> BoundingBoxMinAreas { get; } =
    [
        (10, "Tiny"),
        (25, "Small"),
        (50, "Medium"),
        (100, "Large"),
        (200, "Very large"),
    ];

    public static IReadOnlyList<(int Value, string Label)> ThumbnailTiles { get; } =
    [
        (1, "Single picture (320 x 240)"),
        (4, "2 x 2 grid (640 x 480)"),
    ];

    public static IReadOnlyList<(int Value, string Label)> MaxRecordingDurations { get; } =
    [
        (15, "15 minutes"),
        (30, "30 minutes"),
        (60, "1 hour"),
        (120, "2 hours"),
        (180, "3 hours"),
        (240, "4 hours"),
    ];

    public static IReadOnlyList<(int Value, string Label)> RetentionPeriods { get; } =
    [
        (7, "7 days"),
        (14, "14 days"),
        (30, "30 days"),
        (60, "60 days"),
        (90, "90 days"),
        (180, "6 months"),
        (365, "1 year"),
    ];

    public static IReadOnlyList<(int Value, string Label)> MinFreeSpaces { get; } =
    [
        (512, "500 MB"),
        (1024, "1 GB"),
        (2048, "2 GB (recommended)"),
        (5120, "5 GB"),
        (10240, "10 GB"),
        (20480, "20 GB"),
    ];

    public static IReadOnlyList<(string Value, string Label)> TimelapseIntervals { get; } =
    [
        ("10s", "10 seconds"),
        ("30s", "30 seconds"),
        ("1m", "1 minute"),
        ("5m", "5 minutes"),
        ("10m", "10 minutes"),
        ("30m", "30 minutes"),
        ("1h", "1 hour"),
        ("3h", "3 hours"),
        ("6h", "6 hours"),
        ("12h", "12 hours"),
        ("24h", "24 hours"),
    ];

    /// <summary>
    /// Returns the choices, with the current value added when it is not one
    /// of them. A value set by another client, or by hand in the settings
    /// file, is then shown as it is instead of as an empty box.
    /// </summary>
    /// <typeparam name="T">The type of the setting's value.</typeparam>
    public static IReadOnlyList<(T Value, string Label)> WithCurrent<T>(
        IReadOnlyList<(T Value, string Label)> options,
        T current,
        Func<T, string> describe)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(describe);

        if (current is null || options.Any(o => EqualityComparer<T>.Default.Equals(o.Value, current)))
        {
            return options;
        }

        return [.. options, (current, describe(current))];
    }
}