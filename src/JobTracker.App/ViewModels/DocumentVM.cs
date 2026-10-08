using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core;

namespace JobTracker.App.ViewModels;

/// <summary>The JD or the resume of one application, shown as starrable/copyable lines or edited as plain text.</summary>
public sealed partial class DocumentVM(ApplicationItemVM owner, BulletSource source) : ObservableObject
{
    private string _text = "";

    public BulletSource Source { get; } = source;
    public string Title => Source == BulletSource.Jd ? "job description" : "resume";
    public ObservableCollection<LineVM> Lines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsViewing), nameof(ShowEmptyHint))]
    private bool _isEditing;

    [ObservableProperty] private string _editText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyHint))]
    private bool _isEmpty;

    public bool IsViewing => !IsEditing;
    public bool ShowEmptyHint => IsEmpty && !IsEditing;

    public void Load()
    {
        IsEditing = false;
        _text = Source == BulletSource.Jd ? owner.Main.Store.ReadJd(owner.App) : owner.Main.Store.ReadResume(owner.App);
        Rebuild();
    }

    public void RefreshStars()
    {
        var starred = StarredKeys();
        foreach (var line in Lines)
            line.IsStarred = starred.Contains(BulletText.Key(line.Text));
    }

    public bool ToggleStar(string line)
    {
        var nowStarred = false;
        owner.Main.Try(() =>
            nowStarred = owner.Main.BulletLibrary.Toggle(
                owner.Main.Store.RelativeFolder(owner.App), owner.Company, owner.Role, Source, line));
        owner.Main.RefreshBullets();
        owner.Main.ShowToast(nowStarred ? "Added to starred bullets" : "Removed from starred bullets");
        return nowStarred;
    }

    public void Copy(string line) => owner.Main.Copy(BulletText.Clean(line));

    private HashSet<string> StarredKeys()
    {
        var folder = owner.Main.Store.RelativeFolder(owner.App);
        return owner.Main.BulletLibrary.List()
            .Where(b => b.Source == Source && string.Equals(b.AppFolder, folder, StringComparison.OrdinalIgnoreCase))
            .Select(b => BulletText.Key(b.Text))
            .ToHashSet();
    }

    private void Rebuild()
    {
        var main = owner.Main;
        var terms = main.Terms;
        var searching = main.SearchCovers(Source);
        var starred = StarredKeys();

        var onlyMatches = searching && main.OnlyMatchingLines;

        Lines.Clear();
        foreach (var line in BulletText.Structure(_text))
        {
            var isMatch = searching && line.Kind != LineKind.Blank &&
                          terms.Any(t => line.Raw.Contains(t, StringComparison.OrdinalIgnoreCase));
            if (onlyMatches && !isMatch)
                continue;
            Lines.Add(new LineVM(this, line, isMatch, line.IsActionable && starred.Contains(BulletText.Key(line.Raw))));
        }

        IsEmpty = string.IsNullOrWhiteSpace(_text);
    }

    [RelayCommand]
    private void Edit()
    {
        EditText = _text;
        IsEditing = true;
    }

    [RelayCommand]
    private void Save()
    {
        owner.Main.Try(() =>
        {
            if (Source == BulletSource.Jd)
                owner.Main.Store.SaveJd(owner.App, EditText);
            else
                owner.Main.Store.SaveResume(owner.App, EditText);
            _text = EditText;
            IsEditing = false;
            Rebuild();
            owner.Main.ShowToast($"Saved {Title}");
        });
    }

    [RelayCommand]
    private void Cancel() => IsEditing = false;
}
