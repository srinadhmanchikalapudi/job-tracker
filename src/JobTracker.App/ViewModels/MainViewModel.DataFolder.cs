using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core;

namespace JobTracker.App.ViewModels;

/// <param name="Message">A sentence for the user: what happened, or why it did not.</param>
public sealed record FolderChangeResult(bool Success, string Message);

/// <summary>The part of the main view model that changes where applications are saved, optionally moving what is already there.</summary>
public sealed partial class MainViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeFolder))]
    private bool _isMovingFiles;

    [ObservableProperty] private string _moveStatus = "";
    [ObservableProperty] private double _moveProgressPercent;

    /// <summary>How many applications are saved in the folder in use now.</summary>
    public int ApplicationCount => _items.Count;

    public bool CanChangeFolder => !IsMovingFiles;

    /// <summary>
    /// Starts using <paramref name="newRoot"/>. With <paramref name="moveExisting"/> the saved applications and starred bullets are copied there
    /// first and checked, the new folder is switched to, and only then are the originals removed. If the copy fails, nothing has changed.
    /// </summary>
    public async Task<FolderChangeResult> ChangeDataRootAsync(string newRoot, bool moveExisting)
    {
        if (IsMovingFiles)
            return new FolderChangeResult(false, "Job Tracker is already moving your applications.");

        var oldRoot = DataRoot;
        if (DataRootMover.Validate(oldRoot, newRoot) is { } problem)
            return new FolderChangeResult(false, problem);

        if (!moveExisting)
        {
            try
            {
                ChangeDataRoot(newRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new FolderChangeResult(false, "The new folder could not be saved as your setting: " + ex.Message);
            }

            return new FolderChangeResult(true, $"Now saving to {newRoot}.");
        }

        IsMovingFiles = true;
        MoveProgressPercent = 0;
        MoveStatus = "Copying your applications…";
        try
        {
            var progress = new Progress<MoveProgress>(p =>
            {
                MoveProgressPercent = p.Total == 0 ? 100 : 100.0 * p.Done / p.Total;
                MoveStatus = p.Current.Length == 0 ? "Checking the copy…" : $"Copying {p.Done + 1} of {p.Total}: {p.Current}";
            });

            var result = await Task.Run(() => DataRootMover.CopyAll(oldRoot, newRoot, progress));

            try
            {
                // Back on the UI thread explicitly (not through an ambient context): this reloads the lists the window shows.
                await _dispatcher.InvokeAsync(() => ChangeDataRoot(newRoot)).Task;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The copy is complete and the originals are still in place, so nothing is lost.
                return new FolderChangeResult(false,
                    $"Your applications were copied to {newRoot}, but the new folder could not be saved as your setting ({ex.Message}). The originals were not removed.");
            }

            await _dispatcher.InvokeAsync(() => MoveStatus = "Removing the old copies…").Task;
            var warnings = await Task.Run(() => DataRootMover.DeleteOriginals(oldRoot, result));

            var count = result.Applications.Count;
            var summary = count == 1 ? "Moved 1 application" : $"Moved {count} applications";
            return warnings.Count == 0
                ? new FolderChangeResult(true, $"{summary} to {newRoot}.")
                : new FolderChangeResult(true,
                    $"{summary} to {newRoot}, but some old files could not be removed (a file may be open in another program). They are safe to delete by hand:\n" +
                    string.Join('\n', warnings.Take(5)));
        }
        catch (DataRootException ex)
        {
            return new FolderChangeResult(false, ex.Message);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() =>
            {
                IsMovingFiles = false;
                MoveStatus = "";
            }).Task;
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Try(() => Directory.CreateDirectory(DataRoot));
        if (Directory.Exists(DataRoot))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { DataRoot } });
    }
}
