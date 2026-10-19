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
    public void Two_Boxes_That_Cross_Each_Follow_Their_Own_Track()
    {
        double first = -1;

        var error = WpfTestThread.Run(() =>
        {
            // 100 x 50 analysed, drawn on 200 x 100: twice the size. Half of the old place is kept.
            var overlay = Arranged(new MotionBoundingBoxOverlay { AnalysisWidth = 100, AnalysisHeight = 50, SmoothingFactor = 0.5 });
            overlay.MotionBoxes = [Box(10, trackId: 1), Box(30, trackId: 2)];

            // They cross: track 1 is now where track 2 was a moment ago, and the other way round.
            overlay.MotionBoxes = [Box(28, trackId: 1), Box(12, trackId: 2)];

            first = System.Windows.Controls.Canvas.GetLeft(Rectangles(overlay)[0]);
        });

        error.Should().BeNull();
        first.Should().BeApproximately(2 * 19, 0.001, "track 1 moves from 10 to 28, not from 30, where track 2 was");
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

    private static AtcSoft.VideoSurveillance.Models.BoundingBox Box(
        double x,
        int trackId)
        => new() { X = x, Y = 10, Width = 10, Height = 10, TrackId = trackId };

    private static List<System.Windows.Shapes.Rectangle> Rectangles(
        System.Windows.DependencyObject root)
    {
        var found = new List<System.Windows.Shapes.Rectangle>();
        if (root is System.Windows.Shapes.Rectangle rectangle)
        {
            found.Add(rectangle);
        }

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            found.AddRange(Rectangles(System.Windows.Media.VisualTreeHelper.GetChild(root, i)));
        }

        return found;
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