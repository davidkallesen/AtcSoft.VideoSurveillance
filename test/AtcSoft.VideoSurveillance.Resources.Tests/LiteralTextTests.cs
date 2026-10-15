namespace AtcSoft.VideoSurveillance.Resources;

/// <summary>
/// A text a person reads comes from the Translations. This finds the literal
/// texts left in the views of the API clients; the few that are allowed (a
/// product name, a symbol) are listed with the reason in literal-text-allowed.txt.
/// </summary>
public sealed partial class LiteralTextTests
{
    private static readonly HashSet<string> Allowed = [.. File
        .ReadAllLines(Path.Combine(AppContext.BaseDirectory, "literal-text-allowed.txt"))
        .Where(line => line.Length > 0 && !line.StartsWith('#'))
        .Select(line => string.Join('|', line.Split('|').Take(2)))];

    [Theory]
    [InlineData("src/AtcSoft.VideoSurveillance.Wpf")]
    public void A_View_Has_No_Literal_Text(string folder)
    {
        var found = new List<string>();
        foreach (var file in RepositoryFiles.Find("*.xaml", folder))
        {
            foreach (Match match in XamlTextAttribute().Matches(File.ReadAllText(file)))
            {
                var entry = $"{RepositoryFiles.Relative(file)}|{match.Groups["text"].Value}";
                if (!Allowed.Contains(entry))
                {
                    found.Add(entry);
                }
            }
        }

        found.Should().BeEmpty("a visible text is a key in AtcSoft.VideoSurveillance.Resources");
    }

    // A visible-text attribute whose value is not a markup extension and has a letter in it.
    [GeneratedRegex(@"\b(?:Text|Header|Title|Content|LabelText|ToolTip|WatermarkText|Description|InformationText|Message|Caption)=""(?<text>(?!\{)[^""]*\p{L}[^""]*)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex XamlTextAttribute();
}