namespace Linksoft.VideoSurveillance.Helpers;

/// <summary>
/// Provides default application paths for data storage.
/// </summary>
public static class ApplicationPaths
{
    private static readonly string BaseDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Linksoft",
        "VideoSurveillance");

    /// <summary>
    /// Gets the default path for log files.
    /// </summary>
    public static string DefaultLogsPath { get; } = Path.Combine(BaseDataPath, "logs");

    /// <summary>
    /// Gets the default path for snapshots.
    /// </summary>
    public static string DefaultSnapshotsPath { get; } = Path.Combine(BaseDataPath, "snapshots");

    /// <summary>
    /// Gets the default path for recordings.
    /// </summary>
    public static string DefaultRecordingsPath { get; } = Path.Combine(BaseDataPath, "recordings");

    /// <summary>
    /// Gets the default path for settings file.
    /// </summary>
    public static string DefaultSettingsPath { get; } = Path.Combine(BaseDataPath, "settings.json");

    /// <summary>
    /// Gets the default path for camera data file.
    /// </summary>
    public static string DefaultCameraDataPath { get; } = Path.Combine(BaseDataPath, "cameras.json");
}