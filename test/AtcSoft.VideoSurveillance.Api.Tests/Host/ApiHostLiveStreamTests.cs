namespace AtcSoft.VideoSurveillance.Api.Host;

/// <summary>
/// The whole way through with a real stream: the server opens a camera,
/// records it, makes the thumbnail and serves both. The stream comes from
/// the FFmpeg command-line tool; a machine without it skips the test.
/// </summary>
[Collection(ApiHostTestGroup.Name)]
public sealed class ApiHostLiveStreamTests(ApiHostFixture host)
{
    private static readonly TimeSpan ThumbnailTimeout = TimeSpan.FromSeconds(40);

    [Fact]
    public async Task A_Recorded_Camera_Gets_A_Thumbnail_That_Is_Listed_And_Served()
    {
        var ffmpeg = FindFFmpegTool();
        Assert.SkipWhen(ffmpeg is null, "The FFmpeg command-line tool is not installed.");

        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = host.CreateClient();
        var port = GetFreePort();
        using var stream = StartTestStream(ffmpeg!, port);
        Guid? cameraId = null;

        try
        {
            // Arrange — a camera that is the test stream, rotated a quarter turn
            var name = "Live " + Guid.NewGuid().ToString("N")[..8];
            var created = await client.PostAsJsonAsync(
                "/cameras",
                new { displayName = name, ipAddress = "127.0.0.1", port, protocol = "http", path = "live", rotation = "rotate90" },
                cancellationToken);
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            cameraId = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetGuid();

            // Act
            var started = await client.PostAsync(new Uri($"/cameras/{cameraId}/recording", UriKind.Relative), content: null, cancellationToken);
            started.StatusCode.Should().Be(HttpStatusCode.OK, await started.Content.ReadAsStringAsync(cancellationToken));

            var listed = await WaitForThumbnailAsync(client, name, cancellationToken);

            // Assert — the thumbnail is where the layout puts it, and is a picture
            var thumbnailPath = listed.GetProperty("thumbnailPath").GetString()!;
            thumbnailPath.Should().StartWith($"thumbnails/{name}/").And.EndWith(".png");
            listed.GetProperty("filePath").GetString().Should().StartWith($"{name}/").And.NotContain(":", "clients get a path below the recordings root");

            var thumbnail = await client.GetAsync(
                new Uri("/recordings-files/" + RecordingPathHelper.EscapePathSegments(thumbnailPath), UriKind.Relative),
                cancellationToken);
            thumbnail.StatusCode.Should().Be(HttpStatusCode.OK);
            var png = await thumbnail.Content.ReadAsByteArrayAsync(cancellationToken);
            png.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
            png.Length.Should().BeGreaterThan(1000, "four tiles of a test pattern are not an empty picture");

            // The recording stops cleanly and stays listed with its thumbnail
            var stopped = await client.DeleteAsync(new Uri($"/cameras/{cameraId}/recording", UriKind.Relative), cancellationToken);
            stopped.StatusCode.Should().Be(HttpStatusCode.OK);

            var afterStop = await FindRecordingAsync(client, name, cancellationToken);
            afterStop!.Value.GetProperty("thumbnailPath").GetString().Should().Be(thumbnailPath);
            afterStop.Value.GetProperty("fileSizeBytes").GetInt64().Should().BeGreaterThan(0);
        }
        finally
        {
            if (cameraId is not null)
            {
                await client.DeleteAsync(new Uri($"/cameras/{cameraId}/recording", UriKind.Relative), CancellationToken.None);
                await client.DeleteAsync(new Uri($"/cameras/{cameraId}", UriKind.Relative), CancellationToken.None);
            }

            Stop(stream);
        }
    }

