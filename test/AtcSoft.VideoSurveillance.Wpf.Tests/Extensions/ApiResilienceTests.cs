namespace AtcSoft.VideoSurveillance.Wpf.Extensions;

public sealed class ApiResilienceTests
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_Change_That_Fails_Is_Sent_Once(string method)
    {
        using var handler = new CountingHandler(HttpStatusCode.ServiceUnavailable);
        await using var services = BuildServices(handler);
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(Constants.HttpClientName);
        using var request = new HttpRequestMessage(new HttpMethod(method), "cameras/1/recording");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_Read_That_Fails_Is_Tried_Again()
    {
        using var handler = new CountingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        await using var services = BuildServices(handler);
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(Constants.HttpClientName);

        using var response = await client.GetAsync(new Uri("cameras", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task A_Slow_Call_Gets_Minutes_Not_Seconds()
    {
        using var handler = new CountingHandler(HttpStatusCode.OK);
        await using var services = BuildServices(handler);

        var options = services
            .GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get($"{Constants.HttpClientName}-standard");

        options.AttemptTimeout.Timeout.Should().Be(TimeSpan.FromMinutes(2));
        options.TotalRequestTimeout.Timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    private static ServiceProvider BuildServices(CountingHandler handler)
    {
        var collection = new ServiceCollection();
        collection
            .AddHttpClient(Constants.HttpClientName, c => c.BaseAddress = new Uri("http://server.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddApiResilience();
        return collection.BuildServiceProvider();
    }

    private sealed class CountingHandler(
        params HttpStatusCode[] answers) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var status = answers[Math.Min(Calls, answers.Length - 1)];
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request });
        }
    }
}