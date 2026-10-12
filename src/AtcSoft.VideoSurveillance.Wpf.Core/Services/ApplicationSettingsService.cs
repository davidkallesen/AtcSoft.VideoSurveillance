// ReSharper disable RedundantArgumentDefaultValue
namespace AtcSoft.VideoSurveillance.Wpf.Core.Services;

/// <summary>
/// JSON file-based implementation of <see cref="IApplicationSettingsService"/>.
/// Settings are stored at <see cref="ApplicationPaths.DefaultSettingsPath"/>.
/// </summary>
[Registration(Lifetime.Singleton)]
public class ApplicationSettingsService : IApplicationSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string storagePath;
    private ApplicationSettings appSettings = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationSettingsService"/> class.
    /// Uses the default storage location from <see cref="ApplicationPaths.DefaultSettingsPath"/>.
    /// </summary>
    public ApplicationSettingsService()
        : this(ApplicationPaths.DefaultSettingsPath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplicationSettingsService"/> class
    /// with a custom storage path.
    /// </summary>
    /// <param name="storagePath">The path to the storage file.</param>
    public ApplicationSettingsService(string storagePath)
    {
        this.storagePath = storagePath;
        EnsureSettingsFileExists();
        Load();
    }

    /// <inheritdoc/>
    public GeneralSettings General => appSettings.General;

    /// <inheritdoc/>
    public CameraDisplayAppSettings CameraDisplay => appSettings.CameraDisplay;

    /// <inheritdoc/>
    public ConnectionAppSettings Connection => appSettings.Connection;

    /// <inheritdoc/>
    public PerformanceSettings Performance => appSettings.Performance;

    /// <inheritdoc/>
    public MotionDetectionSettings MotionDetection
        => appSettings.MotionDetection;

    /// <inheritdoc/>
    public RecordingSettings Recording => appSettings.Recording;

    /// <inheritdoc/>
    public AdvancedSettings Advanced => appSettings.Advanced;

    /// <inheritdoc/>
    public void SaveGeneral(GeneralSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.appSettings.General = settings;
        Save();
    }

    /// <inheritdoc/>
    public void SaveCameraDisplay(CameraDisplayAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.appSettings.CameraDisplay = settings;
        Save();
    }

    /// <inheritdoc/>
    public void SaveConnection(ConnectionAppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.appSettings.Connection = settings;
        Save();
    }

    /// <inheritdoc/>
    public void SavePerformance(PerformanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.appSettings.Performance = settings;
        Save();
    }

    /// <inheritdoc/>
    public void SaveMotionDetection(MotionDetectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.appSettings.MotionDetection = settings;
        Save();
    }

    /// <inheritdoc/>
    public void SaveRecording(RecordingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.appSettings.Recording = settings;
        Save();
    }

    /// <inheritdoc/>
    public void SaveAdvanced(AdvancedSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        this.appSettings.Advanced = settings;
        Save();
    }

    /// <inheritdoc/>
    public void ApplyDefaultsToCamera(CameraConfiguration camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        // Apply connection defaults
        camera.Connection.Protocol = Connection.DefaultProtocol;
        camera.Connection.Port = Connection.DefaultPort;

        // Apply display defaults
        camera.Display.OverlayPosition = CameraDisplay.OverlayPosition;

        // Apply performance/stream defaults
        camera.Stream.UseLowLatencyMode = Performance.LowLatencyMode;
        camera.Stream.MaxLatencyMs = Performance.MaxLatencyMs;
        camera.Stream.RtspTransport = Performance.RtspTransport;
        camera.Stream.BufferDurationMs = Performance.BufferDurationMs;
    }

    /// <inheritdoc/>
    public T GetEffectiveValue<T>(
        CameraConfiguration camera,
        T appDefault,
        Func<CameraOverrides?, T?> overrideSelector)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(overrideSelector);

        var overrideValue = overrideSelector(camera.Overrides);
        return overrideValue ?? appDefault;
    }

    /// <inheritdoc/>
    public string? GetEffectiveStringValue(
        CameraConfiguration camera,
        string? appDefault,
        Func<CameraOverrides?, string?> overrideSelector)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(overrideSelector);

        var overrideValue = overrideSelector(camera.Overrides);
        return overrideValue ?? appDefault;
    }

    /// <summary>
    /// Points the recording, snapshot and log folders at this application's
    /// own data folder wherever they still hold the shared model's default.
    /// <para>
    /// The settings model is shared with the server and defaults to the
    /// server's folder. A standalone app that kept that default would record
    /// into, and clean up, the server's folder when both run on one PC. A
    /// path equal to the shared default was never chosen by anyone, so it is
    /// safe to replace; any other path is the user's choice and is kept.
    /// </para>
    /// </summary>
    /// <param name="settings">The settings to adjust in place.</param>
    /// <returns><see langword="true"/> when a path was changed.</returns>
    public static bool UseOwnDataFolder(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var recordingPath = OwnUnlessChosen(
            settings.Recording.RecordingPath,
            SharedApplicationPaths.DefaultRecordingsPath,
            ApplicationPaths.DefaultRecordingsPath);

        var snapshotPath = OwnUnlessChosen(
            settings.CameraDisplay.SnapshotPath,
            SharedApplicationPaths.DefaultSnapshotsPath,
            ApplicationPaths.DefaultSnapshotsPath);

        var logPath = OwnUnlessChosen(
            settings.Advanced.LogPath,
            SharedApplicationPaths.DefaultLogsPath,
            ApplicationPaths.DefaultLogsPath);

        var changed =
            !string.Equals(recordingPath, settings.Recording.RecordingPath, StringComparison.Ordinal) ||
            !string.Equals(snapshotPath, settings.CameraDisplay.SnapshotPath, StringComparison.Ordinal) ||
            !string.Equals(logPath, settings.Advanced.LogPath, StringComparison.Ordinal);

        settings.Recording.RecordingPath = recordingPath;
        settings.CameraDisplay.SnapshotPath = snapshotPath;
        settings.Advanced.LogPath = logPath;

        return changed;
    }

    private static string OwnUnlessChosen(
        string? current,
        string sharedDefault,
        string ownDefault)
        => string.IsNullOrWhiteSpace(current) || string.Equals(current, sharedDefault, StringComparison.OrdinalIgnoreCase)
            ? ownDefault
            : current;

    /// <inheritdoc/>
    public void Load()
    {
        appSettings = SafeJsonFile.TryRead<ApplicationSettings>(storagePath, JsonOptions)
                      ?? new ApplicationSettings();

        // Write a moved path back, so the file says where the data really is.
        if (UseOwnDataFolder(appSettings))
        {
            Save();
        }
    }

    /// <inheritdoc/>
    public Task LoadAsync()
    {
        Load();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Save()
    {
        _ = UseOwnDataFolder(appSettings);

        if (!SafeJsonFile.TryWrite(storagePath, appSettings, JsonOptions))
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save application settings to {storagePath}");
        }
    }

    /// <summary>
    /// Ensures the settings file exists. If not, creates it with default values.
    /// </summary>
    private void EnsureSettingsFileExists()
    {
        if (File.Exists(storagePath))
        {
            return;
        }

        var defaults = new ApplicationSettings();
        _ = UseOwnDataFolder(defaults);

        if (SafeJsonFile.TryWrite(storagePath, defaults, JsonOptions))
        {
            System.Diagnostics.Debug.WriteLine($"Created default settings file: {storagePath}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"Failed to create default settings file: {storagePath}");
        }
    }
}