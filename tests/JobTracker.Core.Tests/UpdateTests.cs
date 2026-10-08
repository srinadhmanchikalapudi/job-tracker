using System.Net;
using System.Security.Cryptography;
using System.Text;
using JobTracker.Core.Updates;

namespace JobTracker.Core.Tests;

internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }
}

internal sealed class FakeLauncher : IProcessLauncher
{
    public List<(string File, string Args)> Started { get; } = [];
    public void Start(string fileName, string arguments) => Started.Add((fileName, arguments));
}

public class UpdateVersionsTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.3-beta+abc", "1.2.3")]
    [InlineData("  V2.0.1 ", "2.0.1")]
    public void Parse_ReadsVersions(string text, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateVersions.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1")]
    [InlineData(null)]
    public void Parse_RejectsNonVersions(string? text) => Assert.Null(UpdateVersions.Parse(text));

    [Fact]
    public void IsNewer_ComparesFirstThreeParts()
    {
        Assert.True(UpdateVersions.IsNewer(new Version(1, 1, 0), new Version(1, 0, 9)));
        Assert.True(UpdateVersions.IsNewer(new Version(1, 10, 0), new Version(1, 9, 0)));
        Assert.False(UpdateVersions.IsNewer(new Version(1, 0, 0), new Version(1, 0, 0)));
        Assert.False(UpdateVersions.IsNewer(new Version(1, 0, 0), new Version(1, 0, 1)));
        Assert.False(UpdateVersions.IsNewer(new Version(1, 0, 0, 5), new Version(1, 0, 0)));
    }

    [Fact]
    public void Display_ShowsThreeParts() => Assert.Equal("1.2.0", UpdateVersions.Display(new Version(1, 2)));
}

