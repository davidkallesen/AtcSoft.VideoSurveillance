namespace AtcSoft.VideoSurveillance.Wpf.Core.Dialogs;

using IWpfCoreSettingsService = AtcSoft.VideoSurveillance.Wpf.Core.Services.IApplicationSettingsService;
using RecordingEntry = AtcSoft.VideoSurveillance.Wpf.Core.Models.RecordingEntry;

/// <summary>
/// Playing a recording leaves the browser as it was, so the next recording
/// can be picked without opening the browser and filtering again.
/// </summary>
public sealed class RecordingsBrowserDialogTests : IDisposable
{
    private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "atcsoft-browser-dialog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (System.IO.Directory.Exists(root))
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_Dialog_Stays_Open_With_Its_Filter_And_Selection_When_A_Recording_Is_Played()
    {
        // Arrange
        Touch("Front Door", "Front Door_20261012_101500_000.mkv");
        Touch("Garden", "Garden_20261012_220000_000.mkv");
        Touch("Garden", "Garden_20261012_230000_000.mkv");

        var settingsService = Substitute.For<IWpfCoreSettingsService>();
        settingsService.Recording.Returns(new RecordingSettings { RecordingPath = root });

        string? played = null;
        var closedByPlay = true;
        string? filterAfterPlay = null;
        string? selectedAfterPlay = null;

        // Act
        var error = WpfTestThread.Run(() =>
        {
            var viewModel = new RecordingsBrowserDialogViewModel(settingsService)
            {
                SelectedCameraFilter = "Garden",
            };

            RecordingEntry? playedRecording = null;
            var closed = false;
            var dialog = new RecordingsBrowserDialog(viewModel, recording => playedRecording = recording);
            dialog.Closed += (_, _) => closed = true;

            viewModel.SelectedRecording = viewModel.FilteredRecordings.Last();
            viewModel.PlayCommand.Execute(parameter: null);

            played = playedRecording?.FileName;
            closedByPlay = closed;
            filterAfterPlay = viewModel.SelectedCameraFilter;
            selectedAfterPlay = viewModel.SelectedRecording?.FileName;

            dialog.Close();
        });

        // Assert
        error.Should().BeNull();
        played.Should().Be("Garden_20261012_220000_000.mkv");
        closedByPlay.Should().BeFalse("the browser is still there when playback ends");
        filterAfterPlay.Should().Be("Garden");
        selectedAfterPlay.Should().Be("Garden_20261012_220000_000.mkv");
    }

    private void Touch(params string[] segments)
    {
        var path = System.IO.Path.Combine([root, .. segments]);
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, "x");
    }
}