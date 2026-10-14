namespace AtcSoft.VideoSurveillance.Api.Host;

/// <summary>
/// The limits in the specification are checked before a handler runs: a
/// value outside them is a 400 validation problem that names the field.
/// </summary>
[Collection(ApiHostTestGroup.Name)]
public sealed class ApiHostValidationTests(ApiHostFixture host)
{
    [Fact]
    public async Task A_Camera_Without_A_Name_And_With_A_Port_Out_Of_Range_Is_Refused_By_Field()
    {
        using var client = host.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/cameras",
            new { displayName = string.Empty, ipAddress = "192.0.2.20", port = 70000 },
            TestContext.Current.CancellationToken);

        var errors = await ReadErrorsAsync(response);
        errors.TryGetProperty("displayName", out _).Should().BeTrue();
        errors.TryGetProperty("port", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_Settings_Update_With_A_Timeout_Of_Zero_Is_Refused_And_Changes_Nothing()
    {
        using var client = host.CreateClient();
        var before = await client.GetFromJsonAsync<JsonElement>("/settings", TestContext.Current.CancellationToken);

        using var response = await client.PutAsJsonAsync(
            "/settings",
            new { connectionTimeoutSeconds = 0, showOverlayTime = !before.GetProperty("showOverlayTime").GetBoolean() },
            TestContext.Current.CancellationToken);

        var errors = await ReadErrorsAsync(response);
        errors.TryGetProperty("connectionTimeoutSeconds", out _).Should().BeTrue();
        var after = await client.GetFromJsonAsync<JsonElement>("/settings", TestContext.Current.CancellationToken);
        after.GetRawText().Should().Be(before.GetRawText());
    }

    [Fact]
    public async Task A_Layout_Without_A_Name_And_Without_Rows_Is_Refused_By_Field()
    {
        using var client = host.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/layouts",
            new { name = string.Empty, rows = 0, columns = 2 },
            TestContext.Current.CancellationToken);

        var errors = await ReadErrorsAsync(response);
        errors.TryGetProperty("name", out _).Should().BeTrue();
        errors.TryGetProperty("rows", out _).Should().BeTrue();
    }

    private static async Task<JsonElement> ReadErrorsAsync(
        HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return problem.GetProperty("errors");
    }
}