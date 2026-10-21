using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core.Export;
using JobTracker.Core.Tailoring;

namespace JobTracker.App.ViewModels;

/// <summary>One skill from the job description, as a chip on the Match tab.</summary>
public sealed partial class TermVM(TermMatch match, MatchVM owner) : ObservableObject
{
    public TermMatch Match { get; } = match;
    public string Skill => Match.Term.Skill;
    public bool IsMissing => Match.Status == MatchStatus.Missing;
    public bool IsAttested => Match.Status == MatchStatus.Attested;
    public bool IsCovered => Match.Status == MatchStatus.InResume;

    /// <summary>A small word after the skill when the job description marks it: "required" or "nice to have".</summary>
    public string Tag => Match.Term.Weight switch
    {
        TermWeight.Required => "required",
        TermWeight.Preferred => "nice to have",
        _ => "",
    };

    public bool HasTag => Tag.Length > 0;

    public string Tooltip => Match.Status switch
    {
        MatchStatus.InResume => $"In your resume: {Match.ResumeLines.FirstOrDefault()}\nJob description: {Match.Term.Evidence}",
        MatchStatus.Attested => (Match.Note is { Length: > 0 } note ? $"You said: {note}" : "You said you have used this.") + $"\nJob description: {Match.Term.Evidence}",
        _ => $"Job description: {Match.Term.Evidence}",
    };

    public string Note => Match.Note ?? "";

    [RelayCommand]
    private void Undo() => owner.Unattest(Skill);

    [RelayCommand]
    private void Hide() => owner.Dismiss(Skill);
}

public sealed partial class VersionVM(ResumeVersion version, MatchVM owner, string score = "") : ObservableObject
{
    /// <summary>What the match would be if this version were the resume, counted from its text. Empty when it cannot be read.</summary>
    public string Score { get; } = score;

    /// <summary>True when this version is the resume in use right now.</summary>
    public bool IsCurrent => Score == "This is your resume now.";
    public bool HasScore => Score.Length > 0;

    public ResumeVersion Version { get; } = version;
    public string Name => $"Version {Version.Number}";
    public string Detail => $"{Version.FileName} · saved {Version.SavedAt:yyyy-MM-dd HH:mm}";

    [RelayCommand]
    private void Show() =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", Version.Path } });

    [RelayCommand]
    private void UseAsResume() => owner.UseAsResume(Version);

    public WordPreview? PrepareWordPreview() => owner.PrepareWordPreview(Version);
}

/// <summary>The Match tab of one application: the job description's skills against the resume, what the person has claimed, and the saved versions.</summary>
public sealed partial class MatchVM(ApplicationItemVM owner) : ObservableObject
{
    private TailorState _state = new();
    private MatchReport _report = new([]);

    public ObservableCollection<TermVM> Missing { get; } = [];
    public ObservableCollection<TermVM> Attested { get; } = [];
    public ObservableCollection<TermVM> Covered { get; } = [];
    public ObservableCollection<VersionVM> Versions { get; } = [];

    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private int _percent;
    [ObservableProperty] private bool _hasTerms;
    [ObservableProperty] private int _hiddenCount;
    [ObservableProperty] private bool _hasVersions;

    /// <summary>The resume in use, shown at the top of the versions: what it is, how it scores, and the name its Word file will have.</summary>
    [ObservableProperty] private string _currentDetail = "";
    [ObservableProperty] private string _currentFileName = "";
    [ObservableProperty] private bool _hasCurrent;

    public ApplicationItemVM Owner => owner;
    public MatchReport Report => _report;
    public TailorState State => _state;

    public bool HasMissing => Missing.Count > 0;
    public bool HasAttested => Attested.Count > 0;
    public bool HasCovered => Covered.Count > 0;
    public bool HasHidden => HiddenCount > 0;
    public bool NoTerms => !HasTerms;

