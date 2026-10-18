namespace AtcSoft.VideoSurveillance.Wpf.Core.UserControls;

/// <summary>
/// The overlay draws the boxes it is bound to. The server client's tiles bind them, as
/// CameraWall's tiles call it from code; before, the client's grid drew no boxes at all.
/// </summary>
public sealed class MotionBoundingBoxOverlayTests
{
    [Fact]
    public void Bound_Boxes_Are_Drawn()
    {
        var drawn = -1;

        var error = WpfTestThread.Run(() =>
        {
            var overlay = Arranged(new MotionBoundingBoxOverlay
            {
                AnalysisWidth = 100,
                AnalysisHeight = 50,
            });

            overlay.BoundingBoxes = [new System.Windows.Rect(10, 10, 20, 20), new System.Windows.Rect(50, 20, 10, 10)];

            drawn = VisibleRectangles(overlay);
        });

        error.Should().BeNull();
        drawn.Should().Be(2);
    }

    [Fact]
    public void No_Boxes_Hides_Them()
    {
        var drawn = -1;

        var error = WpfTestThread.Run(() =>
        {
            var overlay = Arranged(new MotionBoundingBoxOverlay { AnalysisWidth = 100, AnalysisHeight = 50 });
            overlay.BoundingBoxes = [new System.Windows.Rect(10, 10, 20, 20)];

            overlay.BoundingBoxes = [];

            drawn = VisibleRectangles(overlay);
        });

        error.Should().BeNull();
        drawn.Should().Be(0);
    }

    private static MotionBoundingBoxOverlay Arranged(
        MotionBoundingBoxOverlay overlay)
    {
        overlay.Measure(new System.Windows.Size(200, 100));
        overlay.Arrange(new System.Windows.Rect(0, 0, 200, 100));
        overlay.UpdateLayout();
        return overlay;
    }

    private static int VisibleRectangles(System.Windows.DependencyObject root)
    {
        var count = root is System.Windows.Shapes.Rectangle { Visibility: System.Windows.Visibility.Visible } ? 1 : 0;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            count += VisibleRectangles(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
        }

        return count;
    }
}