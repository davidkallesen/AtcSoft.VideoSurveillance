using ApiCamera = VideoSurveillance.Generated.Cameras.Models.Camera;
using ApiCameraOverlayPosition = VideoSurveillance.Generated.Cameras.Models.CameraOverlayPosition;
using ApiCameraProtocol = VideoSurveillance.Generated.Cameras.Models.CameraProtocol;
using ApiCameraSource = VideoSurveillance.Generated.Cameras.Models.CameraSource;
using ApiCameraStreamRtspTransport = VideoSurveillance.Generated.Cameras.Models.CameraStreamRtspTransport;

using CoreCameraProtocol = Linksoft.VideoSurveillance.Enums.CameraProtocol;
using CoreOverlayPosition = Linksoft.VideoSurveillance.Enums.OverlayPosition;

using WpfCoreModels = Linksoft.VideoSurveillance.Wpf.Core.Models;

namespace Linksoft.VideoSurveillance.Wpf.Extensions;

/// <summary>
/// Extension methods for mapping between API camera models and Wpf.Core CameraConfiguration models.
/// </summary>
public static class CameraModelMappingExtensions
{
    /// <summary>
    /// Converts an API <see cref="ApiCamera"/> to a Wpf.Core <see cref="WpfCoreModels.CameraConfiguration"/>.
    /// </summary>
    public static WpfCoreModels.CameraConfiguration ToCameraConfiguration(
        this ApiCamera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        var config = new WpfCoreModels.CameraConfiguration
        {
            Id = camera.Id,
        };

        config.Connection.IpAddress = camera.IpAddress;
        config.Connection.Port = camera.Port;
        config.Connection.Protocol = camera.Protocol?.ToCoreProtocol() ?? CoreCameraProtocol.Rtsp;

        config.Authentication.UserName = camera.Username ?? string.Empty;

        config.Display.DisplayName = camera.DisplayName;
        config.Display.Description = camera.Description ?? string.Empty;
        config.Display.OverlayPosition = camera.OverlayPosition?.ToCoreOverlayPosition() ?? CoreOverlayPosition.TopLeft;

        config.Stream.UseLowLatencyMode = camera.StreamUseLowLatencyMode;
        config.Stream.MaxLatencyMs = camera.StreamMaxLatencyMs;
        config.Stream.RtspTransport = camera.StreamRtspTransport?.ToString()?.ToLowerInvariant() ?? "tcp";
        config.Stream.BufferDurationMs = camera.StreamBufferDurationMs;

        return config;
    }

    /// <summary>
    /// Converts a Wpf.Core <see cref="WpfCoreModels.CameraConfiguration"/> to an API <see cref="CreateCameraRequest"/>.
    /// </summary>
    public static CreateCameraRequest ToCreateCameraRequest(
        this WpfCoreModels.CameraConfiguration config,
        string? password = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var username = string.IsNullOrEmpty(config.Authentication.UserName)
            ? string.Empty
            : config.Authentication.UserName;

        var pwd = string.IsNullOrEmpty(password)
            ? string.Empty
            : password;

        return new CreateCameraRequest(
            DisplayName: config.Display.DisplayName,
            Description: config.Display.Description ?? string.Empty,
            Source: ApiCameraSource.Network,
            IpAddress: config.Connection.IpAddress,
            Protocol: config.Connection.Protocol.ToApiProtocol(),
            Path: string.Empty,
            Username: username,
            Password: pwd,
            UsbDeviceId: string.Empty,
            UsbFriendlyName: string.Empty,
            UsbWidth: 0,
            UsbHeight: 0,
            UsbFrameRate: 0,
            UsbPixelFormat: string.Empty,
            UsbCaptureAudio: false,
            UsbAudioDeviceName: string.Empty,
            OverlayPosition: config.Display.OverlayPosition.ToApiOverlayPosition(),
            StreamUseLowLatencyMode: config.Stream.UseLowLatencyMode,
            StreamMaxLatencyMs: config.Stream.MaxLatencyMs,
            StreamRtspTransport: config.Stream.RtspTransport.ToApiRtspTransport(),
            StreamBufferDurationMs: config.Stream.BufferDurationMs,
            Port: config.Connection.Port);
    }

