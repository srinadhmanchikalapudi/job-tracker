using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core.Ai;
using JobTracker.Core.Builder;
using JobTracker.Core.Tailoring;

namespace JobTracker.App.ViewModels;

/// <summary>One bullet of a job being built, with the one most useful tip about it.</summary>
public sealed partial class BuilderBulletVM(BuilderJobVM job, string text) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tip), nameof(HasTip))]
    private string _text = text;

    public string Tip => ResumeAdvice.BulletTip(Text) ?? "";
    public bool HasTip => Tip.Length > 0;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Text))
            job.Owner.Touch();
    }

    [RelayCommand]
    private void Remove() => job.RemoveBullet(this);

    [RelayCommand]
    private void MoveUp() => job.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => job.Move(this, 1);
}

public sealed partial class BuilderJobVM(ResumeBuilderVM owner, ProfileJob job) : ObservableObject
{
    public ResumeBuilderVM Owner { get; } = owner;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Heading))] private string _company = job.Company;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Heading))] private string _title = job.Title;
    [ObservableProperty] private string _location = job.Location;
    [ObservableProperty] private string _start = job.Start;
    [ObservableProperty] private string _end = job.End;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EndVisible))] private bool _isCurrent = job.IsCurrent;

    public ObservableCollection<BuilderBulletVM> Bullets { get; } = [];

    public bool EndVisible => !IsCurrent;

    /// <summary>For the list on the left: "Senior Engineer, Contoso", or "New job" until something is typed.</summary>
    public string Heading
    {
        get
        {
            var parts = new[] { Title.Trim(), Company.Trim() }.Where(p => p.Length > 0);
            var text = string.Join(", ", parts);
            return text.Length > 0 ? text : "New job";
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Heading) or nameof(EndVisible)))
            Owner.Touch();
    }

    public BuilderBulletVM AddBullet(string text = "")
    {
        var bullet = new BuilderBulletVM(this, text);
        Bullets.Add(bullet);
        Owner.Touch();
        return bullet;
    }

    [RelayCommand]
    private void AddEmptyBullet() => AddBullet();

    public void RemoveBullet(BuilderBulletVM bullet)
    {
        Bullets.Remove(bullet);
        Owner.Touch();
    }

    public void Move(BuilderBulletVM bullet, int direction)
    {
        var index = Bullets.IndexOf(bullet);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= Bullets.Count)
            return;
        Bullets.Move(index, target);
        Owner.Touch();
    }

    public ProfileJob ToProfile() => new()
    {
        Company = Company, Title = Title, Location = Location, Start = Start, End = End, IsCurrent = IsCurrent,
        Bullets = Bullets.Select(b => b.Text).ToList(),
    };
}

public sealed partial class BuilderSchoolVM(ResumeBuilderVM owner, ProfileSchool school) : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Heading))] private string _school = school.School;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Heading))] private string _degree = school.Degree;
    [ObservableProperty] private string _dates = school.Dates;
    [ObservableProperty] private string _details = school.Details;

    public string Heading
    {
        get
        {
            var text = string.Join(", ", new[] { Degree.Trim(), School.Trim() }.Where(p => p.Length > 0));
            return text.Length > 0 ? text : "New school";
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(Heading))
            owner.Touch();
    }

    public ProfileSchool ToProfile() => new() { School = School, Degree = Degree, Dates = Dates, Details = Details };
}

/// <summary>A new bullet the AI helper wrote from the person's answers, after it passed the checks.</summary>
public sealed partial class BuilderOptionVM(NewBulletOption option, ResumeBuilderVM owner) : ObservableObject
{
    public string Text => option.Text;
    public string Note => option.Note;
    public bool HasNote => option.Note.Length > 0;

    [RelayCommand]
    private void Add() => owner.AddHelperBullet(option);
}

