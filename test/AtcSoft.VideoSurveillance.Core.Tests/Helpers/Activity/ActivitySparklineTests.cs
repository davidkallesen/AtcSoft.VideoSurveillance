namespace AtcSoft.VideoSurveillance.Helpers.Activity;

/// <summary>
/// A recording's sparkline keeps the most of each stretch, so a short event still shows, and a
/// recording without an index has none.
/// </summary>
public sealed class ActivitySparklineTests
{
    [Fact]
    public void A_Short_Event_In_A_Long_Recording_Shows_In_Its_Stretch()
    {
        // An hour in ten-second stretches: one busy stretch at 30 minutes.
        var buckets = Enumerable.Range(0, 360)
            .Select(i => new ActivityBucket(i == 180 ? 42.4 : 1, i == 180 ? 1 : 0, IsAnalysed: true))
            .ToList();

        var sparkline = ActivitySparkline.From(Summary(buckets))!;

        sparkline.Should().HaveCount(ActivitySparkline.Points);
        sparkline[12].Should().Be(42);
        sparkline.Where((_, i) => i != 12).Should().OnlyContain(level => level == 1);
    }

    [Fact]
    public void A_Short_Recording_Spreads_Over_Every_Point_And_Unanalysed_Stretches_Stay_Zero()
    {
        var sparkline = ActivitySparkline.From(Summary([new(10, 0, true), new(0, 0, false), new(90, 2, true)]), points: 6)!;

        sparkline.Should().Equal(10, 10, 0, 0, 90, 90);
    }

    [Fact]
    public void Without_An_Index_There_Is_No_Sparkline()
        => ActivitySparkline.From(null).Should().BeNull();

    private static ActivitySummary Summary(
        IReadOnlyList<ActivityBucket> buckets)
        => new(DateTimeOffset.UnixEpoch, buckets.Count * 10, buckets, []);
}