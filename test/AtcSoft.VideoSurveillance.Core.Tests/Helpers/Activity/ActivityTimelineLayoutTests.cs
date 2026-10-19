namespace AtcSoft.VideoSurveillance.Helpers.Activity;

/// <summary>
/// The timeline under a recording: bars for how much changed, bands for where something moved,
/// and jumps to the next or previous event.
/// </summary>
public sealed class ActivityTimelineLayoutTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0.1)]
    [InlineData(25, 0.5)]
    [InlineData(100, 1)]
    [InlineData(250, 1)]
    [InlineData(-3, 0)]
    public void A_Bar_Rises_With_The_Square_Root_Of_The_Level(
        double level,
        double height)
        => ActivityTimelineLayout.BarHeight(level).Should().BeApproximately(height, 1e-9);

    [Fact]
    public void Runs_Of_Stretches_With_Something_Moving_Are_Events()
    {
        var buckets = Buckets(0, 1, 2, 0, 0, 1, 0, 0, 0, 3);

        ActivityTimelineLayout.FindEvents(buckets).Should().Equal(
            new ActivityEvent(0.1, 0.3),
            new ActivityEvent(0.5, 0.6),
            new ActivityEvent(0.9, 1.0));
    }

    [Fact]
    public void A_Five_Second_Event_In_An_Hour_Is_Drawn_Three_Pixels_Wide_Around_Its_Middle()
    {
        var fiveSeconds = new ActivityEvent(1800.0 / 3600, 1805.0 / 3600);

        var (x, width) = ActivityTimelineLayout.ToPixels(fiveSeconds, 1000);

        width.Should().Be(3);
        (x + 1.5).Should().BeApproximately(1802.5 / 3600 * 1000, 1e-9);
    }

    [Fact]
    public void A_Narrow_Band_At_An_Edge_Stays_On_The_Timeline_And_A_Wide_One_Keeps_Its_Width()
    {
        ActivityTimelineLayout.ToPixels(new ActivityEvent(0, 0.0001), 1000).Should().Be((0, 3));
        ActivityTimelineLayout.ToPixels(new ActivityEvent(0.9999, 1), 1000).Should().Be((997, 3));
        ActivityTimelineLayout.ToPixels(new ActivityEvent(0.25, 0.5), 1000).Should().Be((250, 250));
        ActivityTimelineLayout.ToPixels(new ActivityEvent(0.25, 0.5), 0).Should().Be((0, 0));
    }

    [Fact]
    public void Next_Goes_To_The_First_Event_After_The_Position()
    {
        var events = ActivityTimelineLayout.FindEvents(Buckets(0, 1, 0, 0, 1, 0, 0, 0, 0, 0));

        ActivityTimelineLayout.NextEventStart(events, 0).Should().Be(0.1);
        ActivityTimelineLayout.NextEventStart(events, 0.1).Should().Be(0.4);
        ActivityTimelineLayout.NextEventStart(events, 0.4).Should().BeNull();
    }

    [Fact]
    public void Previous_Goes_One_Back_Even_Right_After_Jumping_To_An_Event()
    {
        var events = ActivityTimelineLayout.FindEvents(Buckets(0, 1, 0, 0, 1, 0, 0, 0, 0, 0));

        ActivityTimelineLayout.PreviousEventStart(events, 0.8, slack: 0.01).Should().Be(0.4);
        ActivityTimelineLayout.PreviousEventStart(events, 0.405, slack: 0.01).Should().Be(0.1, "it is still at the start of the second event");
        ActivityTimelineLayout.PreviousEventStart(events, 0.1, slack: 0.01).Should().BeNull();
    }

    private static List<ActivityBucket> Buckets(params int[] tracks)
        => [.. tracks.Select(t => new ActivityBucket(t * 5, t, IsAnalysed: true))];
}