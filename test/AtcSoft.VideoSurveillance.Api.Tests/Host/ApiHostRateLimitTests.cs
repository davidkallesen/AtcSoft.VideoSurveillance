namespace AtcSoft.VideoSurveillance.Api.Host;

/// <summary>
/// The calls that make the server reach out (a webhook test) are limited per
/// client address (x-ratelimit-*). Each test calls from an address of its own,
/// so it neither uses up nor meets the limit of the other host tests.
/// </summary>
[Collection(ApiHostTestGroup.Name)]
public sealed class ApiHostRateLimitTests(ApiHostFixture host)
{
    [Fact]
    public async Task The_Eleventh_Webhook_Test_In_A_Minute_Is_Turned_Away()
    {
        var address = IPAddress.Parse("192.0.2.51");
        var path = $"/webhooks/{Guid.NewGuid()}/test";

        for (var i = 0; i < 10; i++)
        {
            var allowed = await SendAsync(address, path);
            allowed.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        }

        var refused = await SendAsync(address, path);

        refused.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        refused.Response.Headers.RetryAfter.ToString().Should().NotBeEmpty();
        refused.Response.ContentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task The_Twenty_First_Snapshot_In_Ten_Seconds_Is_Turned_Away()
    {
        var address = IPAddress.Parse("192.0.2.54");
        var path = $"/cameras/{Guid.NewGuid()}/snapshot";

        for (var i = 0; i < 20; i++)
        {
            var allowed = await SendAsync(address, path, HttpMethods.Get);
            allowed.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        }

        var refused = await SendAsync(address, path, HttpMethods.Get);

        refused.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task Another_Address_Has_A_Limit_Of_Its_Own()
    {
        var path = $"/webhooks/{Guid.NewGuid()}/test";
        for (var i = 0; i < 11; i++)
        {
            await SendAsync(IPAddress.Parse("192.0.2.52"), path);
        }

        var other = await SendAsync(IPAddress.Parse("192.0.2.53"), path);

        other.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    private Task<HttpContext> SendAsync(
        IPAddress address,
        string path,
        string? method = null)
    {
        var token = host.GetAdminAccessToken();
        return host.SendAsync(context =>
        {
            context.Request.Method = method ?? HttpMethods.Post;
            context.Request.Path = path;
            context.Request.Headers.Authorization = "Bearer " + token;
            context.Connection.RemoteIpAddress = address;
        });
    }
}