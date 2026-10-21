namespace AtcSoft.VideoSurveillance.Storybook;

/// <summary>
/// The new detector meets each scene's own expectations (<c>expect</c> in the storybook), with the
/// scene's own detector settings: the recall, the false boxes per frame and, where given, the
/// number of tracks: one box per object, no box for a tree in the wind or a camera clock. A scene
/// marked known-bad is skipped with its reason while it fails, and fails once it passes, so the
/// mark is taken off.
/// </summary>
public sealed class BuiltInDetectorExpectationTests
{
    public static TheoryData<string, int> Runs()
    {
        var data = new TheoryData<string, int>();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Scenes"), "*.storybook.json").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(file), 4);
            data.Add(Path.GetFileName(file), 15);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Runs))]
    public void The_Scene_Gets_What_It_Expects(
        string sceneFile,
        int analysesPerSecond)
    {
        var scene = StorybookReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "Scenes", sceneFile));
        var detector = new BuiltInMotionDetector(scene.Detector.Settings());

        var score = StorybookRunner.Run(scene, detector, new StorybookRunOptions(AnalysesPerSecond: analysesPerSecond)).Score;
        TestContext.Current.TestOutputHelper?.WriteLine(score.Summary);

        if (scene.Expect.KnownBad is { } knownBad)
        {
            Meets(scene.Expect, score).Should().BeFalse($"{sceneFile} meets its expectations now: take off its knownBad mark");
            Assert.Skip($"Known bad: {knownBad}");
        }

        score.Recall.Should().BeGreaterThanOrEqualTo(scene.Expect.MinRecall, "every moving object needs its box");
        score.FalseBoxesPerFrame.Should().BeLessThanOrEqualTo(scene.Expect.MaxFalseBoxesPerFrame, "nothing else may get one");
        if (scene.Expect.Tracks is { } tracks)
        {
            score.TrackCount.Should().Be(tracks, "one track per object");
            score.Objects.Should().OnlyContain(o => o.IdSwitches <= scene.Expect.MaxIdSwitchesPerObject);
        }
    }

    private static bool Meets(
        StorybookExpect expect,
        StorybookScore score)
        => score.Recall >= expect.MinRecall &&
           score.FalseBoxesPerFrame <= expect.MaxFalseBoxesPerFrame &&
           (expect.Tracks is not { } tracks || (score.TrackCount == tracks && score.Objects.All(o => o.IdSwitches <= expect.MaxIdSwitchesPerObject)));
}