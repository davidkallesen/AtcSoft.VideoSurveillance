// ReSharper disable StringLiteralTypo
namespace Linksoft.VideoEngine;

/// <summary>
/// Static entry point for initializing the video engine.
/// Must be called once before creating any <see cref="IVideoPlayer"/> instances.
/// </summary>
public static class VideoEngineBootstrap
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool isInitialized;
    private static VideoEngineConfig? currentConfig;

    /// <summary>
    /// Gets a value indicating whether the engine has been initialized.
    /// </summary>
    public static bool IsInitialized => isInitialized;

    /// <summary>
    /// Gets the FFmpeg version string after initialization, or <c>null</c> if not yet initialized.
    /// </summary>
    public static string? FFmpegVersion { get; private set; }

    /// <summary>
    /// Gets the current engine configuration, or <c>null</c> if not yet initialized.
    /// </summary>
    public static VideoEngineConfig? Config => currentConfig;

    /// <summary>
    /// Initializes the video engine with default configuration.
    /// </summary>
    public static void Initialize()
    {
        Initialize(new VideoEngineConfig());
    }

    /// <summary>
    /// Initializes the video engine with the specified configuration.
    /// This method is idempotent; subsequent calls are ignored.
    /// </summary>
    /// <param name="config">The engine configuration.</param>
    public static void Initialize(VideoEngineConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var acquired = false;
        try
        {
            InitLock.Wait();
            acquired = true;

            if (isInitialized)
            {
                return;
            }

            var ffmpegPath = config.FFmpegPath ?? DiscoverFFmpegPath();
            FFmpegVersion = FFmpegLoader.Initialize(ffmpegPath, config.FFmpegLogLevel);

            currentConfig = config;
            isInitialized = true;
        }
        finally
        {
            if (acquired)
            {
                InitLock.Release();
            }
        }
    }

    /// <summary>
    /// Discovers the FFmpeg path by checking common locations.
    /// </summary>
    private static string? DiscoverFFmpegPath()
    {
        string[] possiblePaths =
        [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ffmpeg",
                "bin"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ffmpeg",
                "bin"),
            @"C:\ffmpeg\bin",
            @"C:\Program Files\ffmpeg\bin",
        ];

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path) && ContainsFFmpegBinaries(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool ContainsFFmpegBinaries(string path)
    {
        try
        {
            return Directory.GetFiles(path, "avcodec*.dll").Length > 0
                || Directory.GetFiles(path, "libavcodec*.so").Length > 0
                || Directory.GetFiles(path, "libavcodec*.dylib").Length > 0;
        }
        catch
        {
            return false;
        }
    }
}