    [Fact]
    public async Task A_Camera_Can_Be_Watched_Live_Over_Hls()
    {
        var ffmpeg = FindFFmpegTool();
        Assert.SkipWhen(ffmpeg is null, "The FFmpeg command-line tool is not installed.");

        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = host.CreateClient();
        var port = GetFreePort();
        using var stream = StartTestStream(ffmpeg!, port);
        await using var hub = host.CreateHubConnection();
        Guid? cameraId = null;

        try
        {
            // Arrange — an HTTP camera with a password
            var created = await client.PostAsJsonAsync(
                "/cameras",
                new
                {
                    displayName = "Hls " + Guid.NewGuid().ToString("N")[..8],
                    ipAddress = "127.0.0.1",
                    port,
                    protocol = "http",
                    path = "live",
                    username = "viewer",
                    password = "hls-secret",
                },
                cancellationToken);
            created.StatusCode.Should().Be(HttpStatusCode.Created);
            cameraId = (await created.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).GetProperty("id").GetGuid();

            var started = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            hub.On<JsonElement>("StreamStarted", e => started.TrySetResult(e.GetProperty("playlistUrl").GetString()!));
            await hub.StartAsync(cancellationToken);

            // Act
            await hub.InvokeAsync("StartStream", cameraId.Value, cancellationToken);
            var playlistUrl = await started.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

            // Assert — the playlist and its first segment can be fetched
            var playlist = await client.GetAsync(new Uri(playlistUrl, UriKind.Relative), cancellationToken);
            playlist.StatusCode.Should().Be(HttpStatusCode.OK);
            var text = await playlist.Content.ReadAsStringAsync(cancellationToken);
            text.Should().StartWith("#EXTM3U");

            var segment = text.Split('\n').Select(l => l.Trim()).First(l => l.EndsWith(".ts", StringComparison.Ordinal));
            var segmentUrl = playlistUrl[..(playlistUrl.LastIndexOf('/') + 1)] + segment;
            (await client.GetAsync(new Uri(segmentUrl, UriKind.Relative), cancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

            await hub.InvokeAsync("StopStream", cameraId.Value, cancellationToken);
        }
        finally
        {
            if (cameraId is not null)
            {
                await client.DeleteAsync(new Uri($"/cameras/{cameraId}", UriKind.Relative), CancellationToken.None);
            }

            Stop(stream);
        }
    }

    private static async Task<JsonElement> WaitForThumbnailAsync(
        HttpClient client,
        string cameraName,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ThumbnailTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var recording = await FindRecordingAsync(client, cameraName, cancellationToken);
            if (recording is { } found &&
                found.TryGetProperty("thumbnailPath", out var path) &&
                path.ValueKind == JsonValueKind.String)
            {
                return found;
            }

            await Task.Delay(500, cancellationToken);
        }

        Assert.Fail($"No thumbnail was listed for '{cameraName}' within {ThumbnailTimeout.TotalSeconds:N0} seconds.");
        return default;
    }

    private static async Task<JsonElement?> FindRecordingAsync(
        HttpClient client,
        string cameraName,
        CancellationToken cancellationToken)
    {
        var recordings = await client.GetFromJsonAsync<JsonElement>("/recordings", cancellationToken);
        foreach (var recording in recordings.EnumerateArray())
        {
            if (recording.GetProperty("filePath").GetString()!.Contains(cameraName, StringComparison.Ordinal))
            {
                return recording;
            }
        }

        return null;
    }

    /// <summary>
    /// Serves a live-paced test pattern over HTTP, the way an HTTP camera
    /// does. It waits for the one client, the server, and ends by itself
    /// after a minute if nobody stops it.
    /// </summary>
    private static Process StartTestStream(
        string ffmpeg,
        int port)
    {
        var startInfo = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        foreach (var argument in new[]
                 {
                     "-hide_banner", "-loglevel", "error",
                     "-re", "-f", "lavfi", "-i", "testsrc=size=320x240:rate=15",
                     "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency",
                     "-g", "15", "-pix_fmt", "yuv420p",
                     "-t", "60",
                     "-f", "mpegts", "-listen", "1", $"http://127.0.0.1:{port}/live",
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)!;
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return process;
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private static string? FindFFmpegTool()
    {
        var folder = AtcSoft.VideoEngine.VideoEngineBootstrap.FindFFmpegPath();
        if (folder is null)
        {
            return null;
        }

        var tool = Path.Combine(folder, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        return File.Exists(tool) ? tool : null;
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}