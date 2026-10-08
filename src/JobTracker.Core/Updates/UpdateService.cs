using System.Diagnostics;

namespace JobTracker.Core.Updates;

public enum UpdatePhase { Idle, Checking, Available, Downloading, Failed }

/// <summary>
/// Keeps the program up to date, with the user in charge at every step: it looks for a newer GitHub release (on start, at most once a day, and
/// only if allowed; or when asked), says so, and installs it only when the user clicks. A copy that the installer put in place is updated in
/// place (the setup program is downloaded, checked, started, and the program exits so it can be replaced and starts again). A copy that was
/// unzipped or built is pointed to the download page instead.
/// </summary>
public sealed class UpdateService(
    IUpdateChecker checker,
    IUpdateInstaller installer,
    AppSettings settings,
    Action<AppSettings> saveSettings,
    IInstallationInfo installation,
    IUrlOpener opener,
    Version current,
    string releasesUrl,
    Func<DateTime>? now = null,
    Action? exit = null)
{
    /// <summary>Set to 1 to skip the automatic check (development and tests).</summary>
    public const string NoCheckEnvVar = "JOBTRACKER_NO_UPDATE_CHECK";

    /// <summary>The checks on start are at most this far apart.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);

    public UpdatePhase Phase { get; private set; } = UpdatePhase.Idle;
    public UpdateInfo? Available { get; private set; }
    public double Progress { get; private set; }
    public string? Error { get; private set; }

    /// <summary>The result of the last check in words, for the settings window and the banner.</summary>
    public string StatusText { get; private set; } = "";

    public string CurrentText => UpdateVersions.Display(current);

    /// <summary>True when the update can be installed from here (the setup program is attached and this copy was installed by it).</summary>
    public bool CanInstallInPlace => Available?.InstallerUrl is not null && installation.InstalledBySetup;

    public event Action? Changed;

    /// <summary>The automatic check on start: only if allowed, not from a debugger or when switched off for development, and not more than once a day.</summary>
    public async Task CheckOnStartupAsync(CancellationToken ct = default)
    {
        if (!settings.CheckForUpdates || Debugger.IsAttached)
            return;
        if (Environment.GetEnvironmentVariable(NoCheckEnvVar) == "1")
            return;
        if (settings.LastUpdateCheck is { } last && _now() - last < CheckInterval)
            return;
        await CheckAsync(manual: false, ct);
    }

    /// <summary>Looks for a newer release. A check the user asked for says what went wrong; an automatic one stays quiet when it fails.</summary>
    public async Task CheckAsync(bool manual, CancellationToken ct = default)
    {
        if (Phase is UpdatePhase.Checking or UpdatePhase.Downloading)
            return;
        Set(UpdatePhase.Checking, "Looking for updates…");
        var result = await checker.CheckAsync(current, ct);

        switch (result.Status)
        {
            case UpdateStatus.Available:
                Available = result.Update;
                Remember();
                Set(UpdatePhase.Available, $"Version {UpdateVersions.Display(result.Update!.Version)} is available.");
                break;
            case UpdateStatus.UpToDate:
                Available = null;
                Remember();
                Set(UpdatePhase.Idle, $"You have the latest version ({CurrentText}).");
                break;
            default:
                Error = manual ? result.Error : null;
                Set(manual ? UpdatePhase.Failed : UpdatePhase.Idle, manual ? (result.Error ?? "The check failed.") : "");
                break;
        }
    }

    /// <summary>Installs the available update (after the caller asked the user), or opens the download page when it cannot be installed from here.</summary>
    public async Task InstallAsync(CancellationToken ct = default)
    {
        if (Available is not { } update || Phase == UpdatePhase.Downloading)
            return;
        if (!CanInstallInPlace)
        {
            opener.Open(update.PageUrl);
            return;
        }

        Progress = 0;
        Error = null;
        Set(UpdatePhase.Downloading, "Downloading the update…");
        try
        {
            var path = await installer.DownloadAsync(update, new Progress<double>(p => { Progress = p; Changed?.Invoke(); }), ct);
            installer.Launch(path);
            exit?.Invoke(); // the setup program replaces the program, so it must not be running
        }
        catch (UpdateException ex)
        {
            Error = ex.Message;
            Set(UpdatePhase.Failed, ex.Message);
        }
        catch (OperationCanceledException)
        {
            Set(UpdatePhase.Available, $"Version {UpdateVersions.Display(update.Version)} is available.");
        }
    }

    /// <summary>True while there is a version to install (or to download), including after an install that failed.</summary>
    public bool HasUpdateToInstall => Available is not null && Phase is UpdatePhase.Available or UpdatePhase.Failed;

    /// <summary>Opens the release page in the browser.</summary>
    public void OpenReleasePage() => opener.Open(Available?.PageUrl ?? releasesUrl);

    private void Remember()
    {
        try
        {
            settings.LastUpdateCheck = _now();
            saveSettings(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not remembering the time of a check is not worth an error: the next start simply checks again
        }
    }

    private void Set(UpdatePhase phase, string status)
    {
        Phase = phase;
        StatusText = status;
        Changed?.Invoke();
    }
}
