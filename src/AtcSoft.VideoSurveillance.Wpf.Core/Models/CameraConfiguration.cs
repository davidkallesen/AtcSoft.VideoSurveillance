using CoreModels = Linksoft.VideoSurveillance.Models;

namespace Linksoft.VideoSurveillance.Wpf.Core.Models;

/// <summary>
/// Wraps <see cref="CoreModels.CameraConfiguration"/> with change notification for WPF binding.
/// </summary>
public partial class CameraConfiguration : ObservableObject
{
    internal CoreModels.CameraConfiguration Core { get; }

    private ConnectionSettings connection;
    private AuthenticationSettings authentication;
    private CameraDisplaySettings display;
    private StreamSettings stream;

    [JsonIgnore]
    [ObservableProperty]
    private bool canSwapLeft;

    [JsonIgnore]
    [ObservableProperty]
    private bool canSwapRight;

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraConfiguration"/> class.
    /// </summary>
    public CameraConfiguration()
        : this(new CoreModels.CameraConfiguration())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CameraConfiguration"/> class
    /// wrapping the specified Core instance.
    /// </summary>
    internal CameraConfiguration(CoreModels.CameraConfiguration core)
    {
        Core = core ?? throw new ArgumentNullException(nameof(core));

        connection = new ConnectionSettings(Core.Connection);
        authentication = new AuthenticationSettings(Core.Authentication);
        display = new CameraDisplaySettings(Core.Display);
        stream = new StreamSettings(Core.Stream);

        SubscribeToNestedChanges(connection);
        SubscribeToNestedChanges(authentication);
        SubscribeToNestedChanges(display);
        SubscribeToNestedChanges(stream);
    }

    /// <summary>
    /// Gets or sets the unique identifier for the camera.
    /// </summary>
    public Guid Id
    {
        get => Core.Id;
        set => Core.Id = value;
    }

    /// <summary>
    /// Gets or sets the connection settings.
    /// </summary>
    public ConnectionSettings Connection
    {
        get => connection;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(connection, value))
            {
                return;
            }

            connection = value;
            Core.Connection = value.Core;
            OnPropertyChanged();
            SubscribeToNestedChanges(connection);
        }
    }

    /// <summary>
    /// Gets or sets the authentication settings.
    /// </summary>
    public AuthenticationSettings Authentication
    {
        get => authentication;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(authentication, value))
            {
                return;
            }

            authentication = value;
            Core.Authentication = value.Core;
            OnPropertyChanged();
            SubscribeToNestedChanges(authentication);
        }
    }

    /// <summary>
    /// Gets or sets the display settings.
    /// </summary>
    public CameraDisplaySettings Display
    {
        get => display;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(display, value))
            {
                return;
            }

            display = value;
            Core.Display = value.Core;
            OnPropertyChanged();
            SubscribeToNestedChanges(display);
        }
    }

    /// <summary>
    /// Gets or sets the streaming settings.
    /// </summary>
    public StreamSettings Stream
    {
        get => stream;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (ReferenceEquals(stream, value))
            {
                return;
            }

            stream = value;
            Core.Stream = value.Core;
            OnPropertyChanged();
            SubscribeToNestedChanges(stream);
        }
    }

    /// <summary>
    /// Gets or sets the per-camera overrides.
    /// </summary>
    public CameraOverrides Overrides
    {
        get => Core.Overrides;
        set
        {
            if (ReferenceEquals(Core.Overrides, value))
            {
                return;
            }

            Core.Overrides = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Builds the camera stream URI based on the configuration.
    /// Throws on USB cameras — use
    /// <see cref="BuildSourceLocator"/> instead.
    /// </summary>
    /// <returns>The constructed URI for the camera stream.</returns>
    public Uri BuildUri()
        => Core.BuildUri();

    /// <summary>
    /// Builds an FFmpeg-ready locator that handles both network and
    /// USB cameras. Prefer this over <see cref="BuildUri"/> in new
    /// code.
    /// </summary>
    public Linksoft.VideoSurveillance.Helpers.SourceLocator BuildSourceLocator()
        => Linksoft.VideoSurveillance.Helpers.CameraUriHelper.BuildSourceLocator(Core);

    /// <summary>
    /// Returns the display name of the camera.
    /// </summary>
    /// <returns>The display name.</returns>
    public override string ToString()
        => Display.DisplayName;

    /// <summary>
    /// Creates a deep copy of this camera configuration.
    /// </summary>
    /// <returns>A new instance with the same values.</returns>
    public CameraConfiguration Clone()
    {
        var clone = new CameraConfiguration(Core.Clone())
        {
            CanSwapLeft = CanSwapLeft,
            CanSwapRight = CanSwapRight,
        };
        return clone;
    }

    /// <summary>
    /// Copies values from another camera configuration.
    /// </summary>
    /// <param name="source">The source configuration to copy from.</param>
    public void CopyFrom(CameraConfiguration source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Note: Id is intentionally not copied - we're copying values, not identity
        Connection.CopyFrom(source.Connection);
        Authentication.CopyFrom(source.Authentication);
        Display.CopyFrom(source.Display);
        Stream.CopyFrom(source.Stream);

        // Replace Overrides object to trigger PropertyChanged notification
        // (CameraOverrides doesn't implement INotifyPropertyChanged)
        Overrides = source.Overrides.Clone();
    }

    /// <summary>
    /// Determines whether the specified instance has the same values (excluding Id and swap flags).
    /// </summary>
    public bool ValueEquals(CameraConfiguration? other)
    {
        if (other is null)
        {
            return false;
        }

        return Core.ValueEquals(other.Core);
    }

    private void SubscribeToNestedChanges(INotifyPropertyChanged? nested)
    {
        if (nested is not null)
        {
            nested.PropertyChanged += OnNestedPropertyChanged;
        }
    }

    private void OnNestedPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        // Forward the change notification for the parent property
        // WPF bindings to nested paths (e.g., Camera.Connection.IpAddress) work automatically
        // because WPF subscribes to each object's PropertyChanged in the path
        var parentPropertyName = sender switch
        {
            ConnectionSettings => nameof(Connection),
            AuthenticationSettings => nameof(Authentication),
            CameraDisplaySettings => nameof(Display),
            StreamSettings => nameof(Stream),
            _ => null,
        };

        if (parentPropertyName is not null)
        {
            OnPropertyChanged(parentPropertyName);
        }
    }
}