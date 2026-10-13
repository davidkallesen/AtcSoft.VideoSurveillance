namespace AtcSoft.VideoSurveillance.Api.Services;

public sealed class CachedRecordingDurationProviderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "atcsoft-durations-" + Guid.NewGuid().ToString("N"));
    private readonly string file;
    private int probes;

    public CachedRecordingDurationProviderTests()
    {
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, "a.mp4");
        File.WriteAllBytes(file, [1, 2, 3]);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_File_Is_Probed_Once_However_Often_It_Is_Asked_For()
    {
        var sut = Create(TimeSpan.FromSeconds(30));

        sut.GetDuration(file).Should().Be(TimeSpan.FromSeconds(30));
        sut.GetDuration(file).Should().Be(TimeSpan.FromSeconds(30));

        probes.Should().Be(1);
    }

    [Fact]
    public void A_File_That_Changed_Is_Probed_Again()
    {
        var sut = Create(TimeSpan.FromSeconds(30));
        sut.GetDuration(file);

        File.WriteAllBytes(file, [1, 2, 3, 4, 5]);
        sut.GetDuration(file);

        probes.Should().Be(2);
    }

    [Fact]
    public void A_Length_That_Cannot_Be_Read_Is_Null_And_Not_Remembered()
    {
        // A file that is still being finished may be readable a moment later.
        var sut = Create(TimeSpan.Zero);

        sut.GetDuration(file).Should().BeNull();
        sut.GetDuration(file).Should().BeNull();

        probes.Should().Be(2);
    }

    [Fact]
    public void A_File_That_Is_Not_There_Has_No_Length()
    {
        var sut = Create(TimeSpan.FromSeconds(30));

        sut.GetDuration(Path.Combine(directory, "gone.mp4")).Should().BeNull();

        probes.Should().Be(0);
    }

    [Fact]
    public void A_Probe_That_Throws_Gives_No_Length()
    {
        var sut = new CachedRecordingDurationProvider(_ => throw new InvalidOperationException("broken file"));

        sut.GetDuration(file).Should().BeNull();
    }

    private CachedRecordingDurationProvider Create(TimeSpan result)
        => new(_ =>
        {
            probes++;
            return result;
        });
}