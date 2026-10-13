namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// The motion boxes are drawn over the picture, and the picture does not
/// always fill its tile: a 4:3 camera has bars beside it, a camera turned
/// a quarter stands upright in the middle. The boxes have to be laid over
/// the picture as it is shown, not over the tile.
/// </summary>
public sealed class MotionOverlayTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(CameraRotation.None)]
    [InlineData(CameraRotation.Rotate180)]
    public void A_Picture_That_Is_Not_Turned_A_Quarter_Is_Shown_In_The_Shape_The_Camera_Sends(
        CameraRotation? rotation)
        => MotionOverlay.ShownSize(1280, 720, rotation).Should().Be((1280, 720));

    [Theory]
    [InlineData(CameraRotation.Rotate90)]
    [InlineData(CameraRotation.Rotate270)]
    public void A_Picture_Turned_A_Quarter_Stands_Upright(
        CameraRotation rotation)
        => MotionOverlay.ShownSize(1280, 720, rotation).Should().Be((720, 1280));

    [Theory]
    [InlineData(0, 720)]
    [InlineData(1280, 0)]
    [InlineData(-1, -1)]
    public void Until_The_Picture_Has_A_Size_There_Is_No_Shape_To_Lay_The_Boxes_Over(
        int width,
        int height)
        => MotionOverlay.ShownSize(width, height, CameraRotation.None).Should().BeNull();

    [Fact]
    public void The_Boxes_Are_Read_From_What_The_Browser_Reports_About_The_Picture()
    {
        MotionOverlay.ShownSize([640, 480], CameraRotation.Rotate90).Should().Be((480, 640));
        MotionOverlay.ShownSize([640], CameraRotation.None).Should().BeNull();
        MotionOverlay.ShownSize(null, CameraRotation.None).Should().BeNull();
    }
}