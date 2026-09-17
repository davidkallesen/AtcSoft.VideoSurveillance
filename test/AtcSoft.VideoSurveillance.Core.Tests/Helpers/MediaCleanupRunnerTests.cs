namespace Linksoft.VideoSurveillance.Helpers;

public sealed class MediaCleanupRunnerTests : IDisposable
{
    private static readonly HashSet<string> RecordingExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4" };

    private static readonly HashSet<string> EmptySkipSet =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string tempDir;

    public MediaCleanupRunnerTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "MediaCleanupRunner_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void CleanDirectory_OldFile_IsDeleted()
    {
        // Arrange
        var path = WriteFile("Cam_old.mkv", "old", DateTime.Now.AddDays(-31));
        var cutoff = DateTime.Now.AddDays(-30);

        // Act
        var result = MediaCleanupRunner.CleanDirectory(
            tempDir,
            RecordingExtensions,
            cutoff,
            EmptySkipSet,
            deleteCompanionThumbnail: false);

        // Assert
        result.DeletedFiles.Should().Contain(path);
        File.Exists(path).Should().BeFalse();
        result.BytesFreed.Should().Be(3);
    }

    [Fact]
    public void CleanDirectory_RecentFile_IsKept()
    {
        // Arrange
        var path = WriteFile("Cam_new.mkv", "new", DateTime.Now);
        var cutoff = DateTime.Now.AddDays(-30);

        // Act
        var result = MediaCleanupRunner.CleanDirectory(
            tempDir,
            RecordingExtensions,
            cutoff,
            EmptySkipSet,
            deleteCompanionThumbnail: false);

        // Assert
        result.DeletedFiles.Should().BeEmpty();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void CleanDirectory_FileInSkipSet_IsNeverDeleted()
    {
        // Arrange — looks old, but is the active recording
        var path = WriteFile("Cam_active.mkv", "active", DateTime.Now.AddDays(-31));
        var cutoff = DateTime.Now.AddDays(-30);
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(path),
        };

        // Act
        var result = MediaCleanupRunner.CleanDirectory(
            tempDir,
            RecordingExtensions,
            cutoff,
            skip,
            deleteCompanionThumbnail: false);

        // Assert
        result.DeletedFiles.Should().BeEmpty();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void CleanDirectory_DeleteCompanionThumbnail_RemovesPng()
    {
        // Arrange — old .mkv with a sibling .png
        var mkv = WriteFile("Cam_old.mkv", "video", DateTime.Now.AddDays(-31));
        var png = Path.ChangeExtension(mkv, ".png");
        File.WriteAllText(png, "thumb");
        File.SetLastWriteTime(png, DateTime.Now.AddDays(-31));

        // Act
        var result = MediaCleanupRunner.CleanDirectory(
            tempDir,
            RecordingExtensions,
            DateTime.Now.AddDays(-30),
            EmptySkipSet,
            deleteCompanionThumbnail: true);

        // Assert
        File.Exists(mkv).Should().BeFalse();
        File.Exists(png).Should().BeFalse();
        result.DeletedFiles.Should().Contain(mkv);
        result.DeletedThumbnails.Should().Contain(png);
        result.BytesFreed.Should().Be(5 + 5); // "video" + "thumb"
    }

    [Fact]
    public void CleanDirectory_NonMatchingExtension_IsKept()
    {
        // Arrange — old .txt file shouldn't be touched
        var path = WriteFile("Cam.txt", "text", DateTime.Now.AddDays(-31));

        // Act
        var result = MediaCleanupRunner.CleanDirectory(
            tempDir,
            RecordingExtensions,
            DateTime.Now.AddDays(-30),
            EmptySkipSet,
            deleteCompanionThumbnail: false);

        // Assert
        result.DeletedFiles.Should().BeEmpty();
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void CleanDirectory_RootMissing_ReturnsEmptyResultWithoutErrors()
    {
        // Arrange
        var missing = Path.Combine(tempDir, "does-not-exist");

        // Act
        var result = MediaCleanupRunner.CleanDirectory(
            missing,
            RecordingExtensions,
            DateTime.Now,
            EmptySkipSet,
            deleteCompanionThumbnail: false);

        // Assert
        result.DeletedFiles.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CleanDirectory_RecursivelyFindsFilesInSubdirs()
    {
        // Arrange
        var sub = Path.Combine(tempDir, "Cam1");
        Directory.CreateDirectory(sub);
        var path = Path.Combine(sub, "Cam1_old.mkv");
        File.WriteAllText(path, "x");
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-31));

        // Act
        var result = MediaCleanupRunner.CleanDirectory(
            tempDir,
            RecordingExtensions,
            DateTime.Now.AddDays(-30),
            EmptySkipSet,
            deleteCompanionThumbnail: false);

        // Assert
        result.DeletedFiles.Should().Contain(path);
    }

    [Fact]
    public void RemoveEmptyDirectoriesBelow_RemovesEmptyChildren()
    {
        // Arrange
        var emptyChild = Path.Combine(tempDir, "empty");
        Directory.CreateDirectory(emptyChild);

        // Act
        var result = MediaCleanupRunner.RemoveEmptyDirectoriesBelow(tempDir);

        // Assert
        result.RemovedDirectories.Should().Contain(emptyChild);
        Directory.Exists(emptyChild).Should().BeFalse();
        Directory.Exists(tempDir).Should().BeTrue(); // root preserved
    }

    [Fact]
    public void RemoveEmptyDirectoriesBelow_KeepsNonEmptyChildren()
    {
        // Arrange
        var nonEmptyChild = Path.Combine(tempDir, "kept");
        Directory.CreateDirectory(nonEmptyChild);
        File.WriteAllText(Path.Combine(nonEmptyChild, "file.mkv"), "x");

        // Act
        var result = MediaCleanupRunner.RemoveEmptyDirectoriesBelow(tempDir);

        // Assert
        result.RemovedDirectories.Should().BeEmpty();
        Directory.Exists(nonEmptyChild).Should().BeTrue();
    }

    [Fact]
    public void RemoveEmptyDirectoriesBelow_DeepestFirst()
    {
        // Arrange — empty leaf inside an otherwise-empty parent: both should go
        var parent = Path.Combine(tempDir, "outer");
        var leaf = Path.Combine(parent, "inner");
        Directory.CreateDirectory(leaf);

        // Act
        var result = MediaCleanupRunner.RemoveEmptyDirectoriesBelow(tempDir);

        // Assert
        result.RemovedDirectories.Should().Contain(leaf);
        result.RemovedDirectories.Should().Contain(parent);
    }

    [Fact]
    public void CleanDirectory_NullPath_Throws()
    {
        // Act
        var act = () => MediaCleanupRunner.CleanDirectory(
            rootPath: null!,
            RecordingExtensions,
            DateTime.Now,
            EmptySkipSet,
            deleteCompanionThumbnail: false);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    private string WriteFile(
        string relativePath,
        string contents,
        DateTime lastWrite)
    {
        var path = Path.Combine(tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        File.SetLastWriteTime(path, lastWrite);
        return path;
    }
}