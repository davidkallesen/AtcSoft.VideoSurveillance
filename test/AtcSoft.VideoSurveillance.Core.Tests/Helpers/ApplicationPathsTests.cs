namespace AtcSoft.VideoSurveillance.Helpers;

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
    public void DefaultPaths_Contain_AtcSoft_Folder()
    {
        // Assert
        ApplicationPaths.DefaultLogsPath.Should().Contain("AtcSoft");
        ApplicationPaths.DefaultSnapshotsPath.Should().Contain("AtcSoft");
        ApplicationPaths.DefaultRecordingsPath.Should().Contain("AtcSoft");
    }

    [Fact]
    public void DefaultPaths_Do_Not_Contain_The_Pre_Rename_Folder()
    {
        // Assert
        ApplicationPaths.DefaultLogsPath.Should().NotContain("Linksoft");
        ApplicationPaths.DefaultSnapshotsPath.Should().NotContain("Linksoft");
        ApplicationPaths.DefaultRecordingsPath.Should().NotContain("Linksoft");
    }
}