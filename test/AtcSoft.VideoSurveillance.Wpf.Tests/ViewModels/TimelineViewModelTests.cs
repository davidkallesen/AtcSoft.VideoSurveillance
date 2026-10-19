namespace AtcSoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// The client's Timeline view: the span it asks the server for, and how each camera's lane is
/// laid out for the lane control.
/// </summary>
public sealed class TimelineViewModelTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Copenhagen = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

    [Fact]
    public void Without_A_Day_The_Span_Is_The_Last_24_Hours()
        => TimelineViewModel.SpanOf(day: null, Nine, Copenhagen).Should().Be((Nine.AddHours(-24), Nine));

    [Fact]
    public void A_Day_Runs_From_Midnight_To_Midnight_In_The_Viewers_Zone_Also_When_The_Clocks_Change()
    {
        var (from, to) = TimelineViewModel.SpanOf(new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Unspecified), Nine, Copenhagen);
        var (backFrom, backTo) = TimelineViewModel.SpanOf(new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Unspecified), Nine, Copenhagen);

        from.UtcDateTime.Should().Be(new DateTime(2026, 10, 18, 22, 0, 0, DateTimeKind.Utc));
        (to - from).Should().Be(TimeSpan.FromHours(24));
        (backTo - backFrom).Should().Be(TimeSpan.FromHours(25));
    }

    [Fact]
    public void A_Lane_Places_Its_Events_And_Now_As_Fractions_Of_The_Span()
    {
        var lane = new TimelineLane(
            CameraId: Guid.NewGuid(),
            CameraName: "Garden",
            Coverage: [],
            Buckets: [new TimelineBucket(Level: 5, Tracks: 1, IsRecorded: true, IsAnalysed: true), new TimelineBucket(Level: 0, Tracks: 0, IsRecorded: false, IsAnalysed: false)],
            Events: [new TimelineEvent(Guid.Empty, Nine.AddMinutes(15), Nine.AddMinutes(30), 5, 1, "Garden/a.mkv", 900)]);

        var shown = new TimelineLaneViewModel(lane, Nine, Nine.AddHours(1), Nine.AddMinutes(45), (_, _) => { });
        var past = new TimelineLaneViewModel(lane, Nine, Nine.AddHours(1), Nine.AddHours(2), (_, _) => { });

        shown.EventBands.Should().Equal(new ActivityEvent(0.25, 0.5));
        shown.NowFraction.Should().Be(0.75);
        past.NowFraction.Should().Be(-1, "now is after the span: no line");
        shown.Stretches.Should().Equal(new TimelineStretch(5, 1, IsRecorded: true, IsAnalysed: true), new TimelineStretch(0, 0, IsRecorded: false, IsAnalysed: false));
    }

    [Fact]
    public void A_Click_On_A_Lane_Hands_Its_Place_To_The_View()
    {
        var lane = new TimelineLane(Guid.NewGuid(), "Garden", [], [], []);
        (TimelineLaneViewModel Lane, double Fraction)? played = null;
        var viewModel = new TimelineLaneViewModel(lane, Nine, Nine.AddHours(1), Nine, (l, f) => played = (l, f));

        viewModel.PlayAtCommand.Execute(0.4);

        played.Should().NotBeNull();
        played!.Value.Fraction.Should().Be(0.4);
        played.Value.Lane.Should().BeSameAs(viewModel);
    }
}