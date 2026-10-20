namespace AtcSoft.VideoSurveillance.Wpf.Core.UserControls;

/// <summary>
/// The timeline under a recording, drawn to a bitmap: an orange band where something went
/// somewhere, nothing orange where nothing did, and the player's line where it is.
/// </summary>
public sealed class ActivityTimelineTests
{
    private const int Width = 100;
    private const int Height = 20;

    [Fact]
    public void An_Event_Is_An_Orange_Band_At_Its_Place_And_The_Player_Is_A_White_Line()
    {
        byte[] pixels = [];

        var error = WpfTestThread.Run(() =>
        {
            var timeline = new ActivityTimeline
            {
                Buckets = [.. Enumerable.Range(0, 10).Select(i => new ActivityBucket(i == 5 ? 30 : 1, i == 5 ? 1 : 0, IsAnalysed: true))],
                Position = 0.9,
            };

            pixels = Render(timeline);
        });

        error.Should().BeNull();
        Pixel(pixels, 55, Height - 2).Should().Be((0xFF, 0x8C, 0x00), "the sixth of ten stretches has a track");
        Pixel(pixels, 20, Height - 2).Should().NotBe((0xFF, 0x8C, 0x00));
        Pixel(pixels, 90, 2).Should().Be((0xFF, 0xFF, 0xFF), "the player is at nine tenths");
    }

    [Fact]
    public void An_Event_In_A_Watch_Zone_Is_Drawn_In_The_Zones_Colour()
    {
        byte[] pixels = [];

        var error = WpfTestThread.Run(() =>
        {
            var zones = new[]
            {
                new AtcSoft.VideoSurveillance.Models.MotionZone { Name = "Gate", Kind = MotionZoneKind.Watch, Color = "Lime" },
                new AtcSoft.VideoSurveillance.Models.MotionZone { Name = "Gate", Kind = MotionZoneKind.Watch, Color = "Red" },
                new AtcSoft.VideoSurveillance.Models.MotionZone { Name = "Road", Kind = MotionZoneKind.Ignore, Color = "Blue" },
            };
            var timeline = new ActivityTimeline
            {
                Buckets =
                [
                    .. Enumerable.Range(0, 10).Select(i => i switch
                    {
                        2 => new ActivityBucket(30, 1, IsAnalysed: true) { Zones = ["Gate"] },
                        7 => new ActivityBucket(30, 1, IsAnalysed: true) { Zones = ["Road"] },
                        _ => new ActivityBucket(1, 0, IsAnalysed: true),
                    }),
                ],
                ZoneBrushes = MotionZoneColors.ByZone(zones),
            };

            pixels = Render(timeline);
        });

        error.Should().BeNull();
        Pixel(pixels, 25, Height - 2).Should().Be((0x00, 0xFF, 0x00), "the first zone named Gate is lime");
        Pixel(pixels, 75, Height - 2).Should().Be((0xFF, 0x8C, 0x00), "an ignore zone has no colour of its own: orange");
    }

    [Fact]
    public void Without_Activity_Nothing_Is_Drawn_But_The_Strip_Still_Takes_Clicks()
    {
        byte[] pixels = [];
        var hit = false;

        var error = WpfTestThread.Run(() =>
        {
            var timeline = new ActivityTimeline();
            pixels = Render(timeline);
            hit = System.Windows.Media.VisualTreeHelper.HitTest(timeline, new System.Windows.Point(50, 10)) is not null;
        });

        error.Should().BeNull();
        pixels.Should().OnlyContain(b => b == 0);
        hit.Should().BeTrue();
    }

    private static byte[] Render(ActivityTimeline timeline)
    {
        timeline.Measure(new System.Windows.Size(Width, Height));
        timeline.Arrange(new System.Windows.Rect(0, 0, Width, Height));
        timeline.UpdateLayout();

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(Width, Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(timeline);
        var pixels = new byte[Width * Height * 4];
        bitmap.CopyPixels(pixels, Width * 4, 0);
        return pixels;
    }

    private static (int R, int G, int B) Pixel(
        byte[] pixels,
        int x,
        int y)
    {
        var i = ((y * Width) + x) * 4;
        return (pixels[i + 2], pixels[i + 1], pixels[i]);
    }
}