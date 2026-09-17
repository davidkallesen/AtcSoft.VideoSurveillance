namespace Linksoft.VideoSurveillance.Api.Services;

/// <summary>
/// JSON file-based implementation of <see cref="ICameraStorageService"/> for the server edition.
/// </summary>
public sealed partial class JsonCameraStorageService : ICameraStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string storagePath;
    private readonly ILogger<JsonCameraStorageService> logger;

    // All access to data.Cameras / data.Layouts goes through this lock.
    // Without it, CameraConnectionService / handlers iterating GetAllCameras()
    // would throw InvalidOperationException if a REST UpdateCamera mutated
    // the list mid-iteration. Reads return snapshots so callers can iterate
    // outside the lock without blocking writers.
    private readonly Lock dataLock = new();
    private CameraStorageData data = new();

    public JsonCameraStorageService(ILogger<JsonCameraStorageService> logger)
        : this(ApplicationPaths.DefaultCameraDataPath, logger)
    {
    }

    public JsonCameraStorageService(
        string storagePath,
        ILogger<JsonCameraStorageService> logger)
    {
        this.storagePath = storagePath;
        this.logger = logger;
        Load();
    }

    /// <inheritdoc/>
    public Guid? StartupLayoutId
    {
        get
        {
            lock (dataLock)
            {
                return data.StartupLayoutId;
            }
        }

        set
        {
            lock (dataLock)
            {
                data.StartupLayoutId = value;
                SaveLocked();
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<CameraConfiguration> GetAllCameras()
    {
        lock (dataLock)
        {
            return data.Cameras.ToList();
        }
    }

    /// <inheritdoc/>
    public CameraConfiguration? GetCameraById(Guid id)
    {
        lock (dataLock)
        {
            return data.Cameras.Find(c => c.Id == id);
        }
    }

    /// <inheritdoc/>
    public void AddOrUpdateCamera(CameraConfiguration camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        lock (dataLock)
        {
            var existingIndex = data.Cameras.FindIndex(c => c.Id == camera.Id);
            if (existingIndex >= 0)
            {
                data.Cameras[existingIndex] = camera;
            }
            else
            {
                data.Cameras.Add(camera);
            }

            SaveLocked();
        }
    }

    /// <inheritdoc/>
    public bool DeleteCamera(Guid id)
    {
        lock (dataLock)
        {
            var camera = data.Cameras.Find(c => c.Id == id);
            if (camera is null)
            {
                return false;
            }

            data.Cameras.Remove(camera);

            foreach (var layout in data.Layouts)
            {
                layout.Items.RemoveAll(item => item.CameraId == id);
            }

            SaveLocked();
            return true;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<CameraLayout> GetAllLayouts()
    {
        lock (dataLock)
        {
            return data.Layouts.ToList();
        }
    }

    /// <inheritdoc/>
    public CameraLayout? GetLayoutById(Guid id)
    {
        lock (dataLock)
        {
            return data.Layouts.Find(l => l.Id == id);
        }
    }

    /// <inheritdoc/>
    public void AddOrUpdateLayout(CameraLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        lock (dataLock)
        {
            var existingIndex = data.Layouts.FindIndex(l => l.Id == layout.Id);
            if (existingIndex >= 0)
            {
                layout.ModifiedAt = DateTime.UtcNow;
                data.Layouts[existingIndex] = layout;
            }
            else
            {
                data.Layouts.Add(layout);
            }

            SaveLocked();
        }
    }

    /// <inheritdoc/>
    public bool DeleteLayout(Guid id)
    {
        lock (dataLock)
        {
            var layout = data.Layouts.Find(l => l.Id == id);
            if (layout is null)
            {
                return false;
            }

            data.Layouts.Remove(layout);

            if (data.StartupLayoutId == id)
            {
                data.StartupLayoutId = null;
            }

            SaveLocked();
            return true;
        }
    }

    /// <inheritdoc/>
    public void Save()
    {
        lock (dataLock)
        {
            SaveLocked();
        }
    }

    /// <inheritdoc/>
    public void Load()
    {
        var loaded = SafeJsonFile.TryRead<CameraStorageData>(storagePath, JsonOptions);
        lock (dataLock)
        {
            if (loaded is not null)
            {
                data = loaded;
                LogStorageLoaded(data.Cameras.Count, data.Layouts.Count, storagePath);
            }
            else
            {
                data = new CameraStorageData();
            }
        }
    }

    // Caller must already hold dataLock. Extracted so the public mutators
    // don't have to acquire-release-reacquire when they need to persist.
    private void SaveLocked()
    {
        if (!SafeJsonFile.TryWrite(storagePath, data, JsonOptions))
        {
            LogStorageSaveFailed(new IOException("Atomic save failed"), storagePath);
        }
    }
}