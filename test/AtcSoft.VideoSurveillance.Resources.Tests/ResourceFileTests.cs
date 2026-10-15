namespace AtcSoft.VideoSurveillance.Resources;

public sealed class ResourceFileTests
{
    private static readonly IReadOnlyDictionary<string, string> Neutral = ResxFile.Read(string.Empty);

    [Theory]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    public void Every_Text_Is_Translated(string culture)
    {
        var translated = ResxFile.Read(culture);

        Neutral.Keys.Except(translated.Keys, StringComparer.Ordinal).Should().BeEmpty($"every key needs a {culture} text");
        translated.Keys.Except(Neutral.Keys, StringComparer.Ordinal).Should().BeEmpty($"{culture} has no keys of its own");
    }

    [Theory]
    [InlineData("")]
    [InlineData("da-DK")]
    [InlineData("de-DE")]
    [InlineData("en-GB")]
    public void No_Text_Is_Empty(string culture)
        => ResxFile.Read(culture)
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => entry.Key)
            .Should().BeEmpty();

    [Fact]
    public void British_English_Only_Overrides()
    {
        var british = ResxFile.Read("en-GB");

        british.Should().NotBeEmpty();
        british.Keys.Except(Neutral.Keys, StringComparer.Ordinal).Should().BeEmpty("en-GB only changes texts that exist");
        british.Where(entry => entry.Value == Neutral[entry.Key]).Select(entry => entry.Key)
            .Should().BeEmpty("a text equal to US English belongs only in the neutral file");
    }
}