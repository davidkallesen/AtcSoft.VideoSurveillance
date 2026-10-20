namespace AtcSoft.VideoSurveillance.Helpers.MotionDetection;

/// <summary>
/// Watch zones: filled in with the topmost on top and ignore zones apart, the same sensitivity
/// formula as the camera's, and their values kept wherever zones are copied or compared.
/// </summary>
public sealed class WatchZoneTests
{
    [Fact]
    public void Each_Pixel_Gets_The_Topmost_Watch_Zone_And_Ignore_Zones_Are_Left_Out()
    {
        // 4 x 2: the first watch zone covers the left half, the second the middle two columns.
        IReadOnlyList<MotionZone> zones =
        [
            Zone(MotionZoneKind.Watch, 0, 0.5),
            Zone(MotionZoneKind.Ignore, 0, 1),
            Zone(MotionZoneKind.Watch, 0.25, 0.75),
        ];

        var map = ZoneMask.Watched(zones, 4, 2)!;

        map.Should().Equal(1, 2, 2, 0, 1, 2, 2, 0);
        ZoneMask.WatchZones(zones).Should().Equal(zones[0], zones[2]);
        ZoneMask.Watched([Zone(MotionZoneKind.Ignore, 0, 1)], 4, 2).Should().BeNull("there is no watch zone");
        ZoneMask.Ignored(zones, 4, 2).Should().OnlyContain(b => b == 1, "the ignore zone covers it all, watch zones or not");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(100)]
    public void A_Zones_Sensitivity_Means_What_The_Cameras_Does(int sensitivity)
    {
        var camera = MotionDetectorOptions.From(new MotionDetectionSettings { Sensitivity = sensitivity });

        MotionDetectorOptions.Thresholds(sensitivity).Should().Be((camera.ThresholdSigmas, camera.MinThreshold));
        MotionDetectorOptions.DefaultSmallestObjectPercent.Should().BeApproximately(camera.SmallestObjectShare * 100, 1e-12);
    }

    [Fact]
    public void A_Zones_Own_Values_Are_Copied_And_Compared()
    {
        var zone = Zone(MotionZoneKind.Watch, 0, 1);
        zone.Sensitivity = 80;
        zone.SmallestObjectPercent = 0.1;

        var copy = zone.Clone();
        copy.ValueEquals(zone).Should().BeTrue();

        copy.Sensitivity = 81;
        copy.ValueEquals(zone).Should().BeFalse();
        copy.Sensitivity = 80;
        copy.SmallestObjectPercent = 0.2;
        copy.ValueEquals(zone).Should().BeFalse();
    }

    [Fact]
    public void A_Draft_With_Only_A_New_Sensitivity_Is_Changed_And_Keeps_It_Through_A_Turn()
    {
        var draft = new MotionZoneDraft([Zone(MotionZoneKind.Watch, 0.1, 0.4)], CameraRotation.None);
        var edited = draft.Zones[0].Clone();
        edited.Sensitivity = 90;

        draft.Change([edited]);
        draft.Turn(CameraRotation.Rotate90);

        draft.IsChanged.Should().BeTrue();
        draft.Zones[0].Sensitivity.Should().Be(90);
    }

    private static MotionZone Zone(
        MotionZoneKind kind,
        double left,
        double right)
        => new()
        {
            Name = kind.ToString(),
            Kind = kind,
            Points = [new(left, 0), new(right, 0), new(right, 1), new(left, 1)],
        };
}