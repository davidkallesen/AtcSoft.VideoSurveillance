namespace AtcSoft.VideoSurveillance.Api.Services;

public sealed class ServerMediaCleanupServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "atcsoft-server-cleanup-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_Cleanup_Pass_Removes_Thumbnails_Whose_Recording_Is_Gone()
    {
        // Arrange — one recording with its thumbnail, and one thumbnail left
        // behind by a recording that was deleted by hand
        var recordings = Path.Combine(root, "recordings");
        var video = Touch(Path.Combine(recordings, "Cam", "kept.mkv"));
        var thumbnail = Touch(Path.Combine(recordings, "Cam", "kept.png"));
        var orphan = Touch(Path.Combine(recordings, "Cam", "gone.png"));

        using var sut = CreateService(recordings, snapshotPath: Path.Combine(root, "snapshots"));

        // Act
        await sut.DoWorkAsync(CancellationToken.None);

        // Assert
        File.Exists(orphan).Should().BeFalse();
        File.Exists(video).Should().BeTrue();
        File.Exists(thumbnail).Should().BeTrue();
    }

    [Fact]
    public async Task A_Cleanup_Pass_Keeps_Snapshots_Stored_Inside_The_Recording_Folder()
    {
        // Arrange — snapshots are images without a recording
        var recordings = Path.Combine(root, "recordings");
        var snapshots = Path.Combine(recordings, "snapshots");
        var snapshot = Touch(Path.Combine(snapshots, "Cam_20260101_101500.png"));

        using var sut = CreateService(recordings, snapshots);

        // Act
        await sut.DoWorkAsync(CancellationToken.None);

        // Assert
        File.Exists(snapshot).Should().BeTrue();
    }

    private static ServerMediaCleanupService CreateService(
        string recordingPath,
        string snapshotPath)
    {
        var settings = Substitute.For<IApplicationSettingsService>();
        settings.Recording.Returns(new RecordingSettings
        {
            RecordingPath = recordingPath,
            Cleanup =
            {
                Schedule = MediaCleanupSchedule.OnStartupAndPeriodically,
                RecordingRetentionDays = 30,
                EnableDiskSpaceGuard = false,
            },
        });
        settings.CameraDisplay.Returns(new CameraDisplayAppSettings { SnapshotPath = snapshotPath });

        var recordingService = Substitute.For<IRecordingService>();
        recordingService.GetActiveSessions().Returns([]);

        return new ServerMediaCleanupService(
            NullLogger<ServerMediaCleanupService>.Instance,
            new ServerMediaCleanupServiceOptions(),
            Substitute.For<IBackgroundServiceHealthService>(),
            settings,
            recordingService);
    }

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");

        // Recent enough to survive retention, old enough to be past the grace period.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));
        return path;
    }
}