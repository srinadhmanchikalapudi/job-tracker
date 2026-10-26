using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core.Export;
using JobTracker.Core.Tailoring;

namespace JobTracker.App.ViewModels;

/// <summary>One requirement of the job, as a chip on the Match tab and a cell on the coverage map. Clicking it opens its detail.</summary>
public sealed partial class TermVM(Requirement requirement, MatchVM owner) : ObservableObject
{
    public Requirement Requirement { get; } = requirement;
    public TermMatch Match => Requirement.Match;
    public string Skill => Requirement.Name;
    public MatchLevel Level => Requirement.Level;
    public bool IsMissing => Level == MatchLevel.Missing;
    public bool IsAttested => Level == MatchLevel.Claimed;
    public bool IsCovered => Level == MatchLevel.Strong;
    public bool IsPartial => Level == MatchLevel.Partial;

    /// <summary>A small word after the skill when the job description marks it: "required" or "nice to have".</summary>
    public string Tag => Match.Term.Weight switch
    {
        TermWeight.Required => "required",
        TermWeight.Preferred => "nice to have",
        _ => "",
    };

    public bool HasTag => Tag.Length > 0;

    /// <summary>How much it matters for this kind of job, as a word.</summary>
    public string TierText => Requirement.Tier switch { Tier.Critical => "critical", Tier.Important => "important", _ => "optional" };

    /// <summary>The mark on the coverage map.</summary>
    public string Symbol => Level switch { MatchLevel.Strong => "✓", MatchLevel.Partial => "~", MatchLevel.Claimed => "!", _ => "✗" };

    public string LevelWord => Level switch { MatchLevel.Strong => "Strong match", MatchLevel.Partial => "Partial match", MatchLevel.Claimed => "Not written yet", _ => "Missing" };

    public string Tooltip => Level switch
    {
        MatchLevel.Strong => (Match.ImpliedBy is { } by ? $"Counted because your resume says {by}.\n" : "") + $"In your resume: {Match.ResumeLines.FirstOrDefault()}\nJob description: {Match.Term.Evidence}",
        MatchLevel.Partial => $"Related wording: {Match.ResumeLines.FirstOrDefault()}\nIt is close to {Skill}, but does not say it.\nJob description: {Match.Term.Evidence}",
        MatchLevel.Claimed => (Match.Note is { Length: > 0 } note ? $"You said: {note}" : "You said you have used this.") + $"\nJob description: {Match.Term.Evidence}",
        _ => $"Job description: {Match.Term.Evidence}",
    } + $"\n{TierText} for this kind of job. Click for details.";

    public string Note => Match.Note ?? "";

    [ObservableProperty] private bool _isSelected;

    [RelayCommand]
    private void Select() => owner.Select(this);

    [RelayCommand]
    private void Undo() => owner.Unattest(Skill);

    [RelayCommand]
    private void Hide() => owner.Dismiss(Skill);
}

/// <summary>One group of the coverage map: a heading and the requirements under it.</summary>
public sealed record MapGroupVM(string Label, IReadOnlyList<TermVM> Cells);

/// <summary>One bar beside the overall score ("Cloud Match 80%").</summary>
public sealed record ScoreBarVM(string Label, int Percent);

/// <summary>One line of "why this score": "+27 core skills" with what it came from.</summary>
public sealed record BreakdownRowVM(bool Gained, string PointsText, string Label, string Detail);

/// <summary>An ATS risk: how serious, and what.</summary>
public sealed record RiskRowVM(RiskLevel Level, string LevelText, string Text, TermVM? Term);

/// <summary>One step of the score history.</summary>
public sealed record HistoryRowVM(string Label, int Overall, string Summary, bool IsCurrent, string When, IReadOnlyList<string> Changes)
{
    public string Score => $"{Overall}%";
    public bool HasChanges => Changes.Count > 0;
    public string ChangesText => string.Join("\n", Changes);
}

