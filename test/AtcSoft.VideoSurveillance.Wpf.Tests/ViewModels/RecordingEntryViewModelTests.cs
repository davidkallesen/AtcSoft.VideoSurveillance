namespace AtcSoft.VideoSurveillance.Wpf.ViewModels;

using ApiRecording = global::VideoSurveillance.Generated.Recordings.Models.Recording;

public class RecordingEntryViewModelTests
{
    private static readonly Uri ApiBaseUrl = new("http://server:5000/api");

    [Fact]
    public void PlaybackUrl_Points_At_The_Recording_In_A_Day_Folder()
    {
        var vm = Build("Front Door/2026-10-12/Front Door_20261012_101500_000.mkv");

        vm.PlaybackUrl.AbsoluteUri.Should().Be(
            "http://server:5000/recordings-files/Front%20Door/2026-10-12/Front%20Door_20261012_101500_000.mkv");
    }

    [Theory]
    [InlineData("Cam #1/Cam #1_20261012_101500_000.mkv", "/recordings-files/Cam%20%231/Cam%20%231_20261012_101500_000.mkv")]
    [InlineData("50% Gate/a.mkv", "/recordings-files/50%25%20Gate/a.mkv")]
    [InlineData("Door & Yard/a.mkv", "/recordings-files/Door%20%26%20Yard/a.mkv")]
    [InlineData("A+B/a.mkv", "/recordings-files/A%2BB/a.mkv")]
    public void PlaybackUrl_Escapes_Characters_That_Would_Change_The_Url(
        string filePath,
        string expectedPath)
    {
        var vm = Build(filePath);

        // A '#' would otherwise start a fragment and cut the path short.
        vm.PlaybackUrl.Fragment.Should().BeEmpty();
        vm.PlaybackUrl.Query.Should().BeEmpty();
        vm.PlaybackUrl.AbsolutePath.Should().Be(expectedPath);
    }

    private static RecordingEntryViewModel Build(string filePath)
        => new(
            new ApiRecording(
                Id: Guid.NewGuid(),
                CameraId: Guid.NewGuid(),
                CameraName: "Cam",
                FilePath: filePath,
                StartedAt: DateTimeOffset.UnixEpoch,
                Duration: string.Empty,
                FileSizeBytes: 0,
                HasThumbnail: false),
            ApiBaseUrl);
}