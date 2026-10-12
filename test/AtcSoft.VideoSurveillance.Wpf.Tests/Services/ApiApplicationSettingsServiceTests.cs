namespace AtcSoft.VideoSurveillance.Wpf.Services;

public class ApiApplicationSettingsServiceTests
{
    [Fact]
    public void DiskSpaceGuard_Settings_Survive_The_Trip_To_The_Api_And_Back()
    {
        // Arrange
        var settings = ApiApplicationSettingsService.BuildCacheFrom(new ClientPreferences(), api: null);
        settings.Recording.Cleanup.EnableDiskSpaceGuard = false;
        settings.Recording.Cleanup.MinFreeSpaceMb = 5000;

        // Act
        var api = ApiApplicationSettingsService.BuildApiSettings(settings);
        var roundTripped = ApiApplicationSettingsService.BuildCacheFrom(new ClientPreferences(), api);

        // Assert
        api.EnableDiskSpaceGuard.Should().BeFalse();
        api.MinFreeSpaceMb.Should().Be(5000);
        roundTripped.Recording.Cleanup.EnableDiskSpaceGuard.Should().BeFalse();
        roundTripped.Recording.Cleanup.MinFreeSpaceMb.Should().Be(5000);
    }

    [Fact]
    public void DiskSpaceGuard_Stays_On_When_The_Api_Is_Unreachable()
    {
        // Act
        var settings = ApiApplicationSettingsService.BuildCacheFrom(new ClientPreferences(), api: null);

        // Assert
        settings.Recording.Cleanup.EnableDiskSpaceGuard.Should().BeTrue();
        settings.Recording.Cleanup.MinFreeSpaceMb.Should().Be(2048);
    }
}