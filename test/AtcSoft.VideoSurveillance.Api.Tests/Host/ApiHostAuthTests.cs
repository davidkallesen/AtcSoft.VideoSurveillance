namespace AtcSoft.VideoSurveillance.Api.Host;

/// <summary>
/// Sign-in on the running server: the first admin made with the code from the
/// data folder, a session renewed and ended, and the signed-in user named.
/// </summary>
[Collection(ApiHostTestGroup.Name)]
public sealed class ApiHostAuthTests(ApiHostFixture host)
{
    [Fact]
    public async Task The_Admin_Made_With_The_Setup_Code_Signs_In_Renews_And_Ends_A_Session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = host.CreateAnonymousClient();
        using var anonymous = host.CreateAnonymousClient();
        await host.EnsureAdminAsync();

        // The server has its first admin, so it cannot be set up again.
        var status = await client.GetFromJsonAsync<JsonElement>("/auth/setup", cancellationToken);
        status.GetProperty("setupRequired").GetBoolean().Should().BeFalse();
        var again = await client.PostAsJsonAsync("/auth/setup", new { setupCode = "ANYTHING", userName = "second", password = "a long password" }, cancellationToken);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // A wrong password says nothing about which part was wrong.
        var refused = await client.PostAsJsonAsync("/auth/token", new { userName = ApiHostFixture.AdminName, password = "not the password" }, cancellationToken);
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("detail").GetString()
            .Should().Be("The user name or the password is not right.");

        // Signed in, the access token names the user.
        var signedIn = await client.PostAsJsonAsync("/auth/token", new { userName = ApiHostFixture.AdminName, password = ApiHostFixture.AdminPassword }, cancellationToken);
        signedIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var session = await signedIn.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        session.GetProperty("role").GetString().Should().Be("admin");
        session.GetProperty("expiresInSeconds").GetInt32().Should().Be(3600);

        using (var me = new HttpRequestMessage(HttpMethod.Get, "/auth/me"))
        {
            me.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
            var answer = await client.SendAsync(me, cancellationToken);
            answer.StatusCode.Should().Be(HttpStatusCode.OK);
            (await answer.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("userName").GetString().Should().Be(ApiHostFixture.AdminName);
        }

        (await anonymous.GetAsync(new Uri("/auth/me", UriKind.Relative), cancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The refresh token is used once, and signing out ends the session.
        var refreshToken = session.GetProperty("refreshToken").GetString();
        var renewed = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken }, cancellationToken);
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
        var renewedToken = (await renewed.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("refreshToken").GetString();
        (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken }, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync("/auth/sign-out", new { refreshToken = renewedToken }, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = renewedToken }, cancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_Password_Shorter_Than_Eight_Characters_Is_Refused_By_The_Specification()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = host.CreateAnonymousClient();

        var refused = await client.PostAsJsonAsync("/auth/setup", new { setupCode = "X", userName = "admin", password = "short" }, cancellationToken);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refused.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }
}