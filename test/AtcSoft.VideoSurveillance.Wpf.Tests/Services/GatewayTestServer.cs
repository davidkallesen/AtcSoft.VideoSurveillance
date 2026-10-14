namespace AtcSoft.VideoSurveillance.Wpf.Services;

/// <summary>
/// A <see cref="GatewayService"/> on the real generated endpoints and Atc.Rest.Client,
/// whose server answers every request with one response.
/// </summary>
internal sealed class GatewayTestServer : IDisposable
{
    private readonly ServiceProvider services;

    public GatewayTestServer(
        HttpStatusCode status,
        HttpContent content,
        string mediaType)
    {
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        var collection = new ServiceCollection();
        collection
            .AddHttpClient(Constants.HttpClientName, c => c.BaseAddress = new Uri("http://server.test"))
            .ConfigurePrimaryHttpMessageHandler(() => new AnswerHandler(status, content));
        collection.AddVideoSurveillanceEndpoints();
        services = collection.BuildServiceProvider();
        Gateway = ActivatorUtilities.CreateInstance<GatewayService>(services);
    }

    public GatewayService Gateway { get; }

    public void Dispose()
        => services.Dispose();

    private sealed class AnswerHandler(
        HttpStatusCode status,
        HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = content, RequestMessage = request });
    }
}