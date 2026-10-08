using CommunityToolkit.Mvvm.Input;
using JobTracker.Core.Updates;

namespace JobTracker.App.ViewModels;

/// <summary>The part of the main view model that shows update information: the banner and the Settings window.</summary>
public sealed partial class MainViewModel
{
    private UpdateService? _updates;
    private bool _bannerDismissed;

    public void AttachUpdates(UpdateService updates)
    {
        _updates = updates;
        updates.Changed += () =>
        {
            if (_dispatcher.CheckAccess())
                RaiseUpdateChanged();
            else
                _ = _dispatcher.BeginInvoke(RaiseUpdateChanged);
        };
        RaiseUpdateChanged();
    }

    public string VersionText => _updates is null ? "" : $"Version {_updates.CurrentText}";

    /// <summary>The banner shows while a newer version is waiting, downloading, or failed to install. "Later" hides it until the next start.</summary>
    public bool ShowUpdateBanner => !_bannerDismissed && _updates is { Available: not null, Phase: UpdatePhase.Available or UpdatePhase.Downloading or UpdatePhase.Failed };

    public bool IsDownloadingUpdate => _updates?.Phase == UpdatePhase.Downloading;
    public bool CanChooseUpdateAction => ShowUpdateBanner && !IsDownloadingUpdate;
    public double UpdateProgressPercent => (_updates?.Progress ?? 0) * 100;
    public bool CanInstallInPlace => _updates?.CanInstallInPlace == true;
    public string UpdateActionText => CanInstallInPlace ? "Update now" : "Download";
    public string UpdateStatusText => _updates?.StatusText ?? "";

    public string AvailableVersionText =>
        _updates?.Available is { } available ? UpdateVersions.Display(available.Version) : "";

    public string UpdateBannerText => _updates?.Phase switch
    {
        UpdatePhase.Downloading => "Downloading the update…",
        UpdatePhase.Failed when _updates.Error is { } error => error,
        _ => $"Job Tracker {AvailableVersionText} is available.",
    };

    public bool CheckForUpdates
    {
        get => _settings.CheckForUpdates;
        set
        {
            if (_settings.CheckForUpdates == value)
                return;
            _settings.CheckForUpdates = value;
            Try(() => _settings.Save());
            OnPropertyChanged();
        }
    }

    /// <summary>The automatic look for a newer version when the app starts (the service decides whether it is due).</summary>
    public async Task StartupUpdateCheckAsync()
    {
        if (_updates is not null)
            await _updates.CheckOnStartupAsync();
    }

    public Task InstallUpdateAsync() => _updates?.InstallAsync() ?? Task.CompletedTask;

    [RelayCommand]
    private async Task CheckForUpdatesNowAsync()
    {
        if (_updates is null)
            return;
        _bannerDismissed = false;
        await _updates.CheckAsync(manual: true);
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        _bannerDismissed = true;
        RaiseUpdateChanged();
    }

    [RelayCommand]
    private void OpenReleasePage() => _updates?.OpenReleasePage();

    [RelayCommand]
    private void OpenRepository() =>
        new ShellUrlOpener(new ShellProcessLauncher()).Open($"https://github.com/{AppInfo.Owner}/{AppInfo.Repo}");

    private void RaiseUpdateChanged()
    {
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(IsDownloadingUpdate));
        OnPropertyChanged(nameof(CanChooseUpdateAction));
        OnPropertyChanged(nameof(UpdateProgressPercent));
        OnPropertyChanged(nameof(CanInstallInPlace));
        OnPropertyChanged(nameof(UpdateActionText));
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(AvailableVersionText));
        OnPropertyChanged(nameof(UpdateBannerText));
    }
}
