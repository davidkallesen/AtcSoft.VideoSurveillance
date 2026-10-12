namespace AtcSoft.CameraWall.Wpf.Services;

using Drawing = System.Drawing;
using IMediaPipeline = AtcSoft.VideoSurveillance.Services.IMediaPipeline;

public sealed class ThumbnailGeneratorServiceTests : IDisposable
{
    // The generator draws each frame into a 320x240 cell.
    private const int CellWidth = 320;
    private const int CellHeight = 240;

    private static readonly byte[] WhiteFrame = CreateWhitePng();

    private readonly string directory = Path.Combine(Path.GetTempPath(), "atcsoft-thumbs-" + Guid.NewGuid().ToString("N"));
    private readonly ThumbnailGeneratorService sut = new(NullLogger<ThumbnailGeneratorService>.Instance)
    {
        CaptureInterval = TimeSpan.FromMilliseconds(20),
    };

    public void Dispose()
    {
        sut.StopAllCaptures();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Fills_All_Four_Tiles_When_The_First_Capture_Comes_Back_Empty()
    {
        // A recording starts the moment the stream opens, before the first
        // frame is decoded, so the first capture has nothing to return.
        var pipeline = Substitute.For<IMediaPipeline>();
        pipeline.CaptureFrameAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult<byte[]?>(null),
            Task.FromResult<byte[]?>(WhiteFrame));
        var thumbnailPath = Path.Combine(directory, "four.png");

        sut.StartCapture(Guid.NewGuid(), "Cam", pipeline, Path.ChangeExtension(thumbnailPath, ".mkv"), tileCount: 4, thumbnailPath);

        using var thumbnail = await WaitForThumbnailAsync(thumbnailPath);
        thumbnail.Width.Should().Be(CellWidth * 2);
        thumbnail.Height.Should().Be(CellHeight * 2);
        IsWhite(thumbnail, column: 0, row: 0).Should().BeTrue("the top-left tile holds a frame");
        IsWhite(thumbnail, column: 1, row: 0).Should().BeTrue("the top-right tile holds a frame");
        IsWhite(thumbnail, column: 0, row: 1).Should().BeTrue("the bottom-left tile holds a frame");
        IsWhite(thumbnail, column: 1, row: 1).Should().BeTrue("the bottom-right tile holds a frame");
    }

    [Fact]
    public async Task Writes_A_Single_Tile_Thumbnail()
    {
        var pipeline = Substitute.For<IMediaPipeline>();
        pipeline.CaptureFrameAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<byte[]?>(WhiteFrame));
        var thumbnailPath = Path.Combine(directory, "single.png");

        sut.StartCapture(Guid.NewGuid(), "Cam", pipeline, Path.ChangeExtension(thumbnailPath, ".mkv"), tileCount: 1, thumbnailPath);

        using var thumbnail = await WaitForThumbnailAsync(thumbnailPath);
        thumbnail.Width.Should().Be(CellWidth);
        thumbnail.Height.Should().Be(CellHeight);
        IsWhite(thumbnail, column: 0, row: 0).Should().BeTrue();
    }

    [Fact]
    public async Task Stopping_Early_Writes_The_Frames_Captured_So_Far()
    {
        // A recording that is stopped after a second still gets a thumbnail.
        var cameraId = Guid.NewGuid();
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = Substitute.For<IMediaPipeline>();
        pipeline.CaptureFrameAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            captured.TrySetResult();
            return Task.FromResult<byte[]?>(WhiteFrame);
        });
        var thumbnailPath = Path.Combine(directory, "early.png");
        sut.CaptureInterval = TimeSpan.FromMinutes(1);

        sut.StartCapture(cameraId, "Cam", pipeline, Path.ChangeExtension(thumbnailPath, ".mkv"), tileCount: 4, thumbnailPath);
        await captured.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await WaitUntilAsync(() => !sut.IsCaptureActive(cameraId) || sut.CapturedFrameCount(cameraId) > 0);
        sut.StopCapture(cameraId);

        using var thumbnail = await WaitForThumbnailAsync(thumbnailPath);
        IsWhite(thumbnail, column: 0, row: 0).Should().BeTrue("the one captured frame is drawn");
        IsWhite(thumbnail, column: 1, row: 1).Should().BeFalse("no fourth frame was captured");
    }

    [Fact]
    public async Task Gives_Up_Without_A_Thumbnail_When_No_Frame_Ever_Arrives()
    {
        var cameraId = Guid.NewGuid();
        var pipeline = Substitute.For<IMediaPipeline>();
        pipeline.CaptureFrameAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<byte[]?>(null));
        var thumbnailPath = Path.Combine(directory, "never.png");

        sut.StartCapture(cameraId, "Cam", pipeline, Path.ChangeExtension(thumbnailPath, ".mkv"), tileCount: 4, thumbnailPath);

        await WaitUntilAsync(() => !sut.IsCaptureActive(cameraId));
        File.Exists(thumbnailPath).Should().BeFalse();
    }

    private static bool IsWhite(
        Drawing.Bitmap thumbnail,
        int column,
        int row)
    {
        var pixel = thumbnail.GetPixel((column * CellWidth) + (CellWidth / 2), (row * CellHeight) + (CellHeight / 2));
        return pixel is { R: > 200, G: > 200, B: > 200 };
    }

    private static async Task<Drawing.Bitmap> WaitForThumbnailAsync(string path)
    {
        await WaitUntilAsync(() => File.Exists(path)).ConfigureAwait(false);
        File.Exists(path).Should().BeTrue("a thumbnail should have been written to {0}", path);

        // Copy the pixels so the file is not kept open.
        using var stream = new MemoryStream(await File.ReadAllBytesAsync(path).ConfigureAwait(false));
        using var loaded = new Drawing.Bitmap(stream);
        return new Drawing.Bitmap(loaded);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20).ConfigureAwait(false);
        }
    }

    private static byte[] CreateWhitePng()
    {
        using var bitmap = new Drawing.Bitmap(64, 36);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.Clear(Drawing.Color.White);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }
}