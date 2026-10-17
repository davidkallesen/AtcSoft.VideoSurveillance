namespace AtcSoft.VideoSurveillance.E2E.Infrastructure;

/// <summary>
/// Cameras without cameras: an HTTP camera is a live-paced FFmpeg test pattern on a local
/// port (when the FFmpeg command-line tool is installed), and a dead camera is a port
/// nothing listens on, so the failure states get opened too.
/// </summary>
internal static class TestCameras
{
    public const string LiveCameraName = "E2E Live";
    public const string DeadCameraName = "E2E Offline";
    public const string StreamPath = "live";

    /// <summary>
    /// Gets the FFmpeg command-line tool, or <see langword="null"/> when it is not installed.
    /// </summary>
    public static string? FFmpegTool { get; } = FindFFmpegTool();

    /// <summary>
    /// Gets a port nothing listens on (taken and released again).
    /// </summary>
    public static int DeadPort { get; } = E2ESettings.GetFreePort();

    /// <summary>
    /// Starts a live test camera on the port, or returns <see langword="null"/> without FFmpeg.
    /// </summary>
    public static LiveTestCamera? StartLiveCamera(
        int port,
        string outputPath)
        => FFmpegTool is null
            ? null
            : new LiveTestCamera(FFmpegTool, port, outputPath);

    private static string? FindFFmpegTool()
    {
        var folder = AtcSoft.VideoEngine.VideoEngineBootstrap.FindFFmpegPath();
        if (folder is null)
        {
            return null;
        }

        var tool = Path.Combine(folder, "ffmpeg.exe");
        return File.Exists(tool) ? tool : null;
    }
}