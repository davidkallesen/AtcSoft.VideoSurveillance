namespace Linksoft.VideoSurveillance.Wpf.Core;

/// <summary>
/// Provides default application paths for data storage.
/// The base folder can be configured per application (e.g. "CameraWall" or "VideoSurveillance").
/// </summary>
public static class ApplicationPaths
{
    private static string baseDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Linksoft",
        "CameraWall");

    /// <summary>
    /// Gets the default path for log files.
    /// </summary>
    public static string DefaultLogsPath
        => Path.Combine(baseDataPath, "logs");

    /// <summary>
    /// Gets the default path for snapshots.
    /// </summary>
    public static string DefaultSnapshotsPath
        => Path.Combine(baseDataPath, "snapshots");

    /// <summary>
    /// Gets the default path for recordings.
    /// </summary>
    public static string DefaultRecordingsPath
        => Path.Combine(baseDataPath, "recordings");

    /// <summary>
    /// Gets the default path for settings file.
    /// </summary>
    public static string DefaultSettingsPath
        => Path.Combine(baseDataPath, "settings.json");

    /// <summary>
    /// Gets the default path for camera data file.
    /// </summary>
    public static string DefaultCameraDataPath
        => Path.Combine(baseDataPath, "cameras.json");

    /// <summary>
    /// Configures the application folder name under Linksoft in ProgramData.
    /// Must be called before any path properties are accessed.
    /// </summary>
    /// <param name="applicationName">The application folder name (e.g. "CameraWall" or "VideoSurveillance").</param>
    public static void Configure(string applicationName)
    {
        baseDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Linksoft",
            applicationName);
    }
}