namespace JobTracker.Core;

/// <summary>
/// Reads and writes application folders: &lt;root&gt;\&lt;Company&gt;\&lt;yyyy-MM-dd&gt;_&lt;Role&gt;\{jd.md, resume.md, notes.md, meta.json}.
/// The files on disk are the source of truth; nothing is cached.
/// </summary>
public sealed class ApplicationStore(string root)
{
    public const string JdFile = "jd.md";
    public const string ResumeFile = "resume.md";
    public const string NotesFile = "notes.md";
    public const string OriginalJdFile = "jd.original.txt";
    public const string MetaFile = "meta.json";
    public const string AppDataFolder = ".jobtracker";

    public string Root { get; } = root;

    public JobApplication Create(NewApplication input)
    {
        var date = input.AppliedDate ?? DateOnly.FromDateTime(DateTime.Today);
        var companyDir = Path.Combine(Root, FolderNamer.Sanitize(input.Company));
        var folder = FolderNamer.MakeUnique(Path.Combine(companyDir, FolderNamer.ApplicationFolderName(date, input.Role)));

        var meta = new ApplicationMeta
        {
            Company = input.Company.Trim(),
            Role = input.Role.Trim(),
            Url = string.IsNullOrWhiteSpace(input.Url) ? null : input.Url.Trim(),
            AppliedDate = date,
        };

        Directory.CreateDirectory(folder);
        AtomicFile.WriteAllText(Path.Combine(folder, JdFile), input.JdText);
        if (input.OriginalJd is not null && input.OriginalJd != input.JdText)
            AtomicFile.WriteAllText(Path.Combine(folder, OriginalJdFile), input.OriginalJd);
        AtomicFile.WriteAllText(Path.Combine(folder, ResumeFile), input.ResumeText);
        AtomicFile.WriteAllText(Path.Combine(folder, NotesFile), "");
        AtomicFile.WriteJson(Path.Combine(folder, MetaFile), meta);

        return new JobApplication { FolderPath = folder, Meta = meta };
    }

    /// <summary>All applications, newest first. Folders with a missing or unreadable meta.json are skipped.</summary>
    public IReadOnlyList<JobApplication> List()
    {
        var result = new List<JobApplication>();
        if (!Directory.Exists(Root))
            return result;

        foreach (var companyDir in Directory.EnumerateDirectories(Root))
        {
            if (Path.GetFileName(companyDir).StartsWith('.'))
                continue;

            foreach (var appDir in Directory.EnumerateDirectories(companyDir))
            {
                var meta = AtomicFile.ReadJson<ApplicationMeta>(Path.Combine(appDir, MetaFile));
                if (meta is not null)
                    result.Add(new JobApplication { FolderPath = appDir, Meta = meta });
            }
        }

        return result
            .OrderByDescending(a => a.Meta.AppliedDate)
            .ThenBy(a => a.Meta.Company, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string ReadJd(JobApplication app) => ReadOrEmpty(app.JdPath);
    public string ReadResume(JobApplication app) => ReadOrEmpty(app.ResumePath);
    public string ReadNotes(JobApplication app) => ReadOrEmpty(app.NotesPath);

    public void SaveJd(JobApplication app, string text) => AtomicFile.WriteAllText(app.JdPath, text);
    public void SaveResume(JobApplication app, string text) => AtomicFile.WriteAllText(app.ResumePath, text);
    public void SaveNotes(JobApplication app, string text) => AtomicFile.WriteAllText(app.NotesPath, text);

    public void SaveMeta(JobApplication app) =>
        AtomicFile.WriteJson(Path.Combine(app.FolderPath, MetaFile), app.Meta);

    /// <summary>Folder relative to the data root, used as a stable key in bullets.json.</summary>
    public string RelativeFolder(JobApplication app) => Path.GetRelativePath(Root, app.FolderPath);

    private static string ReadOrEmpty(string path) => File.Exists(path) ? File.ReadAllText(path) : "";
}
