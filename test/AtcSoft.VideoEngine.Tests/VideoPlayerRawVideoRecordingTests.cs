namespace AtcSoft.VideoEngine;

/// <summary>
/// The whole way through the player with a source that delivers uncompressed
/// pictures, as a webcam does. The source is a live test stream from the
/// FFmpeg command-line tool; a machine without it skips the test.
/// </summary>
public sealed class VideoPlayerRawVideoRecordingTests : IDisposable
{
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "atcsoft-player-" + Guid.NewGuid().ToString("N"));

    public VideoPlayerRawVideoRecordingTests()
    {
        Directory.CreateDirectory(directory);
    }

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

    [Theory]
    [InlineData("mkv")]
    [InlineData("mp4")]
    public async Task A_Source_Of_Uncompressed_Pictures_Is_Recorded_As_H264_Across_A_File_Switch(
        string format)
    {
        TestEngine.Require();
        var tool = FindFFmpegTool();
        Assert.SkipWhen(tool is null, "The FFmpeg command-line tool is not installed.");

        var cancellationToken = TestContext.Current.CancellationToken;
        var port = GetFreePort();
        using var source = StartRawVideoStream(tool!, port);
        var first = Path.Combine(directory, "first." + format);
        var second = Path.Combine(directory, "second." + format);

        using (var player = new VideoPlayer(Microsoft.Extensions.Logging.Abstractions.NullLogger<VideoPlayer>.Instance))
        {
            try
            {
                // Arrange
                player.Open(new Uri($"tcp://127.0.0.1:{port}"), new StreamOptions { HardwareAcceleration = false });
                await WaitUntilAsync(() => player.State == PlayerState.Playing && player.FramesDecoded > 0, cancellationToken);
                player.StreamInfo!.CodecName.Should().Be("rawvideo", "the test is about a source that is not compressed");

                // Act
                player.StartRecording(first);
                player.IsRecording.Should().BeTrue();
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

                player.SwitchRecording(second);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

                player.StopRecording();
                player.IsRecording.Should().BeFalse();
            }
            finally
            {
                player.Close();
                Stop(source);
            }
        }

        // Assert — about two seconds of 15 pictures a second in each file
        foreach (var path in new[] { first, second })
        {
            var recorded = ReadBack(path);
            recorded.Codec.Should().Be(AVCodecID.H264);
            recorded.Frames.Should().BeInRange(20, 40);
            recorded.FirstIsKeyFrame.Should().BeTrue();
            new FileInfo(path).Length.Should().BeLessThan(2_000_000, "four seconds of raw 320x240 pictures would be 9 MB");
        }
    }

    private static string? FindFFmpegTool()
    {
        var folder = VideoEngineBootstrap.FindFFmpegPath();
        if (folder is null)
        {
            return null;
        }

        var tool = Path.Combine(folder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        return File.Exists(tool) ? tool : null;
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    // Paced like a camera (-re): a file would be read to its end at once.
    private static System.Diagnostics.Process StartRawVideoStream(
        string tool,
        int port)
    {
        var start = new System.Diagnostics.ProcessStartInfo
        {
            FileName = tool,
            Arguments =
                "-hide_banner -loglevel error -re -f lavfi -i testsrc=size=320x240:rate=15 " +
                $"-c:v rawvideo -pix_fmt yuyv422 -t 60 -f nut \"tcp://127.0.0.1:{port}?listen=1\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        return System.Diagnostics.Process.Start(start)
            ?? throw new InvalidOperationException("The test stream could not be started.");
    }

    private static void Stop(System.Diagnostics.Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > StartTimeout)
            {
                Assert.Fail($"The player did not start playing within {StartTimeout.TotalSeconds:N0} s.");
            }

            await Task.Delay(100, cancellationToken);
        }
    }

    private static unsafe Recorded ReadBack(string path)
    {
        AVFormatContext* context = null;
        AVDictionary* options = null;
        avformat_open_input(ref context, path, null, ref options).Should().Be(0, $"'{path}' has to be a readable recording");
        var packet = av_packet_alloc();
        try
        {
            avformat_find_stream_info(context, ref options).Should().BeGreaterThanOrEqualTo(0);
            var codec = context->streams[0]->codecpar->codec_id;

            var frames = 0;
            var firstIsKeyFrame = false;
            while (av_read_frame(context, packet) >= 0)
            {
                if (frames == 0)
                {
                    firstIsKeyFrame = (packet->flags & PktFlags.Key) != PktFlags.None;
                }

                frames++;
                av_packet_unref(packet);
            }

            return new Recorded(codec, frames, firstIsKeyFrame);
        }
        finally
        {
            av_packet_free(ref packet);
            avformat_close_input(ref context);
        }
    }

    private sealed record Recorded(
        AVCodecID Codec,
        int Frames,
        bool FirstIsKeyFrame);
}