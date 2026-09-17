namespace Linksoft.VideoSurveillance.Helpers;

public class ApplicationPathsTests
{
    [Fact]
    public void DefaultPaths_Are_Not_Null_Or_Empty()
    {
        // Assert
        ApplicationPaths.DefaultLogsPath.Should().NotBeNullOrEmpty();
        ApplicationPaths.DefaultSnapshotsPath.Should().NotBeNullOrEmpty();
        ApplicationPaths.DefaultRecordingsPath.Should().NotBeNullOrEmpty();
        ApplicationPaths.DefaultSettingsPath.Should().NotBeNullOrEmpty();
        ApplicationPaths.DefaultCameraDataPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DefaultPaths_Contain_Linksoft_Folder()
    {
        // Assert
        ApplicationPaths.DefaultLogsPath.Should().Contain("Linksoft");
        ApplicationPaths.DefaultSnapshotsPath.Should().Contain("Linksoft");
        ApplicationPaths.DefaultRecordingsPath.Should().Contain("Linksoft");
    }
}