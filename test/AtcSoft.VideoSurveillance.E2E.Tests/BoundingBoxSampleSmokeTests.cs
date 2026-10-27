namespace AtcSoft.VideoSurveillance.E2E;

/// <summary>
/// The bounding box sample (<c>sample/BoundingBoxWpfApp</c>): it opens a storybook or a
/// recording, and what it shows is checked through UI Automation against a run of the same file
/// made here, and the storybook's score against the committed baseline. The overlay shows each
/// box as an element (<c>box-{trackId}</c>, the box in video pixels as its help text).
/// </summary>
[Collection(E2ETestGroup.Name)]
public sealed class BoundingBoxSampleSmokeTests
{
    private static readonly TimeSpan AnalysisTimeout = TimeSpan.FromMinutes(2);

    private static string ScenesFolder
        => Path.Combine(E2ESettings.RepositoryRoot, "test", "AtcSoft.VideoSurveillance.Storybook.Tests", "Scenes");

    [Fact]
    public void A_Storybook_Shows_The_Detectors_Boxes_And_The_Truth_Of_Each_Picture()
    {
        E2ESettings.SkipUnlessEnabled();

        const string sceneFile = "s05-car-human-cat.storybook.json";
        var scene = StorybookReader.ReadFile(Path.Combine(ScenesFolder, sceneFile));
        var expected = StorybookRunner.Run(scene, new BuiltInMotionDetector(scene.Detector.Settings()), new StorybookRunOptions(AnalysesPerSecond: 4));

        using var run = new BoundingBoxSampleRun(["--storybook", Path.Combine(ScenesFolder, sceneFile), "--rate", "4"]);
        var app = run.App;
        var window = app.WaitForWindowTitled(BoundingBoxSampleRun.WindowTitle, DesktopApp.StartTimeout);
        WaitUntilReady(app, window);

        // The score panel: the run made here, and the baseline the storybook tests keep.
        using (var json = JsonDocument.Parse(Element(app, window, "ScoreJson").Patterns.Value.Pattern.Value.Value))
        {
            var score = json.RootElement.GetProperty("score");
            score.GetProperty("recall").GetDouble().Should().Be(expected.Score.Recall);
            score.GetProperty("falseBoxesPerFrame").GetDouble().Should().Be(expected.Score.FalseBoxesPerFrame);
            score.GetProperty("trackCount").GetInt32().Should().Be(expected.Score.TrackCount);
            json.RootElement.GetProperty("way").GetString().Should().Be("fast");

            var baseline = Baseline("s05-car-human-cat@4/builtIn");
            score.GetProperty("recall").GetDouble().Should().BeGreaterThanOrEqualTo(baseline.GetProperty("recall").GetDouble() - 0.005);
            score.GetProperty("falseBoxesPerFrame").GetDouble().Should().BeLessThanOrEqualTo(baseline.GetProperty("falseBoxesPerFrame").GetDouble() + 0.005);
        }

        // A picture with all three moving, and the last picture, after they have gone.
        var busy = expected.Frames.ToList().FindIndex(f => f.Detected.Count == 3);
        busy.Should().BePositive("S5 has a moment with the car, the human and the cat all boxed");
        foreach (var index in new[] { busy, expected.Frames.Count - 1 })
        {
            GoTo(app, window, index, expected.Frames.Count);
            var frame = expected.Frames[index];

            Boxes(app, window, "DetectorBoxes").Should().BeEquivalentTo(
                frame.Detected.Select((d, i) => (d.TrackId is { } id ? $"box-{id}" : $"box-at-{i}", Describe(d.Box))),
                $"the detector's boxes of picture {index}");
            Boxes(app, window, "TruthBoxes").Select(b => b.Box).Should().BeEquivalentTo(
                frame.Truth.Where(t => !t.IsDistractor && t.State == TruthState.MustBox).Select(t => Describe(t.Box)),
                $"the objects to box in picture {index}");

            // A picture of the window in the run folder, to look at.
            window.SetForeground();
            window.Move(0, 0);
            DesktopApp.Settle(TimeSpan.FromMilliseconds(500));
            FlaUI.Core.Capturing.Capture.MainScreen().ToFile(Path.Combine(run.DataFolder, $"s05-picture-{index}.png"));
        }

        var closed = app.CloseApp(window);
        LogFindings.AssertClean(run.LogFolder, "the bounding box sample");
        closed.Should().BeTrue("the sample closes with its window");
    }

