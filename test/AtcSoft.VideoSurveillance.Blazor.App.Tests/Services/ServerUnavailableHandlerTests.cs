namespace AtcSoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// A server that cannot be reached must look like a server that answered
/// "unavailable": the pages show their own message for a failed call, but an
/// exception that escapes takes the whole page down.
/// </summary>
public sealed class ServerUnavailableHandlerTests
{
    private static readonly Uri Address = new("http://server.test/cameras");

    [Fact]
    public async Task A_Server_That_Cannot_Be_Reached_Becomes_An_Unavailable_Answer()
    {
        using var client = CreateClient(_ => throw new HttpRequestException("TypeError: Failed to fetch"));

        using var response = await client.GetAsync(Address, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task A_Call_The_Resilience_Handler_Gave_Up_On_Becomes_An_Unavailable_Answer()
    {
        // A timeout and an open circuit are reported with exceptions of their own.
        using var timedOut = CreateClient(_ => throw new Polly.Timeout.TimeoutRejectedException("too slow"));
        using var circuitOpen = CreateClient(_ => throw new Polly.CircuitBreaker.BrokenCircuitException("too many failures"));

        using var first = await timedOut.GetAsync(Address, TestContext.Current.CancellationToken);
        using var second = await circuitOpen.GetAsync(Address, TestContext.Current.CancellationToken);

        first.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
        second.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task A_Request_That_Timed_Out_Becomes_An_Unavailable_Answer()
    {
        using var client = CreateClient(_ => throw new TaskCanceledException("The request timed out."));

        using var response = await client.GetAsync(Address, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task A_Call_The_Page_Cancelled_Is_Still_Cancelled()
    {
        using var cancelled = new CancellationTokenSource();
        using var client = CreateClient(token =>
        {
            cancelled.Cancel();
            token.ThrowIfCancellationRequested();
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });

        var act = async () => await client.GetAsync(Address, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task An_Answer_From_The_Server_Is_Passed_On_Untouched()
    {
        using var client = CreateClient(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));

        using var response = await client.GetAsync(Address, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The client owns the handler and disposes it.")]
    private static HttpClient CreateClient(
        Func<CancellationToken, HttpResponseMessage> answer)
        => new(new ServerUnavailableHandler { InnerHandler = new StubHandler(answer) });

    private sealed class StubHandler(Func<CancellationToken, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(answer(cancellationToken));
    }
}