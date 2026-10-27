namespace AtcSoft.VideoSurveillance.Storybook;

/// <summary>
/// A scene encoded to H.264 by the FFmpeg command-line tool, as a camera would send it, and read
/// back the way the apps analyse a recording: one picture for every frame drawn, at the frame's
/// time, so the truth of each picture is the truth of the frame it came from. Needs the FFmpeg
/// libraries and its command-line tool; skipped without them.
/// </summary>
public sealed class StorybookEncoderTests : IDisposable
{
    private const string Scene = """
        { "version": 1, "name": "encoded",
          "video": { "width": 160, "height": 120, "fps": 10, "durationSeconds": 2, "bitrateKbps": 200 },
          "objects": [ { "id": "car", "kind": "car", "size": [0.3, 0.2],
                         "path": [ { "t": 0, "at": [0.2, 0.5] }, { "t": 2, "at": [0.8, 0.5] } ] } ] }
        """;

    private readonly string directory = Path.Combine(Path.GetTempPath(), "atcsoft-encode-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a temp folder left behind is harmless.
        }
    }

    [Fact]
    public void Every_Frame_Drawn_Comes_Back_At_Its_Time()
    {
        var tool = EncodedScenes.RequireTool();
        var scene = StorybookReader.Read(Scene);
        Directory.CreateDirectory(directory);
        var video = Path.Combine(directory, "encoded.mkv");

        var truth = StorybookEncoder.Encode(scene, tool, video, TestContext.Current.CancellationToken);

        truth.Should().HaveCount(scene.Video.FrameCount);
        using var reader = GreyFrameFileReader.Open(video, 80, 60);
        var positions = reader.ReadFrames(scene.Video.Fps, TestContext.Current.CancellationToken).Select(f => f.Position.TotalSeconds).ToList();
        positions.Should().HaveCount(scene.Video.FrameCount, "one picture for every frame drawn");
        positions.Select((p, i) => Math.Abs(p - truth[i].Time)).Should().OnlyContain(d => d < 0.002, "each picture at its frame's time");
        TruthFile.Read(TruthFile.PathFor(video)).Should().BeEquivalentTo(truth, o => o.WithStrictOrdering());
    }
}