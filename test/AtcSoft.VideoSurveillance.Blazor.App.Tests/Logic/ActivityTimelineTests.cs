namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

using CoreBucket = AtcSoft.VideoSurveillance.Models.ActivityBucket;
using CoreLayout = AtcSoft.VideoSurveillance.Helpers.Activity.ActivityTimelineLayout;

/// <summary>
/// The web player's activity timeline: the same layout as the desktop apps' (a copy, kept equal
/// here), drawn as SVG, with jumps to the previous and next event.
/// </summary>
public sealed class ActivityTimelineTests
{
    private static readonly int[][] Recordings =
    [
        [0, 1, 2, 0, 0, 1, 0, 0, 0, 3],
        [1, 1, 1, 1],
        [0, 0, 0],
        [],
        [.. Enumerable.Range(0, 3600).Select(i => i is >= 1800 and < 1805 ? 1 : 0)],
    ];

    [Fact]
    public void The_Copy_Lays_Out_Like_The_Desktop_Apps()
    {
        foreach (var tracks in Recordings)
        {
            var web = ActivityTimelineLayout.FindEvents([.. tracks.Select(t => Bucket(t * 7, t))]);
            var core = CoreLayout.FindEvents([.. tracks.Select(t => new CoreBucket(t * 7, t, IsAnalysed: true))]);

            web.Select(e => (e.Start, e.End)).Should().Equal(core.Select(e => (e.Start, e.End)));
            foreach (var width in new[] { 0, 10, 400, 1000 })
            {
                web.Select(e => ActivityTimelineLayout.ToPixels(e, width))
                    .Should().Equal(core.Select(e => CoreLayout.ToPixels(e, width)));
            }

            foreach (var position in new[] { 0, 0.1, 0.3, 0.5001, 0.95, 1 })
            {
                ActivityTimelineLayout.NextEventStart(web, position).Should().Be(CoreLayout.NextEventStart(core, position));
                ActivityTimelineLayout.PreviousEventStart(web, position, 0.01).Should().Be(CoreLayout.PreviousEventStart(core, position, 0.01));
                ActivityTimelineLayout.EventsOnlyTarget(web, position, 0.01, 0.02).Should().Be(CoreLayout.EventsOnlyTarget(core, position, 0.01, 0.02));
            }
        }

        foreach (var level in new[] { -1, 0, 0.5, 4, 25, 99, 100, 400 })
        {
            ActivityTimelineLayout.BarHeight(level).Should().Be(CoreLayout.BarHeight(level));
        }

        ActivityTimelineLayout.MinimumEventWidth.Should().Be(CoreLayout.MinimumEventWidth);
    }

    [Fact]
    public void Bars_Rise_From_Above_The_Band_And_Stretches_Not_Analysed_Are_Marked()
    {
        RecordingActivityBucket[] buckets = [Bucket(25, 0), Bucket(0, 0), Bucket(100, 1), new(Level: 0, Tracks: 0, IsAnalysed: false)];

        var bars = ActivityTimelineShapes.Bars(buckets);
        var notAnalysed = ActivityTimelineShapes.NotAnalysed(buckets);
        var bands = ActivityTimelineShapes.Bands(buckets);

        var area = ActivityTimelineShapes.BoxHeight - ActivityTimelineShapes.BandHeight;
        bars.Should().Equal(new TimelineRect(0, area / 2, 1, area / 2), new TimelineRect(2, 0, 1, area));
        notAnalysed.Should().Equal(new TimelineRect(3, 0, 1, area));
        bands.Should().ContainSingle().Which.Should().Be(new TimelineRect(1, area, 3, ActivityTimelineShapes.BandHeight), "a one-stretch event is widened around its middle");
    }

    [Fact]
    public void Next_And_Previous_Land_A_Second_Before_The_Event()
    {
        // A 100 s recording with events at 10 s and 50 s.
        var events = new[] { new ActivityTimelineEvent(0.1, 0.2), new ActivityTimelineEvent(0.5, 0.6) };

        ActivityEventJumps.Next(events, 0, 100).Should().BeApproximately(9, 1e-9);
        ActivityEventJumps.Next(events, 9, 100).Should().BeApproximately(49, 1e-9, "right after a jump, next goes on to the following event");
        ActivityEventJumps.Next(events, 49, 100).Should().BeNull();
        ActivityEventJumps.Previous(events, 49, 100).Should().BeApproximately(9, 1e-9, "right after a jump, previous goes one further back");
        ActivityEventJumps.Previous(events, 80, 100).Should().BeApproximately(49, 1e-9);
        ActivityEventJumps.Previous(events, 9, 100).Should().BeNull();
        ActivityEventJumps.Next(events, 0, 0).Should().BeNull("the length is not known yet");
    }

    [Fact]
    public void An_Svg_Number_Has_A_Point_In_Every_Language()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("da-DK");

            SvgNumber.Format(12.5).Should().Be("12.5");
            SvgNumber.Format(1234.56789).Should().Be("1234.568");
            SvgNumber.Format(3).Should().Be("3");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData("PT1M5S", 65)]
    [InlineData("00:00:30", 30)]
    [InlineData(null, 0)]
    [InlineData("nonsense", 0)]
    public void The_Length_Of_A_Recording_Is_Read_In_Seconds(
        string? duration,
        double seconds)
        => RecordingDisplay.DurationSeconds(duration).Should().Be(seconds);

    private static RecordingActivityBucket Bucket(
        double level,
        int tracks)
        => new(Level: level, Tracks: tracks, IsAnalysed: true);

    [Fact]
    public void Play_Events_Only_Skips_The_Quiet_Stretches_In_Seconds_And_The_Playhead_Stays_In_The_Box()
    {
        // A 100 s recording with something moving from 20 to 30 s.
        ActivityTimelineEvent[] events = [new(0.2, 0.3)];

        ActivityEventJumps.EventsOnly(events, 5, 100).Should().BeApproximately(19, 1e-9, "a second before the event");
        ActivityEventJumps.EventsOnly(events, 31, 100).Should().BeNull("two seconds after it are shown");
        ActivityEventJumps.EventsOnly(events, 33, 100).Should().Be(100, "after the last event: the end");
        ActivityEventJumps.EventsOnly(events, 33, 0).Should().BeNull("the length is not known yet");

        ActivityTimelineShapes.Playhead(-1, 400).Should().BeNull();
        ActivityTimelineShapes.Playhead(0.5, 400)!.X.Should().Be(199);
        ActivityTimelineShapes.Playhead(1, 400)!.X.Should().Be(398, "the line stays inside the box");
    }
}