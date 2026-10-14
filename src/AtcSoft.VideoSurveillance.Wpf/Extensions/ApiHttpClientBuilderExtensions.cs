namespace AtcSoft.VideoSurveillance.Wpf.Extensions;

public static class ApiHttpClientBuilderExtensions
{
    /// <summary>
    /// The resilience the server's API declares (x-retry-* in the
    /// specification), the same as in the web client: two minutes per attempt
    /// for the calls that are slow by nature, and only reads are repeated.
    /// </summary>
    public static IHttpClientBuilder AddApiResilience(
        this IHttpClientBuilder builder)
        => builder.AddApiResilience(ResiliencePolicies.Standard);
}