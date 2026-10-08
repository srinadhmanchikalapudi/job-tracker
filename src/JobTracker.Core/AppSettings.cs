namespace JobTracker.Core;

/// <summary>
/// Per-user settings in %APPDATA%\JobTracker\settings.json. Kept outside the data root because
/// it records where the data root is.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Dev/testing override: points the app at another data folder without touching the saved setting.</summary>
    public const string DataRootEnvVar = "JOBTRACKER_DATA_ROOT";

    private string _dataRoot = DefaultDataRoot();

    // Set while the environment override is in effect, so saving writes the user's real folder, not the override.
    private string? _persistedDataRoot;

    public string DataRoot
    {
        get => _dataRoot;
        set
        {
            _dataRoot = value;
            _persistedDataRoot = null; // an explicit change is meant to be saved
        }
    }

    /// <summary>Looks for a newer version on GitHub when the app starts (at most once a day).</summary>
    public bool CheckForUpdates { get; set; } = true;

    public DateTime? LastUpdateCheck { get; set; }

    public static string DefaultDataRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "JobApplications");

    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JobTracker", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        var settings = AtomicFile.ReadJson<AppSettings>(path ?? DefaultPath()) ?? new AppSettings();
        if (string.IsNullOrWhiteSpace(settings.DataRoot))
            settings.DataRoot = DefaultDataRoot();

        var overrideRoot = Environment.GetEnvironmentVariable(DataRootEnvVar);
        return path is null && !string.IsNullOrWhiteSpace(overrideRoot) ? settings.WithDataRootOverride(overrideRoot) : settings;
    }

    /// <summary>Uses <paramref name="root"/> for this run only: <see cref="Save"/> keeps writing the folder that was saved before.</summary>
    public AppSettings WithDataRootOverride(string root)
    {
        var real = _persistedDataRoot ?? DataRoot;
        DataRoot = root;
        _persistedDataRoot = real;
        return this;
    }

    public void Save(string? path = null)
    {
        var toSave = new AppSettings
        {
            DataRoot = _persistedDataRoot ?? DataRoot,
            CheckForUpdates = CheckForUpdates,
            LastUpdateCheck = LastUpdateCheck,
        };
        AtomicFile.WriteJson(path ?? DefaultPath(), toSave);
    }
}
