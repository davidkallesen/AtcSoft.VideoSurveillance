namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// A server built on generator 2.x wrote an enum as its C# member name (<c>Rtsp</c>),
/// where the specification says <c>rtsp</c>. Until every server is on v3,
/// the client reads both (<c>readEnumMemberNames</c> in its marker).
/// </summary>
public sealed class GatewayServiceEnumWireTests
{
    [Theory]
    [InlineData("rtsp", "rotate90", "topLeft", "connected")]
    [InlineData("Rtsp", "Rotate90", "TopLeft", "Connected")]
    public async Task A_Camera_Is_Read_By_Its_Values_And_By_The_Member_Names_A_V2_Server_Wrote(
        string protocol,
        string rotation,
        string overlayPosition,
        string connectionState)
    {
        using var body = new StringContent(
            $$"""{"id":"{{Guid.NewGuid()}}","displayName":"Gate","protocol":"{{protocol}}","rotation":"{{rotation}}","overlayPosition":"{{overlayPosition}}","connectionState":"{{connectionState}}"}""");
        using var server = new GatewayTestServer(HttpStatusCode.OK, body, "application/json");

        var camera = await server.Gateway.GetCameraByIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        camera.Should().NotBeNull();
        camera!.Protocol.Should().Be(global::VideoSurveillance.Generated.Cameras.Models.CameraProtocol.Rtsp);
        camera.Rotation.Should().Be(global::VideoSurveillance.Generated.Cameras.Models.CameraRotation.Rotate90);
        camera.OverlayPosition.Should().Be(global::VideoSurveillance.Generated.Cameras.Models.CameraOverlayPosition.TopLeft);
        camera.ConnectionState.Should().Be(global::VideoSurveillance.Generated.Cameras.Models.CameraConnectionState.Connected);
    }
}