/// <summary>One of the top missing keywords with its place in the list ("1. Azure Functions").</summary>
public sealed record RankedTermVM(int Rank, TermVM Term);

/// <summary>A kind of job the person can score for, or "detect it from the title".</summary>
public sealed record RoleOptionVM(string? Id, string Name);

/// <summary>What the detail panel shows for the requirement that was clicked.</summary>
public sealed class RequirementDetailVM
{
    public required string Title { get; init; }
    public required string LevelText { get; init; }
    public required MatchLevel Level { get; init; }
    public string Meta { get; init; } = "";
    public string JobDescriptionSays { get; init; } = "";
    public string Lead { get; init; } = "";
    public IReadOnlyList<string> Lines { get; init; } = [];
    public string RelatedLead { get; init; } = "";
    public IReadOnlyList<string> RelatedLines { get; init; } = [];
    public string SourceText { get; init; } = "";
    public string SourceExplanation { get; init; } = "";
    public string Guidance { get; init; } = "";
    public bool CanConfirm { get; init; }
    public bool CanWrite { get; init; }
    public bool CanUndo { get; init; }
    public bool HasLines => Lines.Count > 0;
    public bool HasRelated => RelatedLines.Count > 0;
    public bool HasSource => SourceText.Length > 0;
    public bool HasLead => Lead.Length > 0;
    public bool HasGuidance => Guidance.Length > 0;
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

/// <summary>The Match tab of one application: how the resume scores against the job description and why, what is missing, and the saved versions.</summary>
public sealed partial class MatchVM(ApplicationItemVM owner) : ObservableObject
{
    private static readonly RoleOptionVM Detect = new(null, "Detect from the job title");

    private TailorState _state = new();
    private ScoreReport _report = AtsScorer.Score(new JdAnalysis([]), "", "");
    private bool _loading;
    private string? _selectedSkill;

    public ObservableCollection<TermVM> Missing { get; } = [];
    public ObservableCollection<TermVM> Partial { get; } = [];
    public ObservableCollection<TermVM> Attested { get; } = [];
    public ObservableCollection<TermVM> Covered { get; } = [];
    public ObservableCollection<VersionVM> Versions { get; } = [];
    public ObservableCollection<ScoreBarVM> Bars { get; } = [];
    public ObservableCollection<BreakdownRowVM> Breakdown { get; } = [];
    public ObservableCollection<RiskRowVM> Risks { get; } = [];
    public ObservableCollection<RankedTermVM> TopMissing { get; } = [];
    public ObservableCollection<MapGroupVM> Map { get; } = [];
    public ObservableCollection<HistoryRowVM> History { get; } = [];
    public IReadOnlyList<RoleOptionVM> RoleOptions { get; } = [Detect, .. RoleProfiles.All.Select(p => new RoleOptionVM(p.Id, p.Name))];

    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private int _percent;
    [ObservableProperty] private bool _hasTerms;
    [ObservableProperty] private int _hiddenCount;
    [ObservableProperty] private bool _hasVersions;
    [ObservableProperty] private string _roleSummary = "";
    [ObservableProperty] private RoleOptionVM _selectedRole = Detect;
    [ObservableProperty] private string _whyTitle = "";
    [ObservableProperty] private string _moreRisks = "";
    [ObservableProperty] private TermVM? _selected;
    [ObservableProperty] private RequirementDetailVM? _selectedDetail;

    /// <summary>The resume in use, shown at the top of the versions: what it is, how it scores, and the name its Word file will have.</summary>
    [ObservableProperty] private string _currentDetail = "";
    [ObservableProperty] private string _currentFileName = "";
    [ObservableProperty] private string _currentFileState = "";
    [ObservableProperty] private bool _hasCurrent;

    public ApplicationItemVM Owner => owner;
    public ScoreReport Report => _report;
    public TailorState State => _state;

