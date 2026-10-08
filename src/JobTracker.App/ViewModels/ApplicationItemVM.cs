using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core;

namespace JobTracker.App.ViewModels;

/// <summary>One application in the list, plus its JD, resume and notes once selected.</summary>
public sealed partial class ApplicationItemVM : ObservableObject
{
    private bool _loadingNotes;

    public ApplicationItemVM(JobApplication app, MainViewModel main)
    {
        App = app;
        Main = main;
        Jd = new DocumentVM(this, BulletSource.Jd);
        Resume = new DocumentVM(this, BulletSource.Resume);
    }

    public JobApplication App { get; }
    public MainViewModel Main { get; }
    public DocumentVM Jd { get; }
    public DocumentVM Resume { get; }

    public string Company => App.Meta.Company;
    public string Role => App.Meta.Role;
    public string DateText => App.Meta.AppliedDate.ToString("yyyy-MM-dd");
    public string FolderPath => App.FolderPath;

    public bool HasUrl => IsWebUrl(App.Meta.Url);

    public ApplicationStatus Status
    {
        get => App.Meta.Status;
        set
        {
            if (App.Meta.Status == value)
                return;
            App.Meta.Status = value;
            OnPropertyChanged();
            Main.Try(() => Main.Store.SaveMeta(App));
        }
    }

    public bool IsStarred
    {
        get => App.Meta.Starred;
        set
        {
            if (App.Meta.Starred == value)
                return;
            App.Meta.Starred = value;
            OnPropertyChanged();
            Main.Try(() => Main.Store.SaveMeta(App));
        }
    }

    [ObservableProperty] private string _notes = "";

    public void LoadDetail()
    {
        Main.Try(() =>
        {
            Jd.Load();
            Resume.Load();
            _loadingNotes = true;
            Notes = Main.Store.ReadNotes(App);
            _loadingNotes = false;
        });
    }

    public void RefreshStars()
    {
        Jd.RefreshStars();
        Resume.RefreshStars();
    }

    partial void OnNotesChanged(string value)
    {
        if (_loadingNotes)
            return;
        // The TextBox commits on lost focus, so this fires when the user clicks away.
        Main.Try(() => Main.Store.SaveNotes(App, value));
        Main.ShowToast("Notes saved");
    }

    [RelayCommand]
    private void OpenFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { App.FolderPath } });

    [RelayCommand]
    private void OpenUrl()
    {
        // Only http(s): meta.json is a plain file and could hold anything.
        if (HasUrl)
            Process.Start(new ProcessStartInfo(App.Meta.Url!) { UseShellExecute = true });
    }

    private static bool IsWebUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
