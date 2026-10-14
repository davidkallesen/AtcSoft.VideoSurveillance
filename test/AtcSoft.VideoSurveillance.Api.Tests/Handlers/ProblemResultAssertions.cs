namespace AtcSoft.VideoSurveillance.Api.Handlers;

/// <summary>
/// Since generator v3 every error a handler returns is a ProblemDetails
/// (<see cref="ProblemHttpResult"/>), whatever factory made it, so a test
/// asserts the status and reads the message from <c>Detail</c>. An error
/// with response headers (Retry-After) comes wrapped, so the status and the
/// body are read through the result interfaces both kinds implement.
/// </summary>
internal static class ProblemResultAssertions
{
    public static ProblemDetails ShouldBeProblem(
        this IResult result,
        int statusCode)
    {
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(statusCode);
        return result.Should().BeAssignableTo<IValueHttpResult>().Which.Value
            .Should().BeAssignableTo<ProblemDetails>().Subject;
    }
}