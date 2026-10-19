using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core;

namespace JobTracker.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public const string AnyStatus = "Any status";
    public const string AllSources = "All sources";

    public static IReadOnlyList<ApplicationStatus> Statuses { get; } = Enum.GetValues<ApplicationStatus>();
    public static IReadOnlyList<SearchScope> Scopes { get; } = Enum.GetValues<SearchScope>();
    public static IReadOnlyList<string> StatusFilterOptions { get; } = [AnyStatus, .. Enum.GetNames<ApplicationStatus>()];
    public static IReadOnlyList<JobTypeOption> JobTypeOptions { get; } =
        [new JobTypeOption("Full-time", JobType.FullTime), new JobTypeOption("Contract", JobType.Contract)];
    public static IReadOnlyList<string> SourceFilterOptions { get; } = [AllSources, nameof(BulletSource.Jd), nameof(BulletSource.Resume)];

    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private ApplicationStore _store;
    private BulletStore _bulletLibrary;
    private ISearchProvider _search;
    private List<ApplicationItemVM> _items = [];
    private CancellationTokenSource? _debounce;

    private readonly OneDriveLocator _oneDrive;
    private readonly IKnownFolders _knownFolders;

    public MainViewModel(AppSettings settings, OneDriveLocator? oneDrive = null, IKnownFolders? knownFolders = null)
    {
        _settings = settings;
        _oneDrive = oneDrive ?? new OneDriveLocator();
        _knownFolders = knownFolders ?? new WindowsKnownFolders();
        (_store, _bulletLibrary, _search) = CreateStores(settings.DataRoot);

        ApplicationsView = CollectionViewSource.GetDefaultView(Applications);
        ApplicationsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ApplicationItemVM.Company)));

        JobTypeTabs = [new JobTypeTab(null, "All"), new JobTypeTab(JobType.FullTime, "Full-time"), new JobTypeTab(JobType.Contract, "Contract")];
        _selectedJobTypeTab = JobTypeTabs[0];

        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast = "";
        };

        Reload();
    }

    public ApplicationStore Store => _store;
    public BulletStore BulletLibrary => _bulletLibrary;

    public ObservableCollection<ApplicationItemVM> Applications { get; } = [];
    public ICollectionView ApplicationsView { get; }
    public ObservableCollection<BulletVM> StarredBullets { get; } = [];

    /// <summary>The tabs above the applications. Each application is either full-time or contract.</summary>
    public IReadOnlyList<JobTypeTab> JobTypeTabs { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllJobTypes))]
    private JobTypeTab _selectedJobTypeTab = null!;

    public bool IsAllJobTypes => SelectedJobTypeTab.Type is null;

    // What the right-hand pane says when nothing is selected: true to why the list is empty.
    [ObservableProperty] private string _emptyTitle = "No applications yet";
    [ObservableProperty] private string _emptyHint = "Paste a job page and your resume, and they are saved to a folder for you.";
    [ObservableProperty] private string _emptyButton = "Add your first application";

    /// <summary>A new application starts as the kind the person is looking at (Full-time when on the All tab).</summary>
    public JobType DefaultNewJobType => SelectedJobTypeTab.Type ?? JobType.FullTime;

    private JobTypeTab TabFor(JobType type) => JobTypeTabs.First(t => t.Type == type);

    [ObservableProperty] private string _dataRoot = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private SearchScope _scope = SearchScope.Both;
    [ObservableProperty] private bool _onlyMatchingLines = true;
    [ObservableProperty] private bool _starredOnly;
    [ObservableProperty] private string _statusFilter = AnyStatus;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _toast = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApplicationsTab))]
    private int _selectedTabIndex;

    /// <summary>The search/filter bar only applies to the Applications tab.</summary>
    public bool IsApplicationsTab => SelectedTabIndex == 0;

    [ObservableProperty] private string _bulletSearch = "";
    [ObservableProperty] private string _bulletSourceFilter = AllSources;
    [ObservableProperty] private string _bulletSummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(NoSelection))]
    private ApplicationItemVM? _selected;

    public bool HasSelection => Selected is not null;
    public bool NoSelection => Selected is null;

    /// <summary>The current search words (empty when no search is active).</summary>
    public string[] Terms => SearchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when a search is active and it covers the given document type.</summary>
    public bool SearchCovers(BulletSource source) =>
        Terms.Length > 0 && (Scope == SearchScope.Both ||
                             (Scope == SearchScope.Jd && source == BulletSource.Jd) ||
                             (Scope == SearchScope.Resume && source == BulletSource.Resume));

    partial void OnSelectedChanged(ApplicationItemVM? value) => value?.LoadDetail();

    partial void OnSelectedJobTypeTabChanged(JobTypeTab value)
    {
        if (value is not null)
            ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ScheduleFilter();
    partial void OnScopeChanged(SearchScope value) => ApplyFilters();
    partial void OnStarredOnlyChanged(bool value) => ApplyFilters();
    partial void OnStatusFilterChanged(string value) => ApplyFilters();
    partial void OnOnlyMatchingLinesChanged(bool value) => Selected?.LoadDetail();

    partial void OnBulletSearchChanged(string value) => RefreshBullets();
    partial void OnBulletSourceFilterChanged(string value) => RefreshBullets();

    private static (ApplicationStore, BulletStore, ISearchProvider) CreateStores(string root)
    {
        var store = new ApplicationStore(root);
        return (store, new BulletStore(root), new KeywordSearchProvider(store));
    }

    public void Reload()
    {
        DataRoot = _settings.DataRoot;
        Try(() =>
        {
            _items = _store.List().Select(a => new ApplicationItemVM(a, this)).ToList();
            ApplyFilters();
            RefreshBullets();
        });
    }

    public void ChangeDataRoot(string path)
    {
        _settings.DataRoot = path;
        _settings.Save();
        (_store, _bulletLibrary, _search) = CreateStores(path);
        Selected = null;
        Reload();
        ShowToast($"Now using {path}");
    }

    public void AddApplication(NewApplication input)
    {
        Try(() =>
        {
            var created = _store.Create(input);
            Reload();
            SelectByFolder(created.FolderPath);
            SelectedTabIndex = 0;
            ShowToast($"Saved to {created.FolderPath}");
        });
    }

    public void SelectByFolder(string folderPath)
    {
        var item = _items.FirstOrDefault(i => string.Equals(i.App.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        if (!Applications.Contains(item) && SelectedJobTypeTab.Type is not null && SelectedJobTypeTab.Type != item.JobType)
            SelectedJobTypeTab = TabFor(item.JobType); // it belongs on the other tab: go there rather than leaving the tab

        if (!Applications.Contains(item))
        {
            // Hidden by the current filters, so clear them to make it visible.
            SearchText = "";
            StarredOnly = false;
            StatusFilter = AnyStatus;
            ApplyFilters();
        }

        Selected = item;
    }

    /// <summary>
    /// An application was switched between Full-time and Contract. Follow it to its new tab so it stays in front of the person,
    /// and keep the counts right.
    /// </summary>
    public void JobTypeChanged(ApplicationItemVM item)
    {
        ShowToast($"Moved to {item.JobType.Label()}");
        if (SelectedJobTypeTab.Type is not null && SelectedJobTypeTab.Type != item.JobType)
        {
            SelectedJobTypeTab = TabFor(item.JobType);
            if (Applications.Contains(item))
                Selected = item;
        }
        else
        {
            UpdateJobTypeCounts();
        }
    }

    private void UpdateJobTypeCounts()
    {
        foreach (var tab in JobTypeTabs)
            tab.Count = tab.Type is null ? _items.Count : _items.Count(i => i.JobType == tab.Type);
    }

    private async void ScheduleFilter()
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, cts.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        // Hop back to the UI thread explicitly rather than relying on an ambient synchronization context.
        _ = _dispatcher.BeginInvoke(() =>
        {
            if (!cts.IsCancellationRequested)
                Try(ApplyFilters);
        });
    }

    private void ApplyFilters()
    {
        _debounce?.Cancel();
        IEnumerable<ApplicationItemVM> query = _items;
        var hitCount = 0;

        if (Terms.Length > 0)
        {
            var hits = _search.Search(_items.Select(i => i.App), SearchText, Scope);
            hitCount = hits.Count;
            var matched = hits.Select(h => h.Application.FolderPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            query = query.Where(i => matched.Contains(i.App.FolderPath));
        }

        if (StarredOnly)
            query = query.Where(i => i.IsStarred);
        if (StatusFilter != AnyStatus)
            query = query.Where(i => i.Status.ToString() == StatusFilter);
        if (SelectedJobTypeTab.Type is { } type)
            query = query.Where(i => i.JobType == type);

        UpdateJobTypeCounts();
        var list = query.ToList();
        var previous = Selected;

        Applications.Clear();
        foreach (var item in list)
            Applications.Add(item);

        Selected = previous is not null && list.Contains(previous) ? previous : list.FirstOrDefault();
        // Same selection can come back unchanged, but the highlighted lines depend on the new search.
        Selected?.LoadDetail();

        if (_items.Count == 0)
        {
            EmptyTitle = "No applications yet";
            EmptyHint = "Paste a job page and your resume, and they are saved to a folder for you.";
            EmptyButton = "Add your first application";
        }
        else if (Terms.Length > 0 || StarredOnly || StatusFilter != AnyStatus)
        {
            EmptyTitle = "No applications match";
            EmptyHint = "Try different words, or click Clear to see everything on this tab.";
            EmptyButton = "New application";
        }
        else
        {
            var kind = SelectedJobTypeTab.Name.ToLowerInvariant();
            EmptyTitle = $"No {kind} applications yet";
            EmptyHint = $"Applications you mark as {kind} appear here. New ones start as {kind} while you are on this tab.";
            EmptyButton = "New application";
        }

        Summary = Terms.Length > 0
            ? $"{list.Count} of {SelectedJobTypeTab.Count} applications · {hitCount} matching lines"
            : $"{list.Count} of {SelectedJobTypeTab.Count} applications";
    }

    public void RefreshBullets()
    {
        var source = BulletSourceFilter == AllSources ? (BulletSource?)null : Enum.Parse<BulletSource>(BulletSourceFilter);
        var total = _bulletLibrary.List().Count;
        var matches = _bulletLibrary.Search(BulletSearch, source);

        StarredBullets.Clear();
        foreach (var bullet in matches)
            StarredBullets.Add(new BulletVM(bullet, this));

        BulletSummary = $"{matches.Count} of {total} starred bullets";
        Selected?.RefreshStars();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = "";
        StarredOnly = false;
        StatusFilter = AnyStatus;
        Scope = SearchScope.Both;
    }

    public void Copy(string text)
    {
        try
        {
            // SetDataObject retries internally when another app is holding the clipboard.
            Clipboard.SetDataObject(text, copy: true);
            ShowToast(text.Length > 60 ? $"Copied: {text[..60]}…" : $"Copied: {text}");
        }
        catch (ExternalException)
        {
            ShowToast("Clipboard is busy, try again");
        }
    }

    public void ShowToast(string message)
    {
        Toast = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    /// <summary>Runs a disk operation, reporting IO problems in the status bar instead of crashing.</summary>
    public void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast($"Disk error: {ex.Message}");
        }
    }
}
