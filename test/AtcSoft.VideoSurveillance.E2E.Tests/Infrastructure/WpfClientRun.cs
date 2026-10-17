namespace AtcSoft.VideoSurveillance.E2E.Infrastructure;

/// <summary>
/// One run of the server's WPF client in its own data folder, pointed at the test server the
/// way Aspire does it, and signed in from the configuration (Auth:UserName / Auth:Password).
/// </summary>
internal sealed class WpfClientRun : IDisposable
{
    public const string MainWindowTitle = "AtcSoft Video Surveillance";

    public WpfClientRun(ServerFixture server)
    {
        ArgumentNullException.ThrowIfNull(server);

        DataFolder = E2ESettings.NewFolder("wpf-client");
        LogFolder = Path.Combine(DataFolder, "logs");

        var process = ChildProcess.Start(
            E2ESettings.WpfClientExe,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ATCSOFT_DATA_PATH"] = DataFolder,
                ["services__api__http__0"] = server.BaseUri.ToString().TrimEnd('/'),
                ["Auth__UserName"] = ServerFixture.AdminName,
                ["Auth__Password"] = ServerFixture.AdminPassword,
            },
            Path.Combine(E2ESettings.RunFolder, "wpf-client-output.log"));
        App = new DesktopApp(process, Path.Combine(E2ESettings.RunFolder, "wpf-client-ui"));
    }

    public string DataFolder { get; }

    public string LogFolder { get; }

    public DesktopApp App { get; }

    public void Dispose()
    {
        App.Dispose();
        App.Process.Dispose();
    }
}