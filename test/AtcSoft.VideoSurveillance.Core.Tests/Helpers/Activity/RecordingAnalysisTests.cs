namespace AtcSoft.VideoSurveillance.Helpers.Activity;

using BuiltInMotionDetector = AtcSoft.VideoSurveillance.Helpers.MotionDetection.BuiltInMotionDetector;

/// <summary>
/// *Analyse recording*: an older recording gets the same activity index as one analysed while it
/// was made, never half of one.
/// </summary>
public sealed class RecordingAnalysisTests : IDisposable
{
    private const int Width = 160;
    private const int Height = 120;
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

    private readonly string folder = Path.Combine(Path.GetTempPath(), "atcsoft-analysis-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_Recording_Gets_An_Index_With_Its_Event_In_Place()
    {
        var video = Video();
        var start = RecordingAnalysis.StartOf(video, Copenhagen);

        var analysed = RecordingAnalysis.Run(Pictures(), Width, Height, new BuiltInMotionDetector(new MotionDetectionSettings()), video, start);

        analysed.Should().Be(80);
        start.Should().Be(new DateTimeOffset(2026, 10, 19, 7, 0, 0, TimeSpan.Zero), "the name's 09:00 is Copenhagen time");
        var log = ActivityLogReader.Read(RecordingPathHelper.BuildActivityPath(video))!;
        log.Start.Should().Be(start);
        log.Seconds.Should().HaveCount(20, "every second was analysed");
        log.Seconds.Where(s => s.Tracks > 0).Select(s => s.Offset).Should().NotBeEmpty().And.OnlyContain(offset => offset >= 5 && offset <= 15, "the square moves from 5 to 15 s");
        File.Exists(RecordingPathHelper.BuildActivityPath(video) + ".part").Should().BeFalse();
    }

    [Fact]
    public void A_Cancelled_Analysis_Leaves_No_Index_Behind()
    {
        var video = Video();
        using var cancellation = new CancellationTokenSource();

        var run = () => RecordingAnalysis.Run(
            Pictures().Select((p, i) =>
            {
                if (i == 10)
                {
                    cancellation.Cancel();
                }

                return p;
            }),
            Width,
            Height,
            new BuiltInMotionDetector(new MotionDetectionSettings()),
            video,
            DateTimeOffset.UnixEpoch,
            cancellation.Token);

        run.Should().Throw<OperationCanceledException>();
        Directory.GetFiles(Path.GetDirectoryName(video)!).Should().Equal(video);
    }

    // Twenty seconds at four pictures a second: a grey yard, and a dark square crossing it from 5 to 15 s.
    private static IEnumerable<(TimeSpan Position, byte[] Pixels)> Pictures()
    {
        for (var i = 0; i < 80; i++)
        {
            var pixels = Enumerable.Repeat((byte)128, Width * Height).ToArray();
            var seconds = i / 4.0;
            if (seconds is >= 5 and < 15)
            {
                var left = (int)((seconds - 5) * 12);
                for (var y = 50; y < 70; y++)
                {
                    for (var x = left; x < left + 20; x++)
                    {
                        pixels[(y * Width) + x] = 20;
                    }
                }
            }

            yield return (TimeSpan.FromSeconds(seconds), pixels);
        }
    }

    private string Video()
    {
        var video = Path.Combine(folder, "Garden", "Garden_20261019_090000_000.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(video)!);
        File.WriteAllText(video, "video");
        return video;
    }
}