    /// <summary>Reads the job description, resume and saved choices again and rebuilds the chips.</summary>
    public void Refresh()
    {
        var main = owner.Main;
        main.Try(() =>
        {
            _state = TailorState.Load(owner.App.FolderPath);
            _report = ResumeMatcher.Match(JdAnalyzer.Analyze(main.Store.ReadJd(owner.App)), main.Store.ReadResume(owner.App), _state);

            Missing.Clear();
            Attested.Clear();
            Covered.Clear();
            foreach (var m in _report.Matches)
            {
                var chip = new TermVM(m, this);
                (m.Status switch { MatchStatus.InResume => Covered, MatchStatus.Attested => Attested, _ => Missing }).Add(chip);
            }

            HasTerms = _report.Matches.Count > 0;
            Percent = _report.CoreCoveragePercent;
            HiddenCount = _state.Dismissed.Count;

            Headline = !HasTerms
                ? "No skills recognised in the job description"
                : _report.CoreTotal == 0
                    ? "Only nice-to-have skills were found"
                    : $"{_report.CoreInResume} of {_report.CoreTotal} key skills are already in your resume";

            Detail = BuildDetail();
            Versions.Clear();
            var jd = JdAnalyzer.Analyze(main.Store.ReadJd(owner.App));
            var current = main.Store.ReadResume(owner.App);
            foreach (var v in ResumeVersions.List(owner.App.FolderPath).OrderByDescending(v => v.Number))
                Versions.Add(new VersionVM(v, this, ScoreOf(v, jd, current)));
            HasVersions = Versions.Count > 0;
            HasCurrent = current.Trim().Length > 0;
            CurrentFileName = HasCurrent ? DownloadNames.Suggest(current) : "";
            var same = Versions.FirstOrDefault(v => v.IsCurrent);
            var scoreText = _report.CoreTotal > 0 ? $" {Percent}% ({_report.CoreInResume} of {_report.CoreTotal} key skills)." : "";
            CurrentDetail = !HasCurrent ? "" : (same is not null ? $"Same as {same.Name}." : "The resume you pasted or attached.") + scoreText;
        });

        OnPropertyChanged(nameof(HasMissing));
        OnPropertyChanged(nameof(HasAttested));
        OnPropertyChanged(nameof(HasCovered));
        OnPropertyChanged(nameof(HasHidden));
        OnPropertyChanged(nameof(NoTerms));
    }

    /// <summary>The score a saved version would give, so a better one can be seen before it is chosen. The resume itself only changes with "Use as my resume".</summary>
    private string ScoreOf(ResumeVersion version, JobTracker.Core.Tailoring.JdAnalysis jd, string currentResume)
    {
        try
        {
            var text = ResumeVersions.Read(version);
            if (text.Trim() == currentResume.Trim())
                return "This is your resume now.";
            var report = ResumeMatcher.Match(jd, text, _state);
            if (report.CoreTotal == 0)
                return "";
            var delta = report.CoreCoveragePercent - _report.CoreCoveragePercent;
            var change = delta == 0 ? "same as your resume now" : delta > 0 ? $"up from {_report.CoreCoveragePercent}% now" : $"down from {_report.CoreCoveragePercent}% now";
            return $"If you use it: {report.CoreCoveragePercent}% ({report.CoreInResume} of {report.CoreTotal} key skills), {change}.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private string BuildDetail()
    {
        if (!HasTerms)
            return "The job description may use words this app does not know yet, or it may be empty. Skills are found by a fixed list, so nothing is guessed.";

        var parts = new List<string>();
        if (_report.PreferredTotal > 0)
            parts.Add($"Nice to have: {_report.PreferredInResume} of {_report.PreferredTotal}.");
        if (_report.CoreAttested > 0)
            parts.Add($"{_report.CoreAttested} more key skill{(_report.CoreAttested == 1 ? "" : "s")} you have used but have not written in your resume yet.");
        return parts.Count == 0 ? "Counted from the words in the job description and in your resume." : string.Join(" ", parts);
    }

    partial void OnHiddenCountChanged(int value) => OnPropertyChanged(nameof(HasHidden));

    public void Attest(string skill, string note)
    {
        _state.Attest(skill, note);
        Save();
    }

    public void Unattest(string skill)
    {
        _state.Unattest(skill);
        Save();
    }

    public void Dismiss(string skill)
    {
        _state.Dismiss(skill);
        Save();
    }

    [RelayCommand]
    private void RestoreHidden()
    {
        foreach (var skill in _state.Dismissed.ToList())
            _state.Restore(skill);
        Save();
    }

    private void Save()
    {
        owner.Main.Try(() => _state.Save(owner.App.FolderPath));
        Refresh();
    }

    /// <summary>Makes the Word document of a saved version, or of the resume as it is now when <paramref name="version"/> is null.</summary>
    public WordPreview? PrepareWordPreview(ResumeVersion? version)
    {
        var main = owner.Main;
        var folder = owner.App.FolderPath;
        var text = version is null ? main.Store.ReadResume(owner.App) : ResumeVersions.Read(version);
        return WordPreviews.Make(main, folder, text, version is null ? "Your resume as it is now" : $"Version {version.Number} of your resume");
    }

    public void UseAsResume(ResumeVersion version)
    {
        owner.Main.Try(() =>
        {
            var backup = ResumeVersions.UseAsResume(owner.App.FolderPath, version);
            owner.Resume.Load();
            Refresh();
            owner.Main.ShowToast(backup is null ? $"Version {version.Number} is now your resume" : $"Version {version.Number} is now your resume. The old one is kept as {backup.FileName}");
        });
    }
}
