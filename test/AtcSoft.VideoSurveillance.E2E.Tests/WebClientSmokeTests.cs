namespace AtcSoft.VideoSurveillance.E2E;

/// <summary>
/// The web client in Edge (headless) against the test server: sign in, every page, every tab,
/// the dialogs the pages and the user menu open, and every camera's form. It fails on a step
/// that breaks, an error in the browser console or a page error, a 5xx answer, and a fatal
/// entry in the server's output.
/// </summary>
/// <remarks>
/// Left out on purpose: downloads, deleting, the webhook test send, switching the language
/// (a page reload), and signing out.
/// </remarks>
[Collection(E2ETestGroup.Name)]
public sealed class WebClientSmokeTests(ServerFixture server)
{
    private static readonly string[] Pages = ["/", "/cameras", "/cameras/create", "/layouts", "/live", "/recordings", "/webhooks", "/settings", "/users"];

    [Fact]
    public async Task Every_Page_Opens_Without_Errors()
    {
        E2ESettings.SkipUnlessEnabled();

        using var web = await WebClientRun.StartAsync(server);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = web.BaseUri.ToString(),
            Locale = "en-US",
        });

        // The app reads its server address from appsettings.json at start.
        await context.RouteAsync(
            "**/appsettings.json",
            route => route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = WebClientRun.AppSettingsJson(server),
            }));

        var page = await context.NewPageAsync();
        var problems = new List<string>();
        page.Console += (_, message) =>
        {
            if (string.Equals(message.Type, "error", StringComparison.Ordinal))
            {
                problems.Add($"console ({page.Url}): {message.Text}");
            }
        };
        page.PageError += (_, error) => problems.Add($"page error ({page.Url}): {error}");
        page.RequestFailed += (_, request) =>
        {
            // Leaving a page cancels its open requests (the event stream): not a failure.
            if (request.Failure?.Contains("ERR_ABORTED", StringComparison.Ordinal) != true)
            {
                problems.Add($"request failed: {request.Method} {request.Url} ({request.Failure})");
            }
        };
        page.Response += (_, response) =>
        {
            if (response.Status >= 500)
            {
                problems.Add($"{response.Status} {response.Request.Method} {response.Url}");
            }
        };

        var steps = new SmokeSteps();
        var shots = E2ESettings.NewFolder("web-client-ui");
        Func<string, Func<Task>, Task> run = (name, step) => steps.RunAsync(name, async () =>
        {
            try
            {
                await step();
            }
            catch
            {
                await SaveEvidenceAsync(page, shots, name);
                throw;
            }
        });

        await run("sign in", () => SignInAsync(page));

        foreach (var path in Pages)
        {
            await run($"page {path}", () => VisitAsync(page, path));
        }

        foreach (var cameraId in await CameraIdsAsync())
        {
            await run($"camera form {cameraId}", () => VisitAsync(page, $"/cameras/{cameraId}/edit"));
        }

        await run("Add user", () => OpenAndCancelAsync(page, "/users", "Add user"));
        await run("Make API key", () => OpenAndCancelAsync(page, "/users", "Make API key", tab: "API keys"));
        await run("Add receiver", () => OpenAndCancelAsync(page, "/webhooks", "Add receiver"));
        await run("Change password", () => ChangePasswordAsync(page));

        await File.WriteAllLinesAsync(Path.Combine(shots, "browser-problems.txt"), problems, TestContext.Current.CancellationToken);
        steps.Failures.Should().BeEmpty(steps.Report());
        problems.Should().BeEmpty("the web client must open every page without errors");
        server.ServerFailures().Should().BeEmpty($"the server must not fail while the web client walks (output: {server.ServerOutputPath})");
    }

    private static async Task SaveEvidenceAsync(
        IPage page,
        string folder,
        string step)
    {
        var name = string.Concat(step.Where(char.IsLetterOrDigit));
        try
        {
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(folder, name + ".png"), FullPage = true });
            await File.WriteAllTextAsync(Path.Combine(folder, name + ".txt"), page.Url + Environment.NewLine + await page.InnerTextAsync("body"));
        }
        catch (PlaywrightException)
        {
            // Evidence only.
        }
    }

    private static async Task SignInAsync(IPage page)
    {
        await page.GotoAsync("/");
        await page.GetByLabel("Username").FillAsync(ServerFixture.AdminName);
        await page.GetByLabel("Password").FillAsync(ServerFixture.AdminPassword);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Signed in as {ServerFixture.AdminName}" })
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
    }

    private static async Task VisitAsync(
        IPage page,
        string path)
    {
        await page.GotoAsync(path);
        await page.WaitForTimeoutAsync(2500);

        // Every tab of the page, so each tab's content is rendered.
        var tabs = page.GetByRole(AriaRole.Tab);
        var count = await tabs.CountAsync();
        for (var i = 0; i < count; i++)
        {
            await tabs.Nth(i).ClickAsync();
            await page.WaitForTimeoutAsync(400);
        }
    }

    private static async Task OpenAndCancelAsync(
        IPage page,
        string path,
        string button,
        string? tab = null)
    {
        await page.GotoAsync(path);
        await page.WaitForTimeoutAsync(1500);
        if (tab is not null)
        {
            await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = tab }).ClickAsync();
            await page.WaitForTimeoutAsync(400);
        }

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = button }).ClickAsync();
        await CancelDialogAsync(page);
    }

    private static async Task ChangePasswordAsync(IPage page)
    {
        await page.GotoAsync("/");
        await page.WaitForTimeoutAsync(1500);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = $"Signed in as {ServerFixture.AdminName}" }).ClickAsync();
        await page.GetByText("Change password").First.ClickAsync();
        await CancelDialogAsync(page);
    }

    private static async Task CancelDialogAsync(IPage page)
    {
        var dialog = page.GetByRole(AriaRole.Dialog);
        await dialog.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(500);
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10_000 });
    }

    private async Task<IReadOnlyList<Guid>> CameraIdsAsync()
    {
        using var admin = await server.CreateAdminClientAsync();
        var cameras = await admin.GetFromJsonAsync<JsonElement>("/cameras");
        var ids = new List<Guid>();
        foreach (var camera in cameras.EnumerateArray())
        {
            ids.Add(camera.GetProperty("id").GetGuid());
        }

        return ids;
    }
}