namespace Linksoft.VideoSurveillance.Models.Settings;

public class GeneralSettingsTests
{
    [Fact]
    public void New_Instance_Has_Expected_Defaults()
    {
        // Act
        var settings = new GeneralSettings();

        // Assert
        settings.ThemeBase.Should().Be("Dark");
        settings.ThemeAccent.Should().Be("Blue");
        settings.Language.Should().Be("1033");
        settings.ConnectCamerasOnStartup.Should().BeTrue();
        settings.StartMaximized.Should().BeFalse();
        settings.StartRibbonCollapsed.Should().BeFalse();
    }

    [Fact]
    public void Clone_Creates_Independent_Copy()
    {
        // Arrange
        var original = new GeneralSettings
        {
            ThemeBase = "Light",
            ThemeAccent = "Red",
            Language = "1030",
        };

        // Act
        var clone = original.Clone();

        // Assert
        clone.ThemeBase.Should().Be("Light");
        clone.ThemeAccent.Should().Be("Red");
        clone.Language.Should().Be("1030");

        // Verify independence
        clone.ThemeBase = "Dark";
        original.ThemeBase.Should().Be("Light");
    }
}