public class GitHubUpdateCheckerTests
{
    private const string Release = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/o/r/releases/tag/v1.2.0",
          "assets": [
            { "name": "JobTracker-1.2.0-win-x64.zip", "browser_download_url": "https://github.com/o/r/releases/download/v1.2.0/zip" },
            { "name": "JobTracker-Setup-1.2.0.exe", "browser_download_url": "https://github.com/o/r/releases/download/v1.2.0/setup",
              "digest": "sha256:ABCDEF0123" }
          ]
        }
        """;

    private static GitHubUpdateChecker Checker(HttpStatusCode status, string body = "", Func<HttpRequestMessage, HttpResponseMessage>? custom = null)
    {
        var handler = new FakeHandler(custom ?? (_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
        return new GitHubUpdateChecker(new HttpClient(handler), "o", "r");
    }

    [Fact]
    public async Task NewerRelease_IsAvailable_WithSetupAssetAndChecksum()
    {
        var result = await Checker(HttpStatusCode.OK, Release).CheckAsync(new Version(1, 1, 0));

        Assert.Equal(UpdateStatus.Available, result.Status);
        var update = result.Update!;
        Assert.Equal(new Version(1, 2, 0), update.Version);
        Assert.EndsWith("/setup", update.InstallerUrl);
        Assert.Equal("abcdef0123", update.InstallerSha256);
        Assert.Equal("https://github.com/o/r/releases/tag/v1.2.0", update.PageUrl);
    }

    [Fact]
    public async Task SameOrOlderRelease_IsUpToDate()
    {
        Assert.Equal(UpdateStatus.UpToDate, (await Checker(HttpStatusCode.OK, Release).CheckAsync(new Version(1, 2, 0))).Status);
        Assert.Equal(UpdateStatus.UpToDate, (await Checker(HttpStatusCode.OK, Release).CheckAsync(new Version(2, 0, 0))).Status);
    }

    [Fact]
    public async Task NoReleaseYet_404_IsUpToDate() =>
        Assert.Equal(UpdateStatus.UpToDate, (await Checker(HttpStatusCode.NotFound).CheckAsync(new Version(1, 0, 0))).Status);

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RateLimited_IsFailedWithAMessage(HttpStatusCode status)
    {
        var result = await Checker(status).CheckAsync(new Version(1, 0, 0));

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("limiting", result.Error);
    }

    [Fact]
    public async Task ServerError_IsFailed() =>
        Assert.Equal(UpdateStatus.Failed, (await Checker(HttpStatusCode.InternalServerError).CheckAsync(new Version(1, 0, 0))).Status);

    [Fact]
    public async Task NetworkTrouble_IsFailedNotThrown()
    {
        var checker = Checker(HttpStatusCode.OK, custom: _ => throw new HttpRequestException("no network"));

        var result = await checker.CheckAsync(new Version(1, 0, 0));

        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Contains("no network", result.Error);
    }

    [Fact]
    public async Task BadJson_IsFailed() =>
        Assert.Equal(UpdateStatus.Failed, (await Checker(HttpStatusCode.OK, "<html>").CheckAsync(new Version(1, 0, 0))).Status);

    [Fact]
    public async Task TagThatIsNotAVersion_IsIgnored() =>
        Assert.Equal(UpdateStatus.UpToDate, (await Checker(HttpStatusCode.OK, """{ "tag_name": "nightly" }""").CheckAsync(new Version(1, 0, 0))).Status);

    [Fact]
    public async Task ReleaseWithoutSetupAsset_IsAvailableButNotInstallable()
    {
        var result = await Checker(HttpStatusCode.OK, """{ "tag_name": "v2.0.0", "assets": [] }""").CheckAsync(new Version(1, 0, 0));

        Assert.Equal(UpdateStatus.Available, result.Status);
        Assert.Null(result.Update!.InstallerUrl);
        Assert.Contains("github.com/o/r/releases/latest", result.Update.PageUrl);
    }
}

public class UpdateInstallerTests
{
    private static readonly byte[] Payload = Encoding.ASCII.GetBytes("pretend this is a setup program");
    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static UpdateInfo Info(string url = "https://github.com/o/r/releases/download/v1.1.0/JobTracker-Setup-1.1.0.exe", string? sha = null) =>
        new(new Version(1, 1, 0), "v1.1.0", "https://github.com/o/r/releases/tag/v1.1.0", url, sha);

    private static (UpdateInstaller Installer, FakeLauncher Launcher, TempDir Temp) Make(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var temp = new TempDir();
        var launcher = new FakeLauncher();
        return (new UpdateInstaller(new HttpClient(new FakeHandler(respond)), launcher, () => temp.Path), launcher, temp);
    }

    private static HttpResponseMessage Ok(byte[]? body = null) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body ?? Payload) };

    [Fact]
    public async Task Download_SavesTheFile_ReportsProgress_AndChecksTheHash()
    {
        var (installer, _, temp) = Make(_ => Ok());
        using var _t = temp;
        double last = 0;

        var path = await installer.DownloadAsync(Info(sha: Sha(Payload)), new Progress<double>(p => last = p));

        Assert.True(File.Exists(path));
        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
        Assert.EndsWith("JobTracker-Setup-1.1.0.exe", path);
        await Task.Delay(50);
        Assert.Equal(1.0, last);
    }

    [Fact]
    public async Task Download_ChecksumMismatch_DeletesTheFile_AndThrows()
    {
        var (installer, _, temp) = Make(_ => Ok());
        using var _t = temp;

        var ex = await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAsync(Info(sha: new string('0', 64))));

        Assert.Contains("checksum", ex.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(temp.Path, "JobTracker-update")));
    }

    [Theory]
    [InlineData("https://evil.example.com/setup.exe")]
    [InlineData("http://github.com/o/r/setup.exe")]
    [InlineData("https://github.com.evil.example.com/setup.exe")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    public async Task Download_UntrustedAddress_IsRefusedWithoutAnyRequest(string url)
    {
        var requested = false;
        var (installer, _, temp) = Make(_ => { requested = true; return Ok(); });
        using var _t = temp;

        await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAsync(Info(url)));

        Assert.False(requested);
    }

    [Theory]
    [InlineData("https://github.com/o/r/releases/download/v1/x.exe")]
    [InlineData("https://objects.githubusercontent.com/abc/x.exe")]
    [InlineData("https://release-assets.githubusercontent.com/abc/x.exe")]
    public void IsTrustedUrl_AcceptsGitHubHosts(string url) => Assert.True(UpdateInstaller.IsTrustedUrl(url));

    [Fact]
    public async Task Download_HttpError_ThrowsWithTheStatus()
    {
        var (installer, _, temp) = Make(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var _t = temp;

        var ex = await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAsync(Info()));

        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task Download_ReleaseWithoutSetup_Throws()
    {
        var (installer, _, temp) = Make(_ => Ok());
        using var _t = temp;

        await Assert.ThrowsAsync<UpdateException>(() => installer.DownloadAsync(Info() with { InstallerUrl = null }));
    }

    [Fact]
    public void Launch_StartsTheSetupWithTheUpdateArguments()
    {
        var (installer, launcher, temp) = Make(_ => Ok());
        using var _t = temp;

        installer.Launch(@"C:\temp\setup.exe");

        var started = Assert.Single(launcher.Started);
        Assert.Equal(@"C:\temp\setup.exe", started.File);
        Assert.Contains("/RESTARTAPP=1", started.Args);
        Assert.Contains("/CLOSEAPPLICATIONS", started.Args);
    }
}

public class UpdateServiceTests
{
    private sealed class FakeChecker(UpdateCheckResult result) : IUpdateChecker
    {
        public int Calls { get; private set; }
        public Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeInstaller : IUpdateInstaller
    {
        public bool Downloaded, Launched;
        public Exception? Fail;
        public Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
        {
            Downloaded = true;
            if (Fail is not null) throw Fail;
            return Task.FromResult(@"C:\temp\setup.exe");
        }
        public void Launch(string setupPath) => Launched = true;
    }

    private sealed class FakeOpener : IUrlOpener
    {
        public List<string> Opened { get; } = [];
        public void Open(string url) => Opened.Add(url);
    }

    private sealed class FakeInstallation(bool installed) : IInstallationInfo
    {
        public bool InstalledBySetup { get; } = installed;
    }

    private static readonly UpdateInfo Newer = new(new Version(1, 1, 0), "v1.1.0", "https://github.com/o/r/releases/tag/v1.1.0",
        "https://github.com/o/r/releases/download/v1.1.0/JobTracker-Setup-1.1.0.exe", null);

    private static (UpdateService Service, FakeChecker Checker, FakeInstaller Installer, FakeOpener Opener, AppSettings Settings, bool[] Exited) Make(
        UpdateCheckResult result, bool installed = true, AppSettings? settings = null, DateTime? now = null)
    {
        var checker = new FakeChecker(result);
        var installer = new FakeInstaller();
        var opener = new FakeOpener();
        settings ??= new AppSettings();
        var exited = new bool[1];
        var service = new UpdateService(checker, installer, settings, _ => { }, new FakeInstallation(installed), opener,
            new Version(1, 0, 0), "https://github.com/o/r/releases/latest", () => now ?? new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            () => exited[0] = true);
        return (service, checker, installer, opener, settings, exited);
    }

    [Fact]
    public async Task Check_NewerRelease_BecomesAvailable_AndRemembersWhenItLooked()
    {
        var (service, _, _, _, settings, _) = Make(UpdateCheckResult.Available(Newer));

        await service.CheckAsync(manual: false);

        Assert.Equal(UpdatePhase.Available, service.Phase);
        Assert.True(service.HasUpdateToInstall);
        Assert.Equal(new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), settings.LastUpdateCheck);
        Assert.Contains("1.1.0", service.StatusText);
    }

    [Fact]
    public async Task Check_UpToDate_SaysSo()
    {
        var (service, _, _, _, _, _) = Make(UpdateCheckResult.UpToDate);

        await service.CheckAsync(manual: true);

        Assert.Equal(UpdatePhase.Idle, service.Phase);
        Assert.Contains("latest version (1.0.0)", service.StatusText);
    }

    [Fact]
    public async Task Check_Failure_IsQuietWhenAutomatic_AndLoudWhenAsked()
    {
        var (auto, _, _, _, _, _) = Make(UpdateCheckResult.Failed("no network"));
        await auto.CheckAsync(manual: false);
        Assert.Equal(UpdatePhase.Idle, auto.Phase);
        Assert.Equal("", auto.StatusText);

        var (asked, _, _, _, _, _) = Make(UpdateCheckResult.Failed("no network"));
        await asked.CheckAsync(manual: true);
        Assert.Equal(UpdatePhase.Failed, asked.Phase);
        Assert.Equal("no network", asked.StatusText);
    }

    [Fact]
    public async Task Startup_RespectsTheSettingAndTheOncePerDayRule()
    {
        var saved = Environment.GetEnvironmentVariable(UpdateService.NoCheckEnvVar);
        Environment.SetEnvironmentVariable(UpdateService.NoCheckEnvVar, null); // this test is about the settings, not the development switch
        try
        {
            await StartupRules();
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateService.NoCheckEnvVar, saved);
        }
    }

    [Fact]
    public async Task Startup_DevelopmentSwitch_SkipsTheCheck()
    {
        var saved = Environment.GetEnvironmentVariable(UpdateService.NoCheckEnvVar);
        Environment.SetEnvironmentVariable(UpdateService.NoCheckEnvVar, "1");
        try
        {
            var (service, checker, _, _, _, _) = Make(UpdateCheckResult.UpToDate);
            await service.CheckOnStartupAsync();
            Assert.Equal(0, checker.Calls);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateService.NoCheckEnvVar, saved);
        }
    }

    private static async Task StartupRules()
    {
        var now = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

        var (off, offChecker, _, _, _, _) = Make(UpdateCheckResult.UpToDate, settings: new AppSettings { CheckForUpdates = false }, now: now);
        await off.CheckOnStartupAsync();
        Assert.Equal(0, offChecker.Calls);

        var (recent, recentChecker, _, _, _, _) = Make(UpdateCheckResult.UpToDate, settings: new AppSettings { LastUpdateCheck = now.AddHours(-3) }, now: now);
        await recent.CheckOnStartupAsync();
        Assert.Equal(0, recentChecker.Calls);

        var (due, dueChecker, _, _, _, _) = Make(UpdateCheckResult.UpToDate, settings: new AppSettings { LastUpdateCheck = now.AddHours(-25) }, now: now);
        await due.CheckOnStartupAsync();
        Assert.Equal(1, dueChecker.Calls);
    }

    [Fact]
    public async Task Install_WhenInstalledBySetup_DownloadsLaunchesAndExits()
    {
        var (service, _, installer, opener, _, exited) = Make(UpdateCheckResult.Available(Newer), installed: true);
        await service.CheckAsync(manual: true);

        await service.InstallAsync();

        Assert.True(installer.Downloaded);
        Assert.True(installer.Launched);
        Assert.True(exited[0]);
        Assert.Empty(opener.Opened);
    }

    [Fact]
    public async Task Install_WhenNotInstalledBySetup_OpensTheDownloadPageInstead()
    {
        var (service, _, installer, opener, _, exited) = Make(UpdateCheckResult.Available(Newer), installed: false);
        await service.CheckAsync(manual: true);

        await service.InstallAsync();

        Assert.False(installer.Downloaded);
        Assert.False(exited[0]);
        Assert.Equal([Newer.PageUrl], opener.Opened);
    }

    [Fact]
    public async Task Install_WhenTheDownloadFails_ShowsTheReason_AndDoesNotExit()
    {
        var (service, _, installer, _, _, exited) = Make(UpdateCheckResult.Available(Newer));
        installer.Fail = new UpdateException("The download was cut short. Try again.");
        await service.CheckAsync(manual: true);

        await service.InstallAsync();

        Assert.Equal(UpdatePhase.Failed, service.Phase);
        Assert.Equal("The download was cut short. Try again.", service.Error);
        Assert.False(exited[0]);
        Assert.True(service.HasUpdateToInstall); // the user can try again
    }
}

public class AppSettingsOverrideTests
{
    [Fact]
    public void UpdateSettings_RoundTrip()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "settings.json");
        var when = new DateTime(2026, 10, 8, 9, 30, 0, DateTimeKind.Utc);

        new AppSettings { CheckForUpdates = false, LastUpdateCheck = when }.Save(path);
        var loaded = AppSettings.Load(path);

        Assert.False(loaded.CheckForUpdates);
        Assert.Equal(when, loaded.LastUpdateCheck);
    }

    [Fact]
    public void Defaults_CheckForUpdates() => Assert.True(new AppSettings().CheckForUpdates);

[Fact]
    public void DataRootOverride_IsNeverWrittenToTheSavedSettings()
    {
        using var temp = new TempDir();
        var real = Path.Combine(temp.Path, "real-root");
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        new AppSettings { DataRoot = real }.Save(settingsPath);

        var running = AppSettings.Load(settingsPath).WithDataRootOverride(Path.Combine(temp.Path, "test-root"));
        Assert.Equal(Path.Combine(temp.Path, "test-root"), running.DataRoot);

        running.LastUpdateCheck = DateTime.UtcNow; // something saves settings while the override is on
        running.Save(settingsPath);

        Assert.Equal(real, AppSettings.Load(settingsPath).DataRoot);
    }

    [Fact]
    public void ChoosingAFolderWhileOverridden_IsSaved()
    {
        using var temp = new TempDir();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        var running = new AppSettings { DataRoot = Path.Combine(temp.Path, "real") }.WithDataRootOverride(Path.Combine(temp.Path, "test-root"));

        running.DataRoot = Path.Combine(temp.Path, "chosen");
        running.Save(settingsPath);

        Assert.Equal(Path.Combine(temp.Path, "chosen"), AppSettings.Load(settingsPath).DataRoot);
    }
}