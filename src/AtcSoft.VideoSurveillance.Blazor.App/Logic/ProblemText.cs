namespace AtcSoft.VideoSurveillance.Blazor.App.Logic;

/// <summary>
/// The server's own words for a request it refused.
/// </summary>
public static class ProblemText
{
    /// <summary>
    /// Gets the text to show: the message of each field a 400 names, else the
    /// problem's <c>detail</c>, else its <c>title</c>, else the status (also for
    /// the answer <see cref="Services.ServerUnavailableHandler"/> makes up when
    /// the server cannot be reached).
    /// </summary>
    public static string From(EndpointResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        switch (response.ContentObject)
        {
            case ValidationProblemDetails { Errors.Count: > 0 } validation:
                return string.Join(" ", validation.Errors.SelectMany(field => field.Value));
            case ProblemDetails { Detail: { Length: > 0 } detail }:
                return detail;
            case ProblemDetails { Title: { Length: > 0 } title }:
                return title;
            default:
                return string.Format(
                    CultureInfo.CurrentCulture,
                    Translations.ServerAnswered2,
                    (int)response.StatusCode,
                    response.StatusCode);
        }
    }

    /// <summary>
    /// Puts the page's own sentence and the server's reason together.
    /// </summary>
    public static string WithReason(
        string sentence,
        string? reason)
        => string.IsNullOrWhiteSpace(reason) ? sentence : $"{sentence} {reason}";
}