/// <summary>
/// The "My resume" window: a resume written from scratch out of the person's own answers. It saves as they type, shows the finished text and a list of
/// things worth fixing as they go, and makes the Word document on request. Bullets can be typed, put together from parts, or written by the AI helper
/// from the person's answers: the helper may use only those answers, and every bullet it offers has passed the same checks as everywhere else.
/// </summary>
public sealed partial class ResumeBuilderVM : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly ProfileStore _store;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _loading;
    private bool _dirty;

    /// <summary>Asked once per AI service before anything is sent. The window supplies the question.</summary>
    public Func<string, bool> ConfirmSend { get; set; } = _ => true;

    public ResumeBuilderVM(MainViewModel main)
    {
        _main = main;
        _store = new ProfileStore(main.DataRoot);
        _saveTimer.Tick += (_, _) => Flush();
        Load(_store.Load());
    }

    // ---- the resume
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _location = "";
    [ObservableProperty] private string _links = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _skills = "";
    [ObservableProperty] private string _certifications = "";

    public ObservableCollection<BuilderJobVM> Jobs { get; } = [];
    public ObservableCollection<BuilderSchoolVM> Schools { get; } = [];
    public ObservableCollection<string> Problems { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasJob), nameof(CanAddBullet))] private BuilderJobVM? _selectedJob;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSchool))] private BuilderSchoolVM? _selectedSchool;
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _saveStatus = "";

    public bool HasJob => SelectedJob is not null;
    public bool HasSchool => SelectedSchool is not null;
    public bool HasProblems => Problems.Count > 0;
    public string ProblemsHeading => Problems.Count == 0 ? "Nothing to fix. Preview it as a Word document when you are ready." : $"{Problems.Count} thing{(Problems.Count == 1 ? "" : "s")} worth fixing";
    public string DataFolder => ResumeBuilderFiles.Folder(_main.DataRoot);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Name) or nameof(Headline) or nameof(Email) or nameof(Phone) or nameof(Location) or nameof(Links) or nameof(Summary)
            or nameof(Skills) or nameof(Certifications))
            Touch();
    }

    // ---------------------------------------------------------------- loading, saving, refreshing

    private void Load(ResumeProfile profile)
    {
        _loading = true;
        Name = profile.Name;
        Headline = profile.Headline;
        Email = profile.Email;
        Phone = profile.Phone;
        Location = profile.Location;
        Links = profile.Links;
        Summary = profile.Summary;
        Skills = profile.Skills;
        Certifications = profile.Certifications;

        Jobs.Clear();
        foreach (var job in profile.Jobs)
        {
            var vm = new BuilderJobVM(this, job);
            foreach (var bullet in job.Bullets)
                vm.Bullets.Add(new BuilderBulletVM(vm, bullet));
            Jobs.Add(vm);
        }

        Schools.Clear();
        foreach (var school in profile.Education)
            Schools.Add(new BuilderSchoolVM(this, school));

        SelectedJob = Jobs.FirstOrDefault();
        SelectedSchool = Schools.FirstOrDefault();
        _loading = false;
        Refresh();
        SaveStatus = _store.Exists ? "Saved" : "";
    }

    public ResumeProfile ToProfile() => new()
    {
        Name = Name, Headline = Headline, Email = Email, Phone = Phone, Location = Location, Links = Links, Summary = Summary, Skills = Skills,
        Certifications = Certifications, Jobs = Jobs.Select(j => j.ToProfile()).ToList(), Education = Schools.Select(s => s.ToProfile()).ToList(),
    };

    /// <summary>Something changed: show the new text and advice now, and save a moment after the typing stops.</summary>
    public void Touch()
    {
        if (_loading)
            return;
        _dirty = true;
        SaveStatus = "Saving…";
        Refresh();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void Refresh()
    {
        var profile = ToProfile();
        PreviewText = ResumeProfileText.Build(profile);
        Problems.Clear();
        foreach (var problem in ResumeAdvice.Problems(profile))
            Problems.Add(problem);
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(ProblemsHeading));
    }

    /// <summary>Writes the resume now (when the window closes, or before a Word document is made).</summary>
    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty)
            return;
        var profile = ToProfile();
        _main.Try(() =>
        {
            // A resume with nothing in it is not saved, so opening the window and closing it leaves no file behind.
            if (!profile.IsEmpty || _store.Exists)
                _store.Save(profile);
        });
        _dirty = false;
        SaveStatus = profile.IsEmpty && !_store.Exists ? "" : "Saved";
    }

    // ---------------------------------------------------------------- jobs and schools

    [RelayCommand]
    private void AddJob()
    {
        var job = new BuilderJobVM(this, new ProfileJob());
        Jobs.Insert(0, job); // the newest job goes first, the way a resume reads
        SelectedJob = job;
        Touch();
    }

    public void RemoveSelectedJob()
    {
        if (SelectedJob is not { } job)
            return;
        var index = Jobs.IndexOf(job);
        Jobs.Remove(job);
        SelectedJob = Jobs.Count == 0 ? null : Jobs[Math.Min(index, Jobs.Count - 1)];
        Touch();
    }

    public void MoveSelectedJob(int direction)
    {
        if (SelectedJob is not { } job)
            return;
        var index = Jobs.IndexOf(job);
        var target = index + direction;
        if (target < 0 || target >= Jobs.Count)
            return;
        Jobs.Move(index, target);
        Touch();
    }

    [RelayCommand]
    private void AddSchool()
    {
        var school = new BuilderSchoolVM(this, new ProfileSchool());
        Schools.Add(school);
        SelectedSchool = school;
        Touch();
    }

    public void RemoveSelectedSchool()
    {
        if (SelectedSchool is not { } school)
            return;
        var index = Schools.IndexOf(school);
        Schools.Remove(school);
        SelectedSchool = Schools.Count == 0 ? null : Schools[Math.Min(index, Schools.Count - 1)];
        Touch();
    }

    // ---------------------------------------------------------------- a bullet put together from parts (no AI)

    public static IReadOnlyList<string> Verbs => BulletComposer.Verbs;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(QuickPreview), nameof(CanAddQuick))] private string _quickVerb = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(QuickPreview), nameof(CanAddQuick))] private string _quickWhat = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(QuickPreview))] private string _quickTools = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(QuickPreview))] private string _quickResult = "";

    public string QuickPreview
    {
        get
        {
            var text = BulletComposer.Compose(QuickVerb, QuickWhat, QuickTools, QuickResult);
            return text.Length > 0 ? text : "Your bullet appears here as you fill in the parts.";
        }
    }

    public bool CanAddQuick => HasJob && BulletComposer.Compose(QuickVerb, QuickWhat, "", "").Length > 0;
    public bool CanAddBullet => HasJob;

    [RelayCommand]
    private void AddQuickBullet()
    {
        if (SelectedJob is not { } job)
            return;
        var text = BulletComposer.Compose(QuickVerb, QuickWhat, QuickTools, QuickResult);
        if (text.Length == 0)
            return;
        job.AddBullet(text);
        QuickVerb = QuickWhat = QuickTools = QuickResult = "";
    }

    // ---------------------------------------------------------------- bullets written by the AI helper from the person's answers

    public ObservableCollection<BuilderOptionVM> HelperOptions { get; } = [];

    [ObservableProperty] private string _answers = "";
    [ObservableProperty] private string _helperStatus = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanAskHelper))] private bool _isBusy;
    [ObservableProperty] private string _helperRejected = "";

    public bool CanAskHelper => HasJob && !IsBusy;
    public bool HasHelperRejected => HelperRejected.Length > 0;

    partial void OnHelperRejectedChanged(string value) => OnPropertyChanged(nameof(HasHelperRejected));

    partial void OnSelectedJobChanged(BuilderJobVM? value)
    {
        HelperOptions.Clear();
        HelperStatus = "";
        HelperRejected = "";
        OnPropertyChanged(nameof(CanAskHelper));
        OnPropertyChanged(nameof(CanAddQuick));
    }

    [RelayCommand]
    private Task AskHelperAsync()
    {
        if (SelectedJob is not { } selected)
            return Task.CompletedTask;
        if (Answers.Trim().Length == 0)
        {
            HelperStatus = "Type what you did first. Nothing can be written from nothing, and the helper never makes things up.";
            return Task.CompletedTask;
        }

        var model = AiAccess.Prepare(_main, ConfirmSend, out var problem);
        if (model is null)
        {
            HelperStatus = problem;
            return Task.CompletedTask;
        }

        var existing = selected.Bullets.Select((b, i) => new ResumeBullet(i + 1, "- " + b.Text, b.Text)).Where(b => b.Text.Trim().Length > 0).ToList();
        var job = new ResumeJob(selected.Heading, "Experience", existing, 0);
        var request = new NewBulletsRequest(job, PreviewText, "", "", [], [], Answers);

        return _dispatcher.InvokeAsync(async () =>
        {
            IsBusy = true;
            HelperOptions.Clear();
            HelperRejected = "";
            HelperStatus = $"Asking {model.Provider}…";

            NewBulletsResult? result = null;
            string? error = null;
            try
            {
                result = await Task.Run(() => new JobBulletWriter(model).NewBulletsAsync(request));
            }
            catch (ModelException ex)
            {
                error = ex.Message;
            }

            IsBusy = false;
            if (error is not null)
            {
                HelperStatus = error;
                return;
            }

            foreach (var option in result!.Options)
                HelperOptions.Add(new BuilderOptionVM(option, this));
            HelperRejected = result.Rejected.Count == 0
                ? ""
                : $"{result.Rejected.Count} suggestion{(result.Rejected.Count == 1 ? " was" : "s were")} dropped because {(result.Rejected.Count == 1 ? "it was" : "they were")} not safe to use:\n" +
                  string.Join("\n", result.Rejected.Select(r => $"• “{(r.Text.Length > 90 ? r.Text[..90] + "…" : r.Text)}” — {string.Join(" ", r.Problems)}"));
            HelperStatus = result.Options.Count > 0
                ? $"{result.Options.Count} bullet{(result.Options.Count == 1 ? "" : "s")} written from your answers. Add the ones you like."
                : "Nothing passed the checks. Add more detail to your answers (what you did, with what, and what changed) and try again.";
        }).Task.Unwrap();
    }

    public void AddHelperBullet(NewBulletOption option)
    {
        if (SelectedJob is not { } job)
            return;
        job.AddBullet(option.Text);
        var shown = HelperOptions.FirstOrDefault(o => o.Text == option.Text);
        if (shown is not null)
            HelperOptions.Remove(shown);
        HelperStatus = "Added. Edit it in the list if you want to change a word.";
    }

    // ---------------------------------------------------------------- the finished resume

    /// <summary>Makes the Word document of the resume as it is, ready to look at, open the folder of, or download.</summary>
    public WordPreview? PrepareWordPreview()
    {
        Flush();
        var text = ResumeProfileText.Build(ToProfile());
        if (text.Trim().Length == 0)
        {
            _main.ShowToast("There is nothing in the resume yet");
            return null;
        }

        return WordPreviews.Make(_main, ResumeBuilderFiles.Folder(_main.DataRoot), "", text, ResumeBuilderFiles.WordFile, "Your resume");
    }

    /// <summary>Deletes the resume and starts again with a blank one.</summary>
    public void StartOver()
    {
        _saveTimer.Stop();
        _main.Try(() => _store.Delete());
        _dirty = false;
        Load(new ResumeProfile());
        HelperOptions.Clear();
        Answers = "";
        SaveStatus = "";
    }
}