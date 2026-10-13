namespace AtcSoft.VideoSurveillance.Wpf.ViewModels;

/// <summary>
/// A tile without a picture says why. "No stream" for a USB camera that
/// was pulled out sends the user looking at the server; the cable is where
/// to look.
/// </summary>
public sealed class CameraTileViewModelPlaceholderTests
{
    [Fact]
    public async Task A_Tile_Without_A_Picture_Says_No_Stream()
    {
        await using var hub = new SurveillanceHubService("http://localhost");
        using var tile = new CameraTileViewModel(Substitute.For<IVideoPlayerFactory>(), hub, "http://localhost");

        tile.PlaceholderText.Should().Be("No Stream");
    }

    [Fact]
    public async Task A_Usb_Camera_That_Was_Pulled_Out_Says_So_And_Says_No_Stream_Again_When_It_Is_Back()
    {
        await using var hub = new SurveillanceHubService("http://localhost");
        using var tile = new CameraTileViewModel(Substitute.For<IVideoPlayerFactory>(), hub, "http://localhost")
        {
            UsbDeviceId = "usb#1",
        };
        var changed = new List<string?>();
        tile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        tile.IsDeviceUnplugged = true;

        tile.PlaceholderText.Should().Be(AtcSoft.VideoSurveillance.Wpf.Core.Resources.Translations.UsbDeviceUnplugged);
        changed.Should().Contain(nameof(CameraTileViewModel.PlaceholderText), "the tile has to show the new text");

        tile.IsDeviceUnplugged = false;

        tile.PlaceholderText.Should().Be("No Stream");
    }
}