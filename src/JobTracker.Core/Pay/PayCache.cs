namespace JobTracker.Core.Pay;

/// <summary>
/// Remembers the pay found in each job description, so the list can show it without opening every file each time the app starts (which matters when
/// OneDrive keeps the files online only). An entry is used only while the description is exactly as it was when the pay was read: its size and
/// last-changed time are checked, and checking them does not read the file. The cache is only a shortcut; deleting it loses nothing.
/// </summary>
public sealed class PayCache
{
    public const string FileName = "pay-cache.json";

    public sealed class Entry
    {
        public long JdLength { get; set; }
        public long JdTicks { get; set; }
        public long OriginalLength { get; set; }
        public long OriginalTicks { get; set; }
        public PayInfo? Pay { get; set; }
    }

    private readonly string _path;
    private Dictionary<string, Entry> _entries;

    public PayCache(string dataRoot)
    {
        _path = Path.Combine(dataRoot, ApplicationStore.AppDataFolder, FileName);
        _entries = AtomicFile.ReadJson<Dictionary<string, Entry>>(_path) ?? new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        _entries = new Dictionary<string, Entry>(_entries, StringComparer.OrdinalIgnoreCase);
    }

    private static string Key(string dataRoot, JobApplication app) => Path.GetRelativePath(dataRoot, app.FolderPath);

    private static (long Length, long Ticks) Stat(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (info.Length, info.LastWriteTimeUtc.Ticks) : (-1, 0);
    }

    /// <summary>True with the remembered pay (which may be "none") when the description has not changed since it was read.</summary>
    public bool TryGet(string dataRoot, JobApplication app, out PayInfo? pay)
    {
        pay = null;
        if (!_entries.TryGetValue(Key(dataRoot, app), out var entry))
            return false;

        var jd = Stat(app.JdPath);
        var original = Stat(Path.Combine(app.FolderPath, ApplicationStore.OriginalJdFile));
        if (entry.JdLength != jd.Length || entry.JdTicks != jd.Ticks || entry.OriginalLength != original.Length || entry.OriginalTicks != original.Ticks)
            return false;

        pay = entry.Pay;
        return true;
    }

    public void Put(string dataRoot, JobApplication app, PayInfo? pay)
    {
        var jd = Stat(app.JdPath);
        var original = Stat(Path.Combine(app.FolderPath, ApplicationStore.OriginalJdFile));
        _entries[Key(dataRoot, app)] = new Entry
        {
            JdLength = jd.Length, JdTicks = jd.Ticks, OriginalLength = original.Length, OriginalTicks = original.Ticks, Pay = pay,
        };
    }

    /// <summary>Writes the cache. Failing to is harmless (it is rebuilt next time), so a locked or read-only folder is ignored.</summary>
    public void Save()
    {
        try
        {
            AtomicFile.WriteJson(_path, _entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the next start simply reads the descriptions again
        }
    }
}
