namespace AtcSoft.VideoSurveillance.Storybook;

/// <summary>
/// The new detector meets each scene's own expectations (<c>expect</c> in the storybook): the
/// recall, the false boxes per frame and, where given, the number of tracks: one box per object,
/// no box for a tree in the wind or a camera clock.
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
        var detector = new BuiltInMotionDetector(new MotionDetectionSettings());

        var score = StorybookRunner.Run(scene, detector, new StorybookRunOptions(AnalysesPerSecond: analysesPerSecond)).Score;
        TestContext.Current.TestOutputHelper?.WriteLine(score.Summary);

        score.Recall.Should().BeGreaterThanOrEqualTo(scene.Expect.MinRecall, "every moving object needs its box");
        score.FalseBoxesPerFrame.Should().BeLessThanOrEqualTo(scene.Expect.MaxFalseBoxesPerFrame, "nothing else may get one");
        if (scene.Expect.Tracks is { } tracks)
        {
            score.TrackCount.Should().Be(tracks, "one track per object");
            score.Objects.Should().OnlyContain(o => o.IdSwitches <= scene.Expect.MaxIdSwitchesPerObject);
        }
    }
}