    public bool HasMissing => Missing.Count > 0;
    public bool HasPartial => Partial.Count > 0;
    public bool HasAttested => Attested.Count > 0;
    public bool HasCovered => Covered.Count > 0;
    public bool HasHidden => HiddenCount > 0;
    public bool NoTerms => !HasTerms;
    public bool HasSelection => Selected is not null;
    public bool NoSelection => Selected is null;
    public bool HasRisks => Risks.Count > 0;
    public bool HasTopMissing => TopMissing.Count > 0;
    public bool HasHistory => History.Count > 1;
    public bool HasMoreRisks => MoreRisks.Length > 0;

    /// <summary>Reads the job description, resume and saved choices again and rebuilds everything on the tab.</summary>
    public void Refresh()
    {
        var main = owner.Main;
        main.Try(() =>
        {
            _loading = true;
            _scored.Clear(); // the job description, the choices or the resume may have changed
            _state = TailorState.Load(owner.App.FolderPath);
            var jd = JdAnalyzer.Analyze(main.Store.ReadJd(owner.App));
            var current = main.Store.ReadResume(owner.App);
            _report = Score(jd, current);

            var terms = _report.Requirements.Select(r => new TermVM(r, this)).ToList();
            Fill(Missing, terms.Where(t => t.IsMissing));
            Fill(Partial, terms.Where(t => t.IsPartial));
            Fill(Attested, terms.Where(t => t.IsAttested));
            Fill(Covered, terms.Where(t => t.IsCovered));

            HasTerms = _report.HasRequirements;
            Percent = _report.Overall;
            HiddenCount = _state.Dismissed.Count;
            Headline = !HasTerms ? "No requirements recognised in the job description" : $"{_report.Matched} of {_report.Total} job requirements matched";
            Detail = BuildDetail();

            // role
            SelectedRole = RoleOptions.FirstOrDefault(o => o.Id is not null && string.Equals(o.Id, _state.RoleId, StringComparison.OrdinalIgnoreCase)) ?? Detect;
            RoleSummary = $"Scored as a {_report.Role.Profile.Name}. {_report.Role.Reason}";

            // score, why, risks, keywords, map
            Fill(Bars, _report.Bars.Select(b => new ScoreBarVM(b.Label, b.Percent)));
            WhyTitle = HasTerms ? $"WHY {_report.Overall}%" : "";
            Fill(Breakdown, _report.Breakdown.Select(l => new BreakdownRowVM(l.Gained, (l.Gained ? "+" : "−") + l.Points, l.Label, l.Detail)));
            var byName = terms.ToDictionary(t => t.Skill, StringComparer.OrdinalIgnoreCase);
            Fill(Risks, _report.Risks.Take(8).Select(r => new RiskRowVM(r.Level, r.Level.ToString(), r.Text, byName.GetValueOrDefault(r.Name))));
            MoreRisks = _report.Risks.Count > 8 ? $"and {_report.Risks.Count - 8} more" : "";
            Fill(TopMissing, _report.TopMissing.Select((r, i) => new RankedTermVM(i + 1, byName[r.Name])));
            Fill(Map, terms
                .GroupBy(t => SkillCategories.MapLabel(t.Requirement.Category))
                .OrderBy(g => SkillCategories.MapOrder(g.Key))
                .Select(g => new MapGroupVM(g.Key, g.OrderByDescending(t => t.Requirement.Weight).ToList())));

            // the selection survives a refresh when the requirement is still there
            Selected = null;
            if (_selectedSkill is not null && byName.TryGetValue(_selectedSkill, out var again))
                Select(again);
            else
                SelectedDetail = null;

            // versions and history
            Fill(Versions, ResumeVersions.List(owner.App.FolderPath).OrderByDescending(v => v.Number).Select(v => new VersionVM(v, this, ScoreOf(v, jd, current))));
            HasVersions = Versions.Count > 0;
            HasCurrent = current.Trim().Length > 0;
            CurrentFileName = HasCurrent ? DownloadNames.Suggest(current) : "";
            CurrentFileState = !HasCurrent ? "" : CurrentResumeWord.ExistingPath(owner.App) is { } made && Path.GetFileName(made) == CurrentFileName
                ? " (ready in this application's folder)"
                : " (not made yet: click Word document)";
            var same = Versions.FirstOrDefault(v => v.IsCurrent);
            CurrentDetail = !HasCurrent ? "" : (same is not null ? $"Same as {same.Name}." : "The resume you pasted or attached.") + (HasTerms ? $" {Percent}% overall match." : "");
            BuildHistory(jd, current);
        });

        _loading = false;
        OnPropertyChanged(nameof(HasMissing));
        OnPropertyChanged(nameof(HasPartial));
        OnPropertyChanged(nameof(HasAttested));
        OnPropertyChanged(nameof(HasCovered));
        OnPropertyChanged(nameof(HasHidden));
        OnPropertyChanged(nameof(NoTerms));
        OnPropertyChanged(nameof(HasRisks));
        OnPropertyChanged(nameof(HasTopMissing));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasMoreRisks));
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    // Each version is scored once per refresh, whoever asks: its row on the versions list and its place in the history use the same report.
    private readonly Dictionary<string, ScoreReport> _scored = new(StringComparer.Ordinal);

