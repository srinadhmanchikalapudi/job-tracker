using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core.Ai;
using JobTracker.Core.Tailoring;

namespace JobTracker.App.ViewModels;

public sealed partial class BulletRowVM(ResumeBullet bullet, bool isChanged, bool isAdded, string jobHeader) : ObservableObject
{
    public int LineNumber { get; } = bullet.LineNumber;
    public string Text { get; } = bullet.Text;
    public bool IsChanged { get; } = isChanged;
    public bool IsAdded { get; } = isAdded;
    public string JobHeader { get; } = jobHeader;
    public string Mark => IsAdded ? "Added" : "Changed";
}

/// <summary>A skill from the job description that a rewrite could speak to. Only skills the resume says, or the person has claimed, are offered.</summary>
public sealed partial class SkillPickVM(string skill, bool isClaimedOnly, bool isMissing = false, string tag = "", string evidence = "") : ObservableObject
{
    public string Skill { get; } = skill;
    public bool IsClaimedOnly { get; } = isClaimedOnly;

    /// <summary>A keyword the job description asks for that the resume does not say. It is only offered, never assumed: picking it means "I really used this".</summary>
    public bool IsMissing { get; } = isMissing;

    /// <summary>"required" or "nice to have" when the job description marks it.</summary>
    public string Tag { get; } = tag;

    public bool HasTag => Tag.Length > 0;

    public string Tooltip => IsMissing
        ? "Not in your resume. Pick it only if you really used it in this job." + (HasTag ? $" The job description marks it {Tag}." : "") + (evidence.Length > 0 ? $"\nJob description: {evidence}" : "")
        : IsClaimedOnly ? "You said you have used this. It is used only in the way your note describes." : "Already in your resume.";

    [ObservableProperty] private bool _isPicked;
}

/// <summary>One choice in the "Show" drop-down above the bullets: everything, one job, or the bullets that belong to no job.</summary>
public sealed record BulletFilter(string Label, string? Header);

public sealed class JobVM(ResumeJob job)
{
    public ResumeJob Job { get; } = job;
    public string Label => Job.Label;
}

public sealed partial class OptionVM(RewriteOption option, TailorVM owner) : ObservableObject
{
    public string Text => option.Text;
    public string Note => option.Note;
    public bool HasNote => option.Note.Length > 0;
    public IReadOnlyList<DiffPart> Diff => option.Diff;

    [RelayCommand]
    private void Use() => owner.Use(option);
}

/// <summary>One reworded bullet of a job, ticked by default; the person unticks any they do not want.</summary>
public sealed partial class JobOptionVM(JobBulletOption option) : ObservableObject
{
    public JobBulletOption Option { get; } = option;
    public string Note => Option.Note;
    public bool HasNote => Option.Note.Length > 0;
    public IReadOnlyList<DiffPart> Diff => Option.Diff;

    [ObservableProperty] private bool _isUsed = true;
}

public sealed partial class NewOptionVM(NewBulletOption option, TailorVM owner) : ObservableObject
{
    public string Text => option.Text;
    public string Note => option.Note;
    public bool HasNote => option.Note.Length > 0;

    [RelayCommand]
    private void Add() => owner.AddBullet(option);
}

/// <summary>
/// The rewrite window. Three ways to work: reword one bullet, reword a whole job's bullets together, or add new bullets to a job. Everything a model
/// proposes has passed the checks before it is shown, accepted changes go into a working copy, and the result is saved as a new version. Nothing
/// replaces resume.md until you ask.
/// </summary>
public sealed partial class TailorVM : ObservableObject
{
    private readonly MatchVM _match;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly string _jdText;
    private string _lastSavedText = "";
    private ResumeVersion? _savedVersion;

    /// <summary>Asked once per service before anything is sent: gets the destination, returns whether the person agrees.</summary>
    public Func<string, bool> ConfirmSend { get; set; } = _ => true;

