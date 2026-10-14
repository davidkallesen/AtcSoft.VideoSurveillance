namespace AtcSoft.VideoSurveillance.Api.Handlers;

/// <summary>
/// A create answers 201 with its address in Location. The generated result
/// that adds the header shows neither its status nor its value, so a test
/// runs it and reads the response.
/// </summary>
internal static class CreatedResultAssertions
{
    public static async Task<T> ShouldBeCreatedAsync<T>(
        this IResult result,
        string locationPrefix)
    {
        var response = await result.ExecuteAsync();
        response.StatusCode.Should().Be(StatusCodes.Status201Created);
        response.Headers.Location.ToString().Should().StartWith(locationPrefix);

        response.Body.Position = 0;
        var value = await JsonSerializer.DeserializeAsync<T>(response.Body, JsonSerializerOptions.Web);
        return value.Should().NotBeNull().And.BeAssignableTo<T>().Subject;
    }
}