    private ScoreReport Score(JdAnalysis jd, string resumeText)
    {
        if (!_scored.TryGetValue(resumeText, out var report))
            _scored[resumeText] = report = AtsScorer.Score(jd, resumeText, owner.Role, _state);
        return report;
    }

    // ---------------------------------------------------------------- the person's choices

    partial void OnSelectedRoleChanged(RoleOptionVM value)
    {
        if (_loading || string.Equals(value.Id, _state.RoleId, StringComparison.OrdinalIgnoreCase))
            return;
        _state.RoleId = value.Id;
        Save();
    }

    /// <summary>Opens the detail of a requirement: where it is found, or what is nearby, and what the person can do about it.</summary>
    public void Select(TermVM term)
    {
        if (Selected is not null)
            Selected.IsSelected = false;
        Selected = term;
        term.IsSelected = true;
        _selectedSkill = term.Skill;
        SelectedDetail = BuildSelectedDetail(term);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(NoSelection));
    }

    [RelayCommand]
    private void CloseDetail()
    {
        if (Selected is not null)
            Selected.IsSelected = false;
        Selected = null;
        _selectedSkill = null;
        SelectedDetail = null;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(NoSelection));
    }

    private RequirementDetailVM BuildSelectedDetail(TermVM term)
    {
        var r = term.Requirement;
        var m = r.Match;
        static string Line(EvidenceLine e, bool showWhy) => e.Text + (e.InSkillsList ? "  (in your skills list)" : "") + (showWhy && e.Why.Length > 0 ? $"  — recognised: {e.Why}" : "");
        var meta = $"{term.TierText} for a {_report.Role.Profile.Name} · " + (m.Term.Weight switch { TermWeight.Required => "the job requires it", TermWeight.Preferred => "nice to have", _ => "part of the work" }) +
                   $" · worth up to {r.MaxPoints:0.#} points";

        var (source, why) = r.Source switch
        {
            RewriteSource.UserConfirmed => ("USER CONFIRMED", "You said you have used it. Only you can say that, and it is the only thing a bullet may be built from."),
            RewriteSource.SystemInferred => ("SYSTEM INFERRED", "Related wording or work is in your resume, but you have not said you used " + r.Name + ". Nothing is added until you do."),
            RewriteSource.JobDescriptionOnly => ("JOB DESCRIPTION ONLY", "Only the job description asks for it. Nothing in your resume points to it, so nothing will be written about it unless you tell me you used it."),
            _ => ("", ""),
        };

        return r.Level switch
        {
            MatchLevel.Strong => new RequirementDetailVM
            {
                Title = r.Name, Level = r.Level, LevelText = m.ImpliedBy is null ? "MATCHED" : "MATCHED (implied)", Meta = meta, JobDescriptionSays = m.Term.Evidence,
                Lead = m.ImpliedBy is { } by ? $"Counted because your resume says {by}, which means {r.Name}. Found in:" : "Found in your resume:",
                Lines = m.EvidenceLines.Take(6).Select(e => Line(e, false)).ToList(),
                Guidance = m.UsedInExperience ? "" : "It only appears in a skills list. An ATS counts it, but a bullet that shows you using it counts for more.",
            },
            MatchLevel.Partial => new RequirementDetailVM
            {
                Title = r.Name, Level = r.Level, LevelText = "PARTIAL MATCH", Meta = meta, JobDescriptionSays = m.Term.Evidence,
                Lead = $"Not found by name. Related wording found (counts half):", Lines = m.EvidenceLines.Take(6).Select(e => Line(e, true)).ToList(),
                SourceText = source, SourceExplanation = why,
                Guidance = $"An ATS may not count this as {r.Name}. If you really used {r.Name}, click I've used it and say where. If you did not, leave it as it is.",
                CanConfirm = true,
            },
            MatchLevel.Claimed => new RequirementDetailVM
            {
                Title = r.Name, Level = r.Level, LevelText = "YOU SAID YOU HAVE USED IT", Meta = meta, JobDescriptionSays = m.Term.Evidence,
                Lead = (m.Note is { Length: > 0 } note ? $"You said: “{note}”. " : "") + "An ATS reads only your resume, so it cannot see this yet.",
                RelatedLead = m.EvidenceLines.Count > 0 ? "Related wording already in your resume:" : "",
                RelatedLines = m.EvidenceLines.Take(4).Select(e => Line(e, true)).ToList(),
                SourceText = source, SourceExplanation = why,
                Guidance = "Write a bullet for it. It is built only from what you said, you can edit it before it is added, and it is checked again after you edit.",
                CanWrite = true, CanUndo = true,
            },
            _ => new RequirementDetailVM
            {
                Title = r.Name, Level = r.Level, LevelText = "NOT FOUND IN YOUR RESUME", Meta = meta, JobDescriptionSays = m.Term.Evidence,
                Lead = "Not found in your resume.",
                RelatedLead = m.RelatedLines.Count > 0 ? "Potential evidence detected (related work, not proof):" : "Nothing in your resume points to it.",
                RelatedLines = m.RelatedLines.Take(5).Select(e => Line(e, false)).ToList(),
                SourceText = source, SourceExplanation = why,
                Guidance = m.RelatedLines.Count > 0
                    ? $"If you have used {r.Name} in this work, click I've used it and say how. Then I can help you write a bullet. Nothing is added for a technology you have not confirmed."
                    : $"If you have used {r.Name}, click I've used it. If you have not, it is better to leave it out than to match it.",
                CanConfirm = true,
            },
        };
    }

    private string BuildDetail()
    {
        if (!HasTerms)
            return "The job description may use words this app does not know yet, or it may be empty. Requirements are found by a fixed list, so nothing is guessed.";

        var parts = new List<string>();
        if (_report.PartlyMatched > 0)
            parts.Add($"{_report.PartlyMatched} more {(_report.PartlyMatched == 1 ? "is" : "are")} partly matched by related wording.");
        if (_report.Claimed > 0)
            parts.Add($"{_report.Claimed} you have used but have not written in your resume yet.");
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
        if (string.Equals(_selectedSkill, skill, StringComparison.OrdinalIgnoreCase))
            _selectedSkill = null;
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

    // ---------------------------------------------------------------- versions and history

    /// <summary>The score a saved version would give, so a better one can be seen before it is chosen. The resume itself only changes with "Use as my resume".</summary>
    private string ScoreOf(ResumeVersion version, JdAnalysis jd, string currentResume)
    {
        try
        {
            var text = ResumeVersions.Read(version);
            if (text.Trim() == currentResume.Trim())
                return "This is your resume now.";
            var report = Score(jd, text);
            if (!report.HasRequirements)
                return "";
            var delta = report.Overall - _report.Overall;
            var change = delta == 0 ? "same as your resume now" : delta > 0 ? $"up from {_report.Overall}% now" : $"down from {_report.Overall}% now";
            return $"If you use it: {report.Overall}% overall match, {change}.";
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    private void BuildHistory(JdAnalysis jd, string current)
    {
        History.Clear();
        if (!_report.HasRequirements || current.Trim().Length == 0)
            return;

        var sources = new List<HistorySource>();
        foreach (var v in ResumeVersions.List(owner.App.FolderPath))
        {
            try
            {
                sources.Add(new HistorySource($"Version {v.Number}", v.SavedAt, ResumeVersions.Read(v), false));
            }
            catch (IOException)
            {
                // a version that cannot be read is left out of the history
            }
        }

        var written = File.Exists(owner.App.ResumePath) ? File.GetLastWriteTime(owner.App.ResumePath) : DateTime.Now;
        sources.Add(new HistorySource("Your resume now", written, current, true));

        foreach (var entry in ScoreHistory.Build(sources, text => Score(jd, text)))
            History.Add(new HistoryRowVM(entry.Label, entry.Overall, entry.Summary, entry.IsCurrent, entry.At.ToString("yyyy-MM-dd HH:mm"),
                entry.Changes.Select(c => $"{(c.Points >= 0 ? "+" : "−")}{Math.Abs(c.Points):0.#}  {c.Text}").ToList()));
    }

    // ---------------------------------------------------------------- Word files

    /// <summary>Makes the Word document of a saved version, or of the resume as it is now when <paramref name="version"/> is null.</summary>
    public WordPreview? PrepareWordPreview(ResumeVersion? version)
    {
        var main = owner.Main;
        var current = main.Store.ReadResume(owner.App);
        var text = version is null ? current : ResumeVersions.Read(version);

        // The resume in use (or a version that says exactly the same) is the upload-ready file. Any other version is only previewed, in its own place,
        // so it can never replace the file of the resume in use; the preview offers to make it the resume.
        if (version is null || text.Trim() == current.Trim())
            return WordPreviews.MakeCurrent(main, owner.App, version is null ? "Your resume as it is now" : $"Version {version.Number} of your resume (the one in use)");

        return WordPreviews.Make(main, owner.App.FolderPath, text, $"Version {version.Number} of your resume",
            Path.Combine("Word versions", DownloadNames.ForVersion(text, version.Number)), () => UseAsResume(version));
    }

    /// <summary>
    /// Makes the upload-ready Word file for the resume in use, replacing the old one. Returns a sentence to tell the person when it could not be
    /// made (the old file is open in Word), otherwise null. Call it whenever the resume in use changes.
    /// </summary>
    public string? RefreshWordFile()
    {
        try
        {
            CurrentResumeWord.Refresh(owner.Main.Store, owner.App);
            return null;
        }
        catch (IOException)
        {
            return $"{DownloadNames.Suggest(owner.Main.Store.ReadResume(owner.App))} is open in another program, so it was not updated. Close it there, then click Word document";
        }
        catch (UnauthorizedAccessException ex)
        {
            return "Could not write the Word file: " + ex.Message;
        }
    }

    public void UseAsResume(ResumeVersion version)
    {
        owner.Main.Try(() =>
        {
            var backup = ResumeVersions.UseAsResume(owner.App.FolderPath, version);
            owner.Resume.Load();
            var problem = RefreshWordFile(); // the upload-ready file now follows the resume that was just chosen
            Refresh();
            var said = backup is null ? $"Version {version.Number} is now your resume" : $"Version {version.Number} is now your resume. The old one is kept as {backup.FileName}";
            owner.Main.ShowToast(problem is null ? $"{said}. {CurrentFileName} is ready in its folder" : $"{said}. {problem}");
        });
    }
}