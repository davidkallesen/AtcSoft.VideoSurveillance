namespace AtcSoft.VideoSurveillance.Storybook;

/// <summary>
/// A storybook is read with errors that say where they are, so a scene written by hand is
/// fixed quickly.
/// </summary>
public sealed class StorybookReaderTests
{
    [Fact]
    public void Every_Scene_In_The_Catalogue_Reads()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Scenes"), "*.storybook.json"))
        {
            var scene = StorybookReader.ReadFile(file);

            scene.Name.Should().NotBeNullOrWhiteSpace(Path.GetFileName(file));
        }
    }

    [Fact]
    public void Key_Points_Out_Of_Order_Are_Named()
    {
        const string json = """
            { "version": 1, "name": "x",
              "objects": [ { "id": "car", "kind": "car", "size": [0.2, 0.1],
                             "path": [ { "t": 5, "at": [0, 0.5] }, { "t": 3, "at": [1, 0.5] } ] } ] }
            """;

        var read = () => StorybookReader.Read(json);

        read.Should().Throw<StorybookException>().WithMessage("*objects[0].path[1].t*");
    }

    [Fact]
    public void An_Unknown_Kind_Is_Named()
    {
        const string json = """
            { "version": 1, "name": "x",
              "objects": [ { "id": "u", "kind": "unicorn", "size": [0.2, 0.1], "path": [ { "t": 1, "at": [0.5, 0.5] } ] } ] }
            """;

        var read = () => StorybookReader.Read(json);

        read.Should().Throw<StorybookException>().WithMessage("*objects[0].kind*unicorn*");
    }

    [Fact]
    public void A_Misspelt_Property_Is_Named()
    {
        const string json = """{ "version": 1, "name": "x", "vidoe": { } }""";

        var read = () => StorybookReader.Read(json);

        read.Should().Throw<StorybookException>().WithMessage("*vidoe*");
    }

    [Fact]
    public void A_Span_About_An_Object_That_Does_Not_Exist_Is_Named()
    {
        const string json = """
            { "version": 1, "name": "x", "expect": { "spans": [ { "object": "ghost", "state": "dontCare" } ] } }
            """;

        var read = () => StorybookReader.Read(json);

        read.Should().Throw<StorybookException>().WithMessage("*expect.spans[0].object*ghost*");
    }
}