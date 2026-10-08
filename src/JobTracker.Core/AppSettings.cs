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

    /// <summary>Dev/testing override: keeps the settings file somewhere else so a test run never changes the real one.</summary>
    public const string SettingsFileEnvVar = "JOBTRACKER_SETTINGS_FILE";

    public static string DefaultPath() =>
        Environment.GetEnvironmentVariable(SettingsFileEnvVar) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JobTracker", "settings.json");

    /// <summary>
    /// Written next to the program by the setup wizard (or by /DATAROOT=... on a silent install) with the folder the person chose.
    /// It is only the starting point for someone who has no settings file yet; once they have one, that wins.
    /// </summary>
    public static string InstallDefaultsPath() => Path.Combine(AppContext.BaseDirectory, "install-defaults.json");

    public static AppSettings Load(string? path = null, string? installDefaultsPath = null)
    {
        var file = path ?? DefaultPath();
        var settings = AtomicFile.ReadJson<AppSettings>(file) ?? new AppSettings();
        if (string.IsNullOrWhiteSpace(settings.DataRoot))
            settings.DataRoot = DefaultDataRoot();

        if (!File.Exists(file) && AtomicFile.ReadJson<AppSettings>(installDefaultsPath ?? InstallDefaultsPath()) is { } chosen &&
            !string.IsNullOrWhiteSpace(chosen.DataRoot))
            settings.DataRoot = Environment.ExpandEnvironmentVariables(chosen.DataRoot); // so an IT-managed install can say %USERPROFILE%\Documents\Jobs

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