    public TailorVM(MatchVM match)
    {
        _match = match;
        var main = match.Owner.Main;
        Session = new TailorSession(main.Store.ReadResume(match.Owner.App));
        _jdText = main.Store.ReadJd(match.Owner.App);
        _lastSavedText = Session.Text;

        BulletsView = CollectionViewSource.GetDefaultView(Bullets);
        BulletsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BulletRowVM.JobHeader)));
        BulletsView.Filter = o => SelectedFilter?.Header is not { } header || ((BulletRowVM)o).JobHeader == header;

        Rebuild(selectLine: null, selectJobIndex: 0);
    }

    /// <summary>
    /// The skills the job description asks for, sorted by what the resume says now: the ones it says (or the person claimed) can be pointed at; the
    /// ones it does not say are offered as keywords to pick only if they are true. Run again after every change, so a keyword that has just been
    /// written into a bullet moves to the first group. Anything already ticked stays ticked.
    /// </summary>
    private void RefreshKeywords()
    {
        var report = ResumeMatcher.Match(JdAnalyzer.Analyze(_jdText), Session.Text, _match.State);
        var usable = report.Matches.Where(m => m.Status != MatchStatus.Missing).ToList();
        var missing = report.Matches.Where(m => m.Status == MatchStatus.Missing).ToList();

        Sync(Skills, usable.Select(m => new SkillPickVM(m.Term.Skill, m.Status == MatchStatus.Attested)));
        Sync(JobSkills, usable.Select(m => new SkillPickVM(m.Term.Skill, m.Status == MatchStatus.Attested)));
        Sync(NewSkills, usable.Select(m => new SkillPickVM(m.Term.Skill, m.Status == MatchStatus.Attested)));
        Sync(NewMissingSkills, missing.Select(m => new SkillPickVM(m.Term.Skill, false, isMissing: true,
            tag: m.Term.Weight switch { TermWeight.Required => "required", TermWeight.Preferred => "nice to have", _ => "" }, evidence: m.Term.Evidence)));
        foreach (var skill in NewSkills.Concat(NewMissingSkills))
            skill.PropertyChanged += (_, _) => OnPropertyChanged(nameof(AnswersHint));
        OnPropertyChanged(nameof(HasSkills));
        OnPropertyChanged(nameof(HasNewMissing));
        OnPropertyChanged(nameof(AnswersHint));
    }

    private static void Sync(ObservableCollection<SkillPickVM> target, IEnumerable<SkillPickVM> wanted)
    {
        var ticked = target.Where(s => s.IsPicked).Select(s => s.Skill).ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.Clear();
        foreach (var skill in wanted)
        {
            skill.IsPicked = ticked.Contains(skill.Skill);
            target.Add(skill);
        }
    }

    public TailorSession Session { get; }
    public string Company => _match.Owner.Company;
    public string Role => _match.Owner.Role;

    public ObservableCollection<BulletRowVM> Bullets { get; } = [];
    public ICollectionView BulletsView { get; }
    public ObservableCollection<JobVM> Jobs { get; } = [];
    public ObservableCollection<BulletFilter> BulletFilters { get; } = [];

    /// <summary>Which bullets the list on the left shows. Only the list changes; the work tabs are not affected.</summary>
    [ObservableProperty] private BulletFilter? _selectedFilter;

    partial void OnSelectedFilterChanged(BulletFilter? value) => BulletsView.Refresh();
    public ObservableCollection<SkillPickVM> Skills { get; } = [];
    public ObservableCollection<SkillPickVM> JobSkills { get; } = [];
    public ObservableCollection<SkillPickVM> NewSkills { get; } = [];

    /// <summary>Keywords from the job description that the resume does not say, offered on the Add new bullets tab.</summary>
    public ObservableCollection<SkillPickVM> NewMissingSkills { get; } = [];

    public bool HasNewMissing => NewMissingSkills.Count > 0;

    // ---- shared
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGenerate), nameof(CanGenerateJob), nameof(CanGenerateNew), nameof(ShowIdleHint))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangesText), nameof(CanSave))]
    private int _changeCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseSaved))]
    private string _savedName = "";

    public string ChangesText
    {
        get
        {
            var changes = Session.Changes;
            var added = changes.Count(c => c.Before is null);
            var changed = changes.Count - added;
            if (changes.Count == 0)
                return "No changes yet";
            var parts = new List<string>();
            if (changed > 0)
                parts.Add(changed == 1 ? "1 bullet changed" : $"{changed} bullets changed");
            if (added > 0)
                parts.Add(added == 1 ? "1 bullet added" : $"{added} bullets added");
            return string.Join(", ", parts);
        }
    }

    public bool CanSave => ChangeCount > 0 && Session.Text != _lastSavedText;
    public bool CanUseSaved => SavedName.Length > 0;

    // ---- tab 1: one bullet
    public ObservableCollection<OptionVM> Options { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanGenerate))]
    private BulletRowVM? _selectedBullet;

    [ObservableProperty] private string _extraFacts = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _rejectedSummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIdleHint))]
    private bool _hasResult;

    public bool HasSelection => SelectedBullet is not null;
    public bool CanGenerate => HasSelection && !IsBusy;
    public bool ShowIdleHint => !IsBusy && !HasResult;
    public bool HasRejected => RejectedSummary.Length > 0;
    public bool HasSkills => Skills.Count > 0;

    partial void OnRejectedSummaryChanged(string value) => OnPropertyChanged(nameof(HasRejected));

    partial void OnSelectedBulletChanged(BulletRowVM? value)
    {
        Options.Clear();
        RejectedSummary = "";
        HasResult = false;
        Status = "";
    }

    // ---- tab 2: a whole job
    public ObservableCollection<JobOptionVM> JobOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJob), nameof(CanGenerateJob), nameof(CanGenerateNew), nameof(JobBulletCountText))]
    private JobVM? _selectedJob;

    [ObservableProperty] private string _jobExtraFacts = "";
    [ObservableProperty] private string _jobStatus = "";
    [ObservableProperty] private string _jobRejectedSummary = "";
    [ObservableProperty] private bool _hasJobResult;

    public bool HasJob => SelectedJob is not null;
    public bool HasJobs => Jobs.Count > 0;
    public bool CanGenerateJob => HasJob && !IsBusy;
    public bool HasJobRejected => JobRejectedSummary.Length > 0;
    public string JobBulletCountText => SelectedJob is null ? "" : SelectedJob.Job.Bullets.Count == 1 ? "1 bullet in this job" : $"{SelectedJob.Job.Bullets.Count} bullets in this job";
    public int JobUsedCount => JobOptions.Count(o => o.IsUsed);
    public bool CanApplyJob => JobOptions.Any(o => o.IsUsed);

    partial void OnJobRejectedSummaryChanged(string value) => OnPropertyChanged(nameof(HasJobRejected));

    partial void OnSelectedJobChanged(JobVM? value)
    {
        JobOptions.Clear();
        JobRejectedSummary = "";
        HasJobResult = false;
        JobStatus = "";
        NewOptions.Clear();
        NewRejectedSummary = "";
        HasNewResult = false;
        NewStatus = "";
        OnPropertyChanged(nameof(CanApplyJob));
    }

    // ---- tab 3: new bullets
    public ObservableCollection<NewOptionVM> NewOptions { get; } = [];

    [ObservableProperty] private string _answers = "";
    [ObservableProperty] private string _newStatus = "";
    [ObservableProperty] private string _newRejectedSummary = "";
    [ObservableProperty] private bool _hasNewResult;
    [ObservableProperty] private string _newNote = "";

    public bool CanGenerateNew => HasJob && !IsBusy;
    public bool HasNewRejected => NewRejectedSummary.Length > 0;
    public bool HasNewNote => NewNote.Length > 0;
    /// <summary>
    /// "Write a bullet for every skill I ticked, from the kind of work this job involves, even if my resume says nothing about it." Off by default: the
    /// person is saying they really used each ticked skill, and the bullets are drafts for them to check.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnswersHint))]
    private bool _draftFromRole;

    public string AnswersHint =>
        Answers.Trim().Length > 0 && !AnswerCheck.SkillsOnly(Answers, out _)
            ? "New bullets will be written from what you typed here and from this job's bullets."
            : DraftFromRole && (Answers.Trim().Length > 0 || NewMissingSkills.Concat(NewSkills).Any(s => s.IsPicked))
                ? "Drafting from the role: each ticked skill gets a bullet about the kind of work this job involves, with no numbers or results added. Check each is true before you add it."
                : Answers.Trim().Length > 0 || NewMissingSkills.Any(s => s.IsPicked)
                    ? "Skill only: each bullet will tie it to work this job's bullets already describe, and add nothing else. Check it is true before you add it."
                    : "Nothing typed: new bullets will only restate what this job's bullets already say, in a different way.";

    partial void OnNewRejectedSummaryChanged(string value) => OnPropertyChanged(nameof(HasNewRejected));
    partial void OnNewNoteChanged(string value) => OnPropertyChanged(nameof(HasNewNote));
    partial void OnAnswersChanged(string value) => OnPropertyChanged(nameof(AnswersHint));

    // ---------------------------------------------------------------- building the lists

    private void Rebuild(int? selectLine, int selectJobIndex)
    {
        var jobs = Session.Jobs;
        Jobs.Clear();
        foreach (var job in jobs)
            Jobs.Add(new JobVM(job));

        Bullets.Clear();
        foreach (var b in Session.Bullets)
        {
            var job = jobs.FirstOrDefault(j => j.Bullets.Any(x => x.LineNumber == b.LineNumber));
            Bullets.Add(new BulletRowVM(b, Session.IsChanged(b.LineNumber), Session.IsAdded(b.LineNumber), job?.Header ?? "Other bullets"));
        }

        // The "Show" drop-down: everything, each job, and the bullets that belong to none. The choice survives a rebuild.
        var chosen = SelectedFilter?.Header;
        BulletFilters.Clear();
        BulletFilters.Add(new BulletFilter("All bullets", null));
        foreach (var job in jobs.Select(j => j.Header).Distinct())
            BulletFilters.Add(new BulletFilter(job, job));
        if (Bullets.Any(b => !jobs.Any(j => j.Header == b.JobHeader)))
            BulletFilters.Add(new BulletFilter("Other bullets", "Other bullets"));
        SelectedFilter = BulletFilters.FirstOrDefault(f => f.Header == chosen) ?? BulletFilters[0];

        SelectedBullet = selectLine is { } line ? Bullets.FirstOrDefault(b => b.LineNumber == line) : null;
        SelectedJob = Jobs.Count == 0 ? null : Jobs[Math.Clamp(selectJobIndex, 0, Jobs.Count - 1)];
        ChangeCount = Session.Changes.Count;
        RefreshKeywords();
        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(ChangesText));
        OnPropertyChanged(nameof(CanSave));
    }

    private int SelectedJobIndex => SelectedJob is null ? 0 : Math.Max(0, Jobs.IndexOf(SelectedJob));

    // ---------------------------------------------------------------- asking the model

    private IChatModel? PrepareModel(out string problem) => AiAccess.Prepare(_match.Owner.Main, ConfirmSend, out problem);

    private (List<string> Picked, List<AttestedSkill> Claimed) Picks(IEnumerable<SkillPickVM> skills)
    {
        var picked = skills.Where(s => s.IsPicked).Select(s => s.Skill).ToList();
        var claimed = _match.State.Attested.Where(a => picked.Any(p => string.Equals(p, a.Skill, StringComparison.OrdinalIgnoreCase))).ToList();
        return (picked, claimed);
    }

    /// <summary>
    /// Asks the model without freezing the window, and shows the answer on the window's thread. The whole sequence runs inside the dispatcher, so
    /// every step after the wait is back on that thread whatever thread the caller was on, and the command finishes there too.
    /// </summary>
    private Task AskAsync<T>(Func<CancellationToken, Task<T>> call, Action<string> setStatus, Action<T> show, string asking) =>
        _dispatcher.InvokeAsync(async () =>
        {
            using var cancel = new CancellationTokenSource();
            _cancel = cancel;
            var started = DateTime.UtcNow;
            // A model can take most of a minute. This line, in the always-visible bottom bar, says it is working and for how long, so a wait is never
            // mistaken for a frozen window, and Stop gives the window back at once.
            void Tick() => BusyText = $"{asking} {(int)(DateTime.UtcNow - started).TotalSeconds} s. This can take up to a minute.";
            var clock = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (_, _) => Tick();
            IsBusy = true;
            Tick();
            clock.Start();
            setStatus(asking);

            T? result = default;
            string? error = null;
            var stopped = false;
            try
            {
                result = await Task.Run(() => call(cancel.Token), cancel.Token);
            }
            catch (OperationCanceledException)
            {
                stopped = true;
            }
            catch (ModelException ex)
            {
                error = ex.Message;
            }
            finally
            {
                clock.Stop();
                _cancel = null;
                IsBusy = false;
                BusyText = "";
            }

            if (stopped)
                setStatus("Stopped. Nothing was added.");
            else if (error is not null)
                setStatus(error);
            else
                show(result!);
        }).Task.Unwrap();

    private CancellationTokenSource? _cancel;

    /// <summary>What the bottom bar says while the model is being asked: what, and for how many seconds.</summary>
    [ObservableProperty] private string _busyText = "";

    /// <summary>Gives up waiting for the model. The window is usable again straight away.</summary>
    [RelayCommand]
    private void Stop() => _cancel?.Cancel();
    // ---------------------------------------------------------------- tab 1: one bullet

    [RelayCommand]
    private async Task GenerateAsync()
    {
        if (SelectedBullet is not { } row)
            return;
        var model = PrepareModel(out var problem);
        if (model is null)
        {
            Status = problem;
            return;
        }

        var (picked, claimed) = Picks(Skills);
        var bullet = Session.Bullets.FirstOrDefault(b => b.LineNumber == row.LineNumber) ?? new ResumeBullet(row.LineNumber, row.Text, row.Text);
        var request = new RewriteRequest(bullet, Session.Text, _jdText, Company, picked, claimed, ExtraFacts);

        Options.Clear();
        RejectedSummary = "";
        HasResult = false;
        await AskAsync(ct => new BulletRewriter(model).RewriteAsync(request, ct), s => Status = s, ShowResult, $"Asking {model.Provider}…");
    }

    public void ShowResult(RewriteResult result)
    {
        Options.Clear();
        foreach (var option in result.Options)
            Options.Add(new OptionVM(option, this));

        RejectedSummary = Dropped(result.Rejected);
        HasResult = true;
        Status = result.Options.Count > 0
            ? $"{result.Options.Count} option{(result.Options.Count == 1 ? "" : "s")} passed the checks."
            : "None of the suggestions passed the checks. Try again, or add a detail you know is true in the box above.";
    }

    public void Use(RewriteOption option)
    {
        if (SelectedBullet is not { } row)
            return;
        Session.Apply(row.LineNumber, option.Text);
        Rebuild(selectLine: row.LineNumber, SelectedJobIndex);
        Status = "Changed. Pick another bullet, or save as a new version.";
    }

    [RelayCommand]
    private void UndoBullet()
    {
        if (SelectedBullet is not { } row)
            return;
        Session.Undo(row.LineNumber);
        Rebuild(selectLine: Session.Bullets.Any(b => b.LineNumber == row.LineNumber) ? row.LineNumber : null, SelectedJobIndex);
    }

    // ---------------------------------------------------------------- tab 2: a whole job

    [RelayCommand]
    private async Task GenerateJobAsync()
    {
        if (SelectedJob is not { } job)
            return;
        var model = PrepareModel(out var problem);
        if (model is null)
        {
            JobStatus = problem;
            return;
        }

        var (picked, claimed) = Picks(JobSkills);
        var request = new JobRewriteRequest(job.Job, Session.Text, _jdText, Company, picked, claimed, JobExtraFacts);

        JobOptions.Clear();
        JobRejectedSummary = "";
        HasJobResult = false;
        await AskAsync(ct => new JobBulletWriter(model).RewriteJobAsync(request, ct), s => JobStatus = s, ShowJobResult, $"Asking {model.Provider}…");
    }

    public void ShowJobResult(JobRewriteResult result)
    {
        JobOptions.Clear();
        foreach (var option in result.Options)
        {
            var vm = new JobOptionVM(option);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(JobOptionVM.IsUsed))
                    OnPropertyChanged(nameof(CanApplyJob));
            };
            JobOptions.Add(vm);
        }

        JobRejectedSummary = Dropped(result.Rejected);
        HasJobResult = true;
        OnPropertyChanged(nameof(CanApplyJob));
        JobStatus = result.Options.Count > 0
            ? $"{result.Options.Count} bullet{(result.Options.Count == 1 ? "" : "s")} can be improved. Untick any you do not want."
            : "None of the suggestions passed the checks, so every bullet stays as it is.";
    }

    [RelayCommand]
    private void ApplyJob()
    {
        var chosen = JobOptions.Where(o => o.IsUsed).Select(o => o.Option).ToList();
        if (chosen.Count == 0)
            return;
        foreach (var option in chosen)
            Session.Apply(option.Bullet.LineNumber, option.Text);
        var index = SelectedJobIndex;
        Rebuild(selectLine: null, index);
        JobStatus = $"Changed {chosen.Count} bullet{(chosen.Count == 1 ? "" : "s")}. Save as a new version when you are happy.";
    }

    // ---------------------------------------------------------------- tab 3: new bullets

    [RelayCommand]
    private async Task GenerateNewAsync()
    {
        if (SelectedJob is not { } job)
            return;

        var (picked, claimed) = Picks(NewSkills);

        // A skill claimed without saying what was done with it: a keyword ticked above, or a skill typed on its own ("Kubernetes"). It is the person's
        // word that they used it; the bullets may tie it to work this job already describes, and the person is told to check each one.
        var claims = new List<AttestedSkill>(claimed);
        var typedSkills = AnswerCheck.SkillsOnly(Answers, out var typed) ? typed : [];
        foreach (var skill in NewMissingSkills.Where(s => s.IsPicked).Select(s => s.Skill).Concat(typedSkills))
        {
            if (!claims.Any(c => string.Equals(c.Skill, skill, StringComparison.OrdinalIgnoreCase)))
                claims.Add(new AttestedSkill { Skill = skill, Note = "" });
            if (!picked.Contains(skill, StringComparer.OrdinalIgnoreCase))
                picked.Add(skill);
        }

        // With "write bullets for the skills I ticked", every ticked skill is a claim, including the ones the resume already says.
        if (DraftFromRole)
            foreach (var skill in picked.Where(p => !claims.Any(c => string.Equals(c.Skill, p, StringComparison.OrdinalIgnoreCase))).ToList())
                claims.Add(new AttestedSkill { Skill = skill, Note = "" });

        var answers = typedSkills.Count > 0 ? "" : Answers; // the typed words were the skills, not an account of what was done

        // Something too short to write from, that is not a skill either ("banana"): say so before asking anyone anything.
        if (AnswerCheck.IsTooThin(Answers) && claims.Count == 0)
        {
            NewOptions.Clear();
            NewNote = "";
            NewStatus = AnswerCheck.ThinMessage(Answers);
            return;
        }

        var model = PrepareModel(out var problem);
        if (model is null)
        {
            NewStatus = problem;
            return;
        }

        var request = new NewBulletsRequest(job.Job, Session.Text, _jdText, Company, picked, claims, answers, DraftFromRole);
        _claimedWithoutDetails = claims.Where(c => c.Note.Trim().Length == 0).Select(c => c.Skill).ToList();
        _draftedFromRole = request.DraftsFromRole;

        NewOptions.Clear();
        NewRejectedSummary = "";
        NewNote = "";
        HasNewResult = false;
        await AskAsync(ct => new JobBulletWriter(model).NewBulletsAsync(request, ct), s => NewStatus = s, ShowNewResult, $"Asking {model.Provider}…");
    }

    // The skills the current request claims without details, so the result can say how its bullets were made.
    private List<string> _claimedWithoutDetails = [];
    private bool _draftedFromRole;

    public void ShowNewResult(NewBulletsResult result)
    {
        NewOptions.Clear();
        foreach (var option in result.Options)
            NewOptions.Add(new NewOptionVM(option, this));

        NewRejectedSummary = Dropped(result.Rejected);
        var skills = string.Join(", ", _claimedWithoutDetails);
        // A skill that got no bullet at all, so the person is not left wondering why only some of the ticked ones appear.
        var withoutBullet = _claimedWithoutDetails
            .Where(s => !result.Options.Any(o => o.Text.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        NewNote = result.Options.Count == 0 ? ""
            : _draftedFromRole
                ? $"You ticked ({skills}) but did not say what you did with them, so each bullet describes the kind of work this job involves, taken from its other bullets and the job description, with no numbers or results added. I cannot know that is what you did: read each one and add it only if it is true."
            : _claimedWithoutDetails.Count > 0
                ? $"You gave the skill ({skills}) but not what you did with it, so each bullet ties it to work this job's bullets already describe, and adds nothing else. I cannot know that is how you used it: read each one and add it only if it is true."
                : result.DraftedWithoutAnswers
                    ? "You gave no details, so these only restate what this job already says. Read each one and add it only if it is true and worth a line of its own."
                    : "";
        HasNewResult = true;
        NewStatus = result.Options.Count > 0 && _draftedFromRole && withoutBullet.Count > 0
            ? $"{result.Options.Count} new bullet{(result.Options.Count == 1 ? "" : "s")} passed the checks. None passed for {string.Join(", ", withoutBullet)}: see why below, or say what you did with it in the box and try again."
            : result.Options.Count > 0
            ? $"{result.Options.Count} new bullet{(result.Options.Count == 1 ? "" : "s")} passed the checks."
            : _draftedFromRole
                ? $"Nothing could be written for {skills} without adding something you did not say. Type what you did with it in a sentence, and try again."
            : _claimedWithoutDetails.Count > 0
                ? $"Nothing honest could be tied to this job's existing bullets. Say what you did with {skills} in a sentence, and try again."
                : result.DraftedWithoutAnswers
                    ? "Nothing new can be said honestly from this job's bullets alone. Type what you did in the box above, or tick a keyword you have used, and try again."
                    : "None of the suggestions passed the checks. Try again with more detail.";
    }

    public void AddBullet(NewBulletOption option)
    {
        if (SelectedJob is not { } job)
            return;
        var index = SelectedJobIndex;
        var line = Session.InsertAfter(job.Job.LastLine, option.Text);
        var remaining = NewOptions.Where(o => o.Text != option.Text).ToList();

        Rebuild(selectLine: line, index);
        foreach (var o in remaining)
            NewOptions.Add(o);
        HasNewResult = true;
        NewStatus = $"Added under {job.Job.Header}. Add more, or save as a new version.";
        NewNote = "";
    }

    // ---------------------------------------------------------------- saving

    private static string Dropped(IReadOnlyList<RejectedOption> rejected) =>
        rejected.Count == 0
            ? ""
            : $"{rejected.Count} suggestion{(rejected.Count == 1 ? " was" : "s were")} dropped because {(rejected.Count == 1 ? "it was" : "they were")} not safe to use:\n" +
              string.Join("\n", rejected.Select(r => $"• “{Shorten(r.Text)}” — {string.Join(" ", r.Problems)}"));

    private static string Shorten(string text) => text.Length > 90 ? text[..90] + "…" : text;

    [RelayCommand]
    private void SaveVersion()
    {
        var main = _match.Owner.Main;
        main.Try(() =>
        {
            _savedVersion = ResumeVersions.SaveNew(_match.Owner.App.FolderPath, Session.Text);
            _lastSavedText = Session.Text;
            SavedName = _savedVersion.FileName;
            _match.Refresh();
            main.ShowToast($"Saved as {SavedName}. Your resume is unchanged");
        });
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>
    /// Makes the Word document of the resume with your changes. Changes that are not saved yet are saved as a new version first, so the document
    /// and the version always say the same thing. With no changes it shows the resume as it is.
    /// </summary>
    public WordPreview? PrepareWordPreview()
    {
        var main = _match.Owner.Main;
        if (CanSave)
            SaveVersion();
        if (_savedVersion is not null && Session.Text == _lastSavedText && ChangeCount > 0)
            return _match.PrepareWordPreview(_savedVersion);
        return _match.PrepareWordPreview(null);
    }

    [RelayCommand]
    private void UseSavedAsResume()
    {
        if (_savedVersion is not null)
        {
            _match.UseAsResume(_savedVersion);
            SavedName = "";
        }
    }
}
