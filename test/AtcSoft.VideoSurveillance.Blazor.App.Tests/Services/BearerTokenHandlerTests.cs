namespace AtcSoft.VideoSurveillance.Blazor.App.Services;

/// <summary>
/// Every call to the server carries the access token; one that is about to run
/// out is renewed first, and a read the server refused for its token is sent
/// again after a renewal.
/// </summary>
public sealed class BearerTokenHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-15T12:00:00Z", CultureInfo.InvariantCulture);

    private readonly Microsoft.Extensions.Time.Testing.FakeTimeProvider time = new(Now);
    private readonly BrowserSession session;

    public BearerTokenHandlerTests()
        => session = new BrowserSession(time);

    public void Dispose()
        => session.Dispose();

    [Fact]
    public async Task A_Call_Carries_The_Access_Token()
    {
        session.Apply(Session("first", expiresIn: 3600));
        var sent = new List<HttpRequestMessage>();
        using var invoker = CreateInvoker(request =>
        {
            sent.Add(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await CallAsync(invoker, HttpMethod.Get, "http://server.test/cameras");

        sent.Single().Headers.Authorization!.ToString().Should().Be("Bearer first");
    }

    [Fact]
    public async Task A_Token_About_To_Run_Out_Is_Renewed_Before_The_Call()
    {
        session.Apply(Session("old", expiresIn: 3600));
        session.Renew = _ =>
        {
            session.Apply(Session("new", expiresIn: 3600));
            return Task.FromResult(true);
        };
        time.Advance(TimeSpan.FromMinutes(59.5));
        var sent = new List<string?>();
        using var invoker = CreateInvoker(request =>
        {
            sent.Add(request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await CallAsync(invoker, HttpMethod.Get, "http://server.test/cameras");

        sent.Should().Equal("new");
    }

    [Fact]
    public async Task A_Read_Refused_For_Its_Token_Is_Sent_Again_After_A_Renewal()
    {
        session.Apply(Session("stale", expiresIn: 3600));
        session.Renew = _ =>
        {
            session.Apply(Session("fresh", expiresIn: 3600));
            return Task.FromResult(true);
        };
        var sent = new List<string?>();
        using var invoker = CreateInvoker(request =>
        {
            sent.Add(request.Headers.Authorization?.Parameter);
            return new HttpResponseMessage(sent.Count == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        });

        var response = await CallAsync(invoker, HttpMethod.Get, "http://server.test/cameras");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        sent.Should().Equal("stale", "fresh");
    }

    [Fact]
    public async Task A_Session_That_Cannot_Be_Renewed_Ends()
    {
        session.Apply(Session("stale", expiresIn: 3600));
        session.Renew = _ => Task.FromResult(false);
        using var invoker = CreateInvoker(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await CallAsync(invoker, HttpMethod.Get, "http://server.test/cameras");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        session.IsSignedIn.Should().BeFalse("the user signs in again");
    }

    [Fact]
    public async Task The_Sign_In_Calls_Go_Without_A_Token()
    {
        session.Apply(Session("first", expiresIn: 3600));
        var sent = new List<HttpRequestMessage>();
        using var invoker = CreateInvoker(request =>
        {
            sent.Add(request);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await CallAsync(invoker, HttpMethod.Post, "http://server.test/auth/refresh");

        sent.Single().Headers.Authorization.Should().BeNull();
    }

    private static Session Session(
        string accessToken,
        int expiresIn)
        => new(
            AccessToken: accessToken,
            ExpiresInSeconds: expiresIn,
            RefreshToken: "refresh-" + accessToken,
            UserName: "admin",
            Role: UserRole.Admin,
            MediaToken: "media-" + accessToken);

    private static async Task<HttpResponseMessage> CallAsync(
        HttpMessageInvoker invoker,
        HttpMethod method,
        string url)
    {
        using var request = new HttpRequestMessage(method, url);
        return await invoker.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The invoker owns the handlers (disposeHandler) and the test disposes the invoker.")]
    private HttpMessageInvoker CreateInvoker(
        Func<HttpRequestMessage, HttpResponseMessage> answer)
        => new(new BearerTokenHandler(session) { InnerHandler = new AnswerHandler(answer) }, disposeHandler: true);

    private sealed class AnswerHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(answer(request));
    }
}