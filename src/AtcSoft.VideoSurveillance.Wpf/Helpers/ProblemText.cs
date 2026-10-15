namespace AtcSoft.VideoSurveillance.Wpf.Helpers;

/// <summary>
/// The server's own words for a request it refused.
/// </summary>
public static class ProblemText
{
    /// <summary>
    /// Gets the text to show: the message of each field a 400 names, else the
    /// problem's <c>detail</c>, else the <c>title</c> of a
    /// problem body (one made up from the status alone has the English reason phrase, so the status is said in the client's language instead), else the status.
    /// </summary>
    public static string From(EndpointResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Every generated result is an IEndpointResult with the problem already read.
        var problem = response is IEndpointResult result
            ? result.Problem
            : response.ContentObject as ProblemDetails;

        switch (problem)
        {
            case ValidationProblemDetails { Errors.Count: > 0 } validation:
                return string.Join(" ", validation.Errors.SelectMany(field => field.Value));
            case ProblemDetails { Detail: { Length: > 0 } detail }:
                return detail;
            case ProblemDetails { Title: { Length: > 0 } title } when response.ContentObject is ProblemDetails:
                return title;
            default:
                return string.Format(
                    CultureInfo.CurrentCulture,
                    Translations.ServerAnswered2,
                    (int)response.StatusCode,
                    response.StatusCode);
        }
    }
}