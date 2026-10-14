namespace AtcSoft.VideoSurveillance.Wpf.Extensions;

public static class ApiHttpClientBuilderExtensions
{
    /// <summary>
    /// The resilience the server's API needs, the same as in the web client.
    /// </summary>
    public static IHttpClientBuilder AddApiResilience(
        this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            // Some calls are slow by nature: starting a recording waits for the
            // camera to connect, and saving a changed folder layout moves the
            // recordings before it answers. The defaults (10 s per attempt)
            // would give up on them and try again.
            options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(5);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);

            // Only reads are repeated. Repeating a save, a start or a delete
            // that did reach the server does it twice.
            options.Retry.DisableForUnsafeHttpMethods();
        });

        return builder;
    }
}