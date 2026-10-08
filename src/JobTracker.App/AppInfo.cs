using System.Net.Http;
using System.Reflection;
using System.Windows;
using JobTracker.Core;
using JobTracker.Core.Updates;

namespace JobTracker.App;

/// <summary>Where the program lives on GitHub and which version is running.</summary>
public static class AppInfo
{
    public const string Owner = "srinadhmanchikalapudi";
    public const string Repo = "job-tracker";

    public static string ReleasesUrl => $"https://github.com/{Owner}/{Repo}/releases/latest";

    /// <summary>The running version, from the build (publish.ps1 and the release workflow set it from the tag). 0.0.0 if it cannot be read.</summary>
    public static Version CurrentVersion
    {
        get
        {
            var informational = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return UpdateVersions.Parse(informational) ?? typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
        }
    }

    /// <summary>Builds the update service for the real app: GitHub for the lookup and the download, Windows for the launch.</summary>
    public static UpdateService CreateUpdateService(AppSettings settings)
    {
        var lookup = NewHttpClient(TimeSpan.FromSeconds(15));
        var download = NewHttpClient(TimeSpan.FromMinutes(10));
        var launcher = new ShellProcessLauncher();
        return new UpdateService(
            new GitHubUpdateChecker(lookup, Owner, Repo),
            new UpdateInstaller(download, launcher),
            settings,
            s => s.Save(),
            new InstallationInfo(),
            new ShellUrlOpener(launcher),
            CurrentVersion,
            ReleasesUrl,
            exit: () => Application.Current.Dispatcher.Invoke(Application.Current.Shutdown));
    }

    private static HttpClient NewHttpClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("JobTracker"); // the GitHub API refuses requests without a user agent
        return http;
    }
}
