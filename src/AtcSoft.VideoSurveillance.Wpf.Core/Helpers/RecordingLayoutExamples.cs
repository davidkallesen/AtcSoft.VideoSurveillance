namespace AtcSoft.VideoSurveillance.Wpf.Core.Helpers;

/// <summary>
/// Texts that show the user what the two recording folder layout switches
/// do: the path a recording and its thumbnail get, for the current choice
/// and for each switch on and off.
/// </summary>
public static class RecordingLayoutExamples
{
    private const string ExampleCameraName = "Camera 1";

    /// <summary>
    /// Gets where a recording goes, relative to the recording folder.
    /// </summary>
    public static string Video(
        bool useDailySubfolders,
        bool separateThumbnailFolder,
        string? format)
        => Build(useDailySubfolders, separateThumbnailFolder, format).Video;

    /// <summary>
    /// Gets where a thumbnail goes, relative to the recording folder.
    /// </summary>
    public static string Thumbnail(
        bool useDailySubfolders,
        bool separateThumbnailFolder,
        string? format)
        => Build(useDailySubfolders, separateThumbnailFolder, format).Thumbnail;

    /// <summary>
    /// Gets the hover help for the "folder per day" switch: the recording
    /// path with the switch on and with it off.
    /// </summary>
    public static string DailySubfoldersHint(
        bool separateThumbnailFolder,
        string? format)
        => string.Format(
            CultureInfo.CurrentCulture,
            Translations.RecordingLayoutOnOffHint,
            Video(useDailySubfolders: true, separateThumbnailFolder, format),
            Video(useDailySubfolders: false, separateThumbnailFolder, format));

    /// <summary>
    /// Gets the hover help for the "separate thumbnail folder" switch: the
    /// thumbnail path with the switch on and with it off.
    /// </summary>
    public static string SeparateThumbnailFolderHint(
        bool useDailySubfolders,
        string? format)
        => string.Format(
            CultureInfo.CurrentCulture,
            Translations.RecordingLayoutOnOffHint,
            Thumbnail(useDailySubfolders, separateThumbnailFolder: true, format),
            Thumbnail(useDailySubfolders, separateThumbnailFolder: false, format));

    /// <summary>
    /// Gets the always-visible example for the current choice, with the note
    /// that existing recordings follow the setting.
    /// </summary>
    public static string Summary(
        bool useDailySubfolders,
        bool separateThumbnailFolder,
        string? format)
    {
        var (video, thumbnail) = Build(useDailySubfolders, separateThumbnailFolder, format);

        return string.Format(
            CultureInfo.CurrentCulture,
            Translations.RecordingLayoutExampleSummary,
            video,
            thumbnail);
    }

    private static (string Video, string Thumbnail) Build(
        bool useDailySubfolders,
        bool separateThumbnailFolder,
        string? format)
        => RecordingPathHelper.BuildExample(
            ExampleCameraName,
            DateTime.Now,
            format,
            new AtcSoft.VideoSurveillance.Helpers.RecordingLayoutOptions(useDailySubfolders, separateThumbnailFolder));
}