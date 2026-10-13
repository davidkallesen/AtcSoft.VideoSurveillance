namespace AtcSoft.VideoSurveillance.Api.Services;

/// <summary>
/// The server's motion detector, driven one picture at a time. The pictures
/// are a single byte each that the reader turns into a grayscale picture:
/// level 50 is the empty scene, anything else has a bright block in it.
/// </summary>
public sealed class ServerMotionDetectionServiceTests : IDisposable
{
    private const int Width = 320;
    private const int Height = 240;
    private const byte EmptyScene = 50;
    private const byte SceneWithBlock = 200;
    private const byte Unreadable = 0;

    private readonly Guid cameraId = Guid.NewGuid();
    private readonly IMediaPipeline pipeline = Substitute.For<IMediaPipeline>();
    private readonly FakeClock clock = new();
    private readonly List<MotionDetectedEventArgs> events = [];
    private readonly MotionDetectionSettings settings = new()
    {
        AnalysisWidth = Width,
        AnalysisHeight = Height,
        MinimumChangePercent = 0.5,
        PostMotionDurationSeconds = 10,
    };

    private readonly ServerMotionDetectionService sut;

    public ServerMotionDetectionServiceTests()
    {
        sut = new ServerMotionDetectionService(
            NullLogger<ServerMotionDetectionService>.Instance,
            new BlockReader(),
            clock,
            runLoops: false);
        sut.MotionDetected += (_, e) => events.Add(e);
        sut.StartDetection(cameraId, pipeline, settings);
    }

    public void Dispose()
    {
        sut.Dispose();
        pipeline.Dispose();
    }

    [Fact]
    public async Task A_Scene_That_Does_Not_Change_Raises_Nothing()
    {
        await ShowAsync(EmptyScene, EmptyScene, EmptyScene);

        events.Should().BeEmpty();
        sut.IsMotionDetected(cameraId).Should().BeFalse();
    }

    [Fact]
    public async Task Something_Moving_Into_The_Scene_Is_Reported_With_Where_It_Is()
    {
        await ShowAsync(EmptyScene, SceneWithBlock);

        var motion = events.Should().ContainSingle().Subject;
        motion.CameraId.Should().Be(cameraId);
        motion.IsMotionActive.Should().BeTrue();
        motion.ChangePercentage.Should().BeGreaterThan(5);
        motion.BoundingBoxes.Should().ContainSingle();
        motion.AnalysisWidth.Should().Be(Width);
        motion.AnalysisHeight.Should().Be(Height);
        sut.IsMotionDetected(cameraId).Should().BeTrue();
        sut.GetLastBoundingBoxes(cameraId).Should().ContainSingle();
        sut.GetAnalysisResolution(cameraId).Should().Be((Width, Height));
    }

    [Fact]
    public async Task Motion_Is_Over_Only_After_The_Scene_Has_Been_Quiet_For_The_Post_Motion_Time()
    {
        // Something that moves stops and starts; an end reported at every
        // pause would start and stop a recording many times a minute.
        await ShowAsync(EmptyScene, SceneWithBlock);
        events.Clear();

        clock.Advance(TimeSpan.FromSeconds(4));
        await ShowAsync(SceneWithBlock);
        clock.Advance(TimeSpan.FromSeconds(5));
        await ShowAsync(SceneWithBlock);

        events.Should().BeEmpty("the scene has been quiet for 9 of the 10 seconds");
        sut.IsMotionDetected(cameraId).Should().BeTrue();

        clock.Advance(TimeSpan.FromSeconds(1));
        await ShowAsync(SceneWithBlock);

        var end = events.Should().ContainSingle().Subject;
        end.IsMotionActive.Should().BeFalse();
        end.BoundingBoxes.Should().BeEmpty();
        sut.IsMotionDetected(cameraId).Should().BeFalse();
        sut.GetLastBoundingBoxes(cameraId).Should().BeEmpty();
    }

    [Fact]
    public async Task Movement_During_The_Quiet_Time_Keeps_The_Motion_Going()
    {
        await ShowAsync(EmptyScene, SceneWithBlock);
        clock.Advance(TimeSpan.FromSeconds(8));
        await ShowAsync(EmptyScene);
        clock.Advance(TimeSpan.FromSeconds(8));
        await ShowAsync(EmptyScene);

        events.Should().HaveCount(2).And.OnlyContain(e => e.IsMotionActive);
        sut.IsMotionDetected(cameraId).Should().BeTrue();
    }

    [Fact]
    public async Task A_Change_Below_The_Minimum_Is_Not_Motion()
    {
        settings.MinimumChangePercent = 50;

        await ShowAsync(EmptyScene, SceneWithBlock);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Picture_That_Cannot_Be_Captured_Or_Read_Is_Skipped()
    {
        await ShowAsync(EmptyScene);
        pipeline.CaptureFrameAsync(Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        await sut.AnalyzeOnceAsync(cameraId, TestContext.Current.CancellationToken);
        await ShowAsync(Unreadable);
        pipeline.CaptureFrameAsync(Arg.Any<CancellationToken>()).Returns<byte[]?>(_ => throw new InvalidOperationException("camera gone"));
        await sut.AnalyzeOnceAsync(cameraId, TestContext.Current.CancellationToken);

        await ShowAsync(SceneWithBlock);

        events.Should().ContainSingle("the block is compared with the last picture that could be read")
            .Which.IsMotionActive.Should().BeTrue();
    }

    [Fact]
    public async Task A_Camera_That_Is_No_Longer_Watched_Raises_Nothing()
    {
        await ShowAsync(EmptyScene);
        sut.StopDetection(cameraId);

        await ShowAsync(SceneWithBlock);

        events.Should().BeEmpty();
        sut.IsDetectionActive(cameraId).Should().BeFalse();
    }

    [Fact]
    public async Task Stopping_While_Something_Moves_Reports_The_End_Of_The_Motion()
    {
        // Whoever started a recording on the motion has to hear that it is over.
        await ShowAsync(EmptyScene, SceneWithBlock);
        events.Clear();

        sut.StopDetection(cameraId);

        events.Should().ContainSingle().Which.IsMotionActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(2, 500)]
    [InlineData(15, 250)]
    [InlineData(30, 250)]
    public void Pictures_Are_Analysed_At_The_Chosen_Rate_But_No_More_Than_Four_A_Second(
        int analysisFrameRate,
        int expectedMilliseconds)
        => ServerMotionDetectionService.GetInterval(new MotionDetectionSettings { AnalysisFrameRate = analysisFrameRate })
            .TotalMilliseconds.Should().Be(expectedMilliseconds);

    private async Task ShowAsync(params byte[] scenes)
    {
        foreach (var scene in scenes)
        {
            pipeline.CaptureFrameAsync(Arg.Any<CancellationToken>()).Returns(new[] { scene });
            await sut.AnalyzeOnceAsync(cameraId, TestContext.Current.CancellationToken);
        }
    }

    private sealed class BlockReader : IMotionFrameReader
    {
        public byte[]? ReadGrayscale(
            byte[] encodedImage,
            int width,
            int height)
        {
            if (encodedImage[0] == Unreadable)
            {
                return null;
            }

            var picture = new byte[width * height];
            Array.Fill(picture, EmptyScene);
            if (encodedImage[0] != EmptyScene)
            {
                for (var row = 60; row < 140; row++)
                {
                    Array.Fill(picture, encodedImage[0], (row * width) + 100, 100);
                }
            }

            return picture;
        }
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}