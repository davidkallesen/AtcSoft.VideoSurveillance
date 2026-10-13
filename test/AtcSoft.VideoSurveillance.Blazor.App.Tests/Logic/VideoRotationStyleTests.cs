namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

public sealed class VideoRotationStyleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(CameraRotation.None)]
    public void An_Unrotated_Camera_Fills_Its_Tile(CameraRotation? rotation)
    {
        var style = VideoRotationStyle.For(rotation);

        style.Should().Contain("width: 100%").And.Contain("height: 100%");
        style.Should().NotContain("rotate");
    }

    [Fact]
    public void A_Half_Turn_Only_Turns_The_Picture()
    {
        var style = VideoRotationStyle.For(CameraRotation.Rotate180);

        style.Should().Contain("rotate(180deg)");
        style.Should().Contain("width: 100%").And.Contain("height: 100%");
    }

    [Theory]
    [InlineData(CameraRotation.Rotate90, "rotate(90deg)")]
    [InlineData(CameraRotation.Rotate270, "rotate(270deg)")]
    public void A_Quarter_Turn_Swaps_Width_And_Height_So_The_Picture_Still_Fits(
        CameraRotation rotation,
        string expectedTransform)
    {
        var style = VideoRotationStyle.For(rotation);

        // The element is laid out with the tile's height as its width and
        // the other way round, then turned around the tile's centre.
        style.Should().Contain("width: 100cqh").And.Contain("height: 100cqw");
        style.Should().Contain("translate(-50%, -50%)").And.Contain(expectedTransform);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(CameraRotation.None, false)]
    [InlineData(CameraRotation.Rotate90, true)]
    [InlineData(CameraRotation.Rotate180, true)]
    [InlineData(CameraRotation.Rotate270, true)]
    public void IsRotated_Tells_Whether_The_Browser_Controls_Would_Be_Turned_Too(
        CameraRotation? rotation,
        bool expected)
        => VideoRotationStyle.IsRotated(rotation).Should().Be(expected);
}