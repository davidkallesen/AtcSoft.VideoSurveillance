namespace AtcSoft.VideoSurveillance.Api.Handlers;

/// <summary>
/// Since generator v3 every error a handler returns is a ProblemDetails
/// (<see cref="ProblemHttpResult"/>), whatever factory made it, so a test
/// asserts the status and reads the message from <c>Detail</c>.
/// </summary>
internal static class ProblemResultAssertions
{
    public static ProblemDetails ShouldBeProblem(
        this IResult result,
        int statusCode)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(statusCode);
        return problem.ProblemDetails;
    }
}