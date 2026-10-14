namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// The client reads an enum only by the specification's value (<c>rtsp</c>).
/// A server built on generator 2.x wrote the C# member name (<c>Rtsp</c>),
/// so the server and the desktop client are released together.
/// </summary>
public sealed class GatewayServiceEnumWireTests
{
    [Fact]
    public async Task A_Camera_Is_Read_By_The_Values_Of_The_Specification()
    {
        using var body = CameraBody("rtsp", "rotate90", "topLeft", "connected");
        using var server = new GatewayTestServer(HttpStatusCode.OK, body, "application/json");

        var camera = await server.Gateway.GetCameraByIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        camera.Should().NotBeNull();
        camera!.Protocol.Should().Be(global::VideoSurveillance.Generated.Cameras.Models.CameraProtocol.Rtsp);
        camera.Rotation.Should().Be(global::VideoSurveillance.Generated.Cameras.Models.CameraRotation.Rotate90);
        camera.OverlayPosition.Should().Be(global::VideoSurveillance.Generated.Cameras.Models.CameraOverlayPosition.TopLeft);
        camera.ConnectionState.Should().Be(global::VideoSurveillance.Generated.Cameras.Models.CameraConnectionState.Connected);
    }

    [Fact]
    public async Task A_Camera_From_A_Server_Built_On_Generator_V2_Is_Not_Read()
    {
        using var body = CameraBody("Rtsp", "Rotate90", "TopLeft", "Connected");
        using var server = new GatewayTestServer(HttpStatusCode.OK, body, "application/json");

        var read = () => server.Gateway.GetCameraByIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<Atc.Rest.Client.RestClientDeserializationException>();
    }

    private static StringContent CameraBody(
        string protocol,
        string rotation,
        string overlayPosition,
        string connectionState)
        => new(
            $$"""{"id":"{{Guid.NewGuid()}}","displayName":"Gate","protocol":"{{protocol}}","rotation":"{{rotation}}","overlayPosition":"{{overlayPosition}}","connectionState":"{{connectionState}}"}""");
}