    [Fact]
    public void A_Recording_Shows_The_Detectors_Boxes_Without_A_Truth()
    {
        E2ESettings.SkipUnlessEnabled();
        var folder = VideoEngineBootstrap.FindFFmpegPath();
        var tool = folder is null ? null : Path.Combine(folder, "ffmpeg.exe");
        Assert.SkipUnless(tool is not null && File.Exists(tool), "The FFmpeg command-line tool makes the recording.");
        VideoEngineBootstrap.Initialize();

        // A recording of a slow walker, made from the storybook, opened as a plain recording.
        var scene = StorybookReader.ReadFile(Path.Combine(ScenesFolder, "s03-slow-walker.storybook.json"));
        scene.Video.BitrateKbps = 2000;
        var recording = Path.Combine(E2ESettings.NewFolder("bounding-box-recording"), "slow-walker.mkv");
        StorybookEncoder.Encode(scene, tool, recording, TestContext.Current.CancellationToken);
        var expected = StorybookRunner.RunRecording(recording, new BuiltInMotionDetector(new AtcSoft.VideoSurveillance.Models.Settings.MotionDetectionSettings()), new StorybookRunOptions(AnalysesPerSecond: 4), TestContext.Current.CancellationToken);

        using var run = new BoundingBoxSampleRun(["--recording", recording]);
        var app = run.App;
        var window = app.WaitForWindowTitled(BoundingBoxSampleRun.WindowTitle, DesktopApp.StartTimeout);
        WaitUntilReady(app, window);

        Element(app, window, "ScoreSummary").Name.Should().Contain("no truth");
        var boxed = expected.Frames.ToList().FindIndex(f => f.Detected.Count > 0);
        boxed.Should().BePositive("the walker is found");
        GoTo(app, window, boxed, expected.Frames.Count);
        Boxes(app, window, "DetectorBoxes").Should().BeEquivalentTo(
            expected.Frames[boxed].Detected.Select((d, i) => (d.TrackId is { } id ? $"box-{id}" : $"box-at-{i}", Describe(d.Box))));
        Boxes(app, window, "TruthBoxes").Should().BeEmpty("a recording has no truth");

        var closed = app.CloseApp(window);
        LogFindings.AssertClean(run.LogFolder, "the bounding box sample");
        closed.Should().BeTrue("the sample closes with its window");
    }

    private static string Describe(PixelBox box)
        => string.Create(CultureInfo.InvariantCulture, $"{box.X:0.##},{box.Y:0.##},{box.Width:0.##},{box.Height:0.##}");

    private static JsonElement Baseline(string key)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(E2ESettings.RepositoryRoot, "test", "AtcSoft.VideoSurveillance.Storybook.Tests", "baseline.json")));
        return document.RootElement.GetProperty(key).Clone();
    }

    private static AutomationElement Element(
        DesktopApp app,
        AutomationElement window,
        string automationId)
        => app.FindWhere(window, e => e.Properties.AutomationId.ValueOrDefault == automationId, $"with the automation id '{automationId}'");

    private static void WaitUntilReady(
        DesktopApp app,
        AutomationElement window)
    {
        var status = Element(app, window, "Status");
        var deadline = DateTime.UtcNow + AnalysisTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var text = status.Properties.Name.ValueOrDefault ?? string.Empty;
            if (text == "Ready")
            {
                return;
            }

            if (text.StartsWith("Failed", StringComparison.Ordinal) || text.Contains("needed", StringComparison.Ordinal))
            {
                throw app.Fail($"The sample could not open the file: {text}");
            }

            Thread.Sleep(250);
        }

        throw app.Fail("The sample did not finish analysing in time.");
    }

    private static void GoTo(
        DesktopApp app,
        AutomationElement window,
        int index,
        int count)
    {
        Element(app, window, "FrameSlider").Patterns.RangeValue.Pattern.SetValue(index);
        var info = Element(app, window, "FrameInfo");
        var expected = $"picture {index + 1} of {count}";
        var deadline = DateTime.UtcNow + DesktopApp.ElementTimeout;
        while (!(info.Properties.Name.ValueOrDefault ?? string.Empty).Contains(expected, StringComparison.Ordinal))
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw app.Fail($"The sample did not show {expected}.");
            }

            Thread.Sleep(100);
        }
    }

    private static List<(string Id, string Box)> Boxes(
        DesktopApp app,
        AutomationElement window,
        string overlayId)
        => Element(app, window, overlayId)
            .FindAllChildren()
            .Select(b => (b.Properties.AutomationId.ValueOrDefault ?? string.Empty, b.Properties.HelpText.ValueOrDefault ?? string.Empty))
            .ToList();
}