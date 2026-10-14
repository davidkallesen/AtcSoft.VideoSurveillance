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
        var roundTripped = ApiApplicationSettingsService.BuildCacheFrom(new ClientPreferences(), AsServerAnswer(api));

        // Assert
        api.EnableDiskSpaceGuard.Should().BeFalse();
        api.MinFreeSpaceMb.Should().Be(5000);
        roundTripped.Recording.Cleanup.EnableDiskSpaceGuard.Should().BeFalse();
        roundTripped.Recording.Cleanup.MinFreeSpaceMb.Should().Be(5000);
    }

    [Fact]
    public void The_Grid_Size_Is_Kept_On_This_Computer_Because_The_Server_Has_No_Place_For_It()
    {
        // Each desktop has its own screen; the size of its grid is no
        // setting of the server.
        var general = new AtcSoft.VideoSurveillance.Models.Settings.GeneralSettings { GridSize = 2, StartRibbonCollapsed = true, ThemeBase = "Light" };

        var preferences = ClientPreferences.From(general);
        var loaded = ApiApplicationSettingsService.BuildCacheFrom(preferences, api: null);

        preferences.GridSize.Should().Be(2);
        loaded.General.GridSize.Should().Be(2);
        loaded.General.StartRibbonCollapsed.Should().BeTrue();
        loaded.General.ThemeBase.Should().Be("Light");
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

    /// <summary>
    /// What the server answers to the update: the same settings, as it writes them.
    /// </summary>
    private static AppSettings AsServerAnswer(UpdateSettingsRequest update)
        => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(update))!;
}