    /// <summary>
    /// Converts a Wpf.Core <see cref="WpfCoreModels.CameraConfiguration"/> to an API <see cref="UpdateCameraRequest"/>.
    /// </summary>
    public static UpdateCameraRequest ToUpdateCameraRequest(
        this WpfCoreModels.CameraConfiguration config,
        string? password = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var username = string.IsNullOrEmpty(config.Authentication.UserName)
            ? string.Empty
            : config.Authentication.UserName;

        var pwd = string.IsNullOrEmpty(password)
            ? string.Empty
            : password;

        return new UpdateCameraRequest(
            DisplayName: config.Display.DisplayName,
            Description: config.Display.Description ?? string.Empty,
            Source: ApiCameraSource.Network,
            IpAddress: config.Connection.IpAddress,
            Port: config.Connection.Port,
            Protocol: config.Connection.Protocol.ToApiProtocol(),
            Path: string.Empty,
            Username: username,
            Password: pwd,
            UsbDeviceId: string.Empty,
            UsbFriendlyName: string.Empty,
            UsbWidth: 0,
            UsbHeight: 0,
            UsbFrameRate: 0,
            UsbPixelFormat: string.Empty,
            UsbCaptureAudio: false,
            UsbAudioDeviceName: string.Empty,
            OverlayPosition: config.Display.OverlayPosition.ToApiOverlayPosition(),
            StreamUseLowLatencyMode: config.Stream.UseLowLatencyMode,
            StreamMaxLatencyMs: config.Stream.MaxLatencyMs,
            StreamRtspTransport: config.Stream.RtspTransport.ToApiRtspTransport(),
            StreamBufferDurationMs: config.Stream.BufferDurationMs);
    }

    /// <summary>
    /// Converts an API <see cref="ApiCameraProtocol"/> to a Core <see cref="CoreCameraProtocol"/>.
    /// </summary>
    public static CoreCameraProtocol ToCoreProtocol(
        this ApiCameraProtocol protocol)
        => protocol switch
        {
            ApiCameraProtocol.Rtsp => CoreCameraProtocol.Rtsp,
            ApiCameraProtocol.Http => CoreCameraProtocol.Http,
            ApiCameraProtocol.Https => CoreCameraProtocol.Https,
            _ => CoreCameraProtocol.Rtsp,
        };

    /// <summary>
    /// Converts a Core <see cref="CoreCameraProtocol"/> to an API <see cref="ApiCameraProtocol"/>.
    /// </summary>
    public static ApiCameraProtocol ToApiProtocol(
        this CoreCameraProtocol protocol)
        => protocol switch
        {
            CoreCameraProtocol.Rtsp => ApiCameraProtocol.Rtsp,
            CoreCameraProtocol.Http => ApiCameraProtocol.Http,
            CoreCameraProtocol.Https => ApiCameraProtocol.Https,
            _ => ApiCameraProtocol.Rtsp,
        };

    /// <summary>
    /// Converts an API <see cref="ApiCameraOverlayPosition"/> to a Core <see cref="CoreOverlayPosition"/>.
    /// </summary>
    public static CoreOverlayPosition ToCoreOverlayPosition(
        this ApiCameraOverlayPosition position)
        => position switch
        {
            ApiCameraOverlayPosition.TopLeft => CoreOverlayPosition.TopLeft,
            ApiCameraOverlayPosition.TopRight => CoreOverlayPosition.TopRight,
            ApiCameraOverlayPosition.BottomLeft => CoreOverlayPosition.BottomLeft,
            ApiCameraOverlayPosition.BottomRight => CoreOverlayPosition.BottomRight,
            _ => CoreOverlayPosition.TopLeft,
        };

    /// <summary>
    /// Converts a Core <see cref="CoreOverlayPosition"/> to an API <see cref="ApiCameraOverlayPosition"/>.
    /// </summary>
    public static ApiCameraOverlayPosition ToApiOverlayPosition(
        this CoreOverlayPosition position)
        => position switch
        {
            CoreOverlayPosition.TopLeft => ApiCameraOverlayPosition.TopLeft,
            CoreOverlayPosition.TopRight => ApiCameraOverlayPosition.TopRight,
            CoreOverlayPosition.BottomLeft => ApiCameraOverlayPosition.BottomLeft,
            CoreOverlayPosition.BottomRight => ApiCameraOverlayPosition.BottomRight,
            _ => ApiCameraOverlayPosition.TopLeft,
        };

    /// <summary>
    /// Converts a string RTSP transport value to an API <see cref="ApiCameraStreamRtspTransport"/>.
    /// </summary>
    public static ApiCameraStreamRtspTransport ToApiRtspTransport(
        this string? transport)
        => string.Equals(transport, "udp", StringComparison.OrdinalIgnoreCase)
            ? ApiCameraStreamRtspTransport.Udp
            : ApiCameraStreamRtspTransport.Tcp;
}