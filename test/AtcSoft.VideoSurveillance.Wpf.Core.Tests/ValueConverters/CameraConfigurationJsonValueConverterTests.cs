namespace Linksoft.VideoSurveillance.Wpf.Core.ValueConverters;

public class CameraConfigurationJsonValueConverterTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(),
            new CameraConfigurationJsonValueConverter(),
        },
    };

    [Fact]
    public void Deserialize_NestedOverrides_RecordingOnly_EnableRecordingOnConnect()
    {
        // Arrange - this is the exact shape from cameras.json that was failing
        const string json = """
        {
            "id": "8cb3d71d-8f6b-4f06-ac2e-a3f2cd8a2566",
            "connection": { "ipAddress": "192.168.1.43", "protocol": "Rtsp", "port": 554, "path": "stream1" },
            "authentication": { "userName": "user", "password": "pass" },
            "display": { "displayName": "TAPO-C210-03" },
            "stream": {},
            "overrides": {
                "recording": {
                    "enableRecordingOnConnect": true
                }
            }
        }
        """;

        // Act
        var camera = JsonSerializer.Deserialize<CameraConfiguration>(json, JsonOptions);

        // Assert
        camera.Should().NotBeNull();
        camera!.Overrides.Should().NotBeNull();
        camera.Overrides!.Recording.EnableRecordingOnConnect.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_NestedOverrides_RecordingOnly_EnableRecordingOnMotion()
    {
        // Arrange
        const string json = """
        {
            "id": "00000000-0000-0000-0000-000000000001",
            "connection": { "ipAddress": "10.0.0.1", "protocol": "Rtsp", "port": 554 },
            "authentication": { "userName": "u", "password": "p" },
            "display": { "displayName": "Test" },
            "stream": {},
            "overrides": {
                "recording": {
                    "enableRecordingOnMotion": true
                }
            }
        }
        """;

        // Act
        var camera = JsonSerializer.Deserialize<CameraConfiguration>(json, JsonOptions);

        // Assert
        camera.Should().NotBeNull();
        camera!.Overrides!.Recording.EnableRecordingOnMotion.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_NestedOverrides_ConnectionOnly()
    {
        // Arrange
        const string json = """
        {
            "id": "00000000-0000-0000-0000-000000000002",
            "connection": { "ipAddress": "10.0.0.2", "protocol": "Rtsp", "port": 554 },
            "authentication": { "userName": "u", "password": "p" },
            "display": { "displayName": "Test" },
            "stream": {},
            "overrides": {
                "connection": {
                    "connectionTimeoutSeconds": 30
                }
            }
        }
        """;

        // Act
        var camera = JsonSerializer.Deserialize<CameraConfiguration>(json, JsonOptions);

        // Assert
        camera.Should().NotBeNull();
        camera!.Overrides!.Connection.ConnectionTimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public void Deserialize_NestedOverrides_MotionDetectionOnly()
    {
        // Arrange
        const string json = """
        {
            "id": "00000000-0000-0000-0000-000000000003",
            "connection": { "ipAddress": "10.0.0.3", "protocol": "Rtsp", "port": 554 },
            "authentication": { "userName": "u", "password": "p" },
            "display": { "displayName": "Test" },
            "stream": {},
            "overrides": {
                "motionDetection": {
                    "sensitivity": 50
                }
            }
        }
        """;

        // Act
        var camera = JsonSerializer.Deserialize<CameraConfiguration>(json, JsonOptions);

        // Assert
        camera.Should().NotBeNull();
        camera!.Overrides!.MotionDetection.Sensitivity.Should().Be(50);
    }

    [Fact]
    public void Deserialize_NestedOverrides_PerformanceOnly()
    {
        // Arrange
        const string json = """
        {
            "id": "00000000-0000-0000-0000-000000000004",
            "connection": { "ipAddress": "10.0.0.4", "protocol": "Rtsp", "port": 554 },
            "authentication": { "userName": "u", "password": "p" },
            "display": { "displayName": "Test" },
            "stream": {},
            "overrides": {
                "performance": {
                    "hardwareAcceleration": false
                }
            }
        }
        """;

        // Act
        var camera = JsonSerializer.Deserialize<CameraConfiguration>(json, JsonOptions);

        // Assert
        camera.Should().NotBeNull();
        camera!.Overrides!.Performance.HardwareAcceleration.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_NestedOverrides_MultipleSections()
    {
        // Arrange
        const string json = """
        {
            "id": "00000000-0000-0000-0000-000000000005",
            "connection": { "ipAddress": "10.0.0.5", "protocol": "Rtsp", "port": 554 },
            "authentication": { "userName": "u", "password": "p" },
            "display": { "displayName": "Test" },
            "stream": {},
            "overrides": {
                "recording": {
                    "enableRecordingOnConnect": true
                },
                "connection": {
                    "autoReconnectOnFailure": false
                }
            }
        }
        """;

        // Act
        var camera = JsonSerializer.Deserialize<CameraConfiguration>(json, JsonOptions);

        // Assert
        camera.Should().NotBeNull();
        camera!.Overrides!.Recording.EnableRecordingOnConnect.Should().BeTrue();
        camera.Overrides!.Connection.AutoReconnectOnFailure.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_NoOverrides_ReturnsDefaults()
    {
        // Arrange
        const string json = """
        {
            "id": "00000000-0000-0000-0000-000000000006",
            "connection": { "ipAddress": "10.0.0.6", "protocol": "Rtsp", "port": 554 },
            "authentication": { "userName": "u", "password": "p" },
            "display": { "displayName": "Test" },
            "stream": {}
        }
        """;

        // Act
        var camera = JsonSerializer.Deserialize<CameraConfiguration>(json, JsonOptions);

        // Assert
        camera.Should().NotBeNull();
        camera!.Overrides.Should().NotBeNull();
        camera.Overrides!.HasAnyOverride().Should().BeFalse();
        camera.Overrides.Recording.EnableRecordingOnConnect.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_NestedOverrides_PreservesValues()
    {
        // Arrange
        var original = new CameraConfiguration();
        original.Connection.IpAddress = "192.168.1.100";
        original.Connection.Port = 554;
        original.Display.DisplayName = "RoundTrip Test";
        original.Overrides!.Recording.EnableRecordingOnConnect = true;
        original.Overrides.Recording.RecordingFormat = "mkv";

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<CameraConfiguration>(json, JsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Display.DisplayName.Should().Be("RoundTrip Test");
        deserialized.Overrides!.Recording.EnableRecordingOnConnect.Should().BeTrue();
        deserialized.Overrides.Recording.RecordingFormat.Should().Be("mkv");
    }
}