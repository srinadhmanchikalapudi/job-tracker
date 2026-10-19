using JobTracker.Core;

namespace JobTracker.App.ViewModels;

/// <summary>The part of the main view model that says whether the applications are backed up by OneDrive.</summary>
public sealed partial class MainViewModel
{
    private BackupStatus? _backup;

    /// <summary>Looks at OneDrive and the current folder again. Called when Settings opens and after the folder changes.</summary>
    public void RefreshBackupStatus()
    {
        _backup = BackupAdvisor.Evaluate(DataRoot, _knownFolders, _oneDrive);
        OnPropertyChanged(nameof(BackupHeadline));
        OnPropertyChanged(nameof(BackupDetail));
        OnPropertyChanged(nameof(BackupIsGood));
        OnPropertyChanged(nameof(BackupSuggestedFolder));
        OnPropertyChanged(nameof(CanMoveToOneDrive));
    }

    public string BackupHeadline => _backup?.Headline ?? "";
    public string BackupDetail => _backup?.Detail ?? "";
    public bool BackupIsGood => _backup?.Kind == BackupKind.InOneDrive;

    /// <summary>Where the applications would go to be backed up, or null when a move would not help.</summary>
    public string? BackupSuggestedFolder => _backup?.SuggestedFolder;

    public bool CanMoveToOneDrive => BackupSuggestedFolder is not null && CanChangeFolder;
}