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

    /// <summary>The resume file the person attached, kept untouched next to resume.md: resume.original.pdf, .docx, .md or .txt.</summary>
    public const string OriginalResumePrefix = "resume.original";
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
            JobType = input.JobType,
        };

        try
        {
            Directory.CreateDirectory(folder);
            AtomicFile.WriteAllText(Path.Combine(folder, JdFile), input.JdText);
            if (input.OriginalJd is not null && input.OriginalJd != input.JdText)
                AtomicFile.WriteAllText(Path.Combine(folder, OriginalJdFile), input.OriginalJd);
            AtomicFile.WriteAllText(Path.Combine(folder, ResumeFile), input.ResumeText);
            if (!string.IsNullOrWhiteSpace(input.ResumeFilePath))
                KeepOriginalResume(input.ResumeFilePath, folder);
            AtomicFile.WriteAllText(Path.Combine(folder, NotesFile), "");
            AtomicFile.WriteJson(Path.Combine(folder, MetaFile), meta);
        }
        catch
        {
            // Never leave a half-made application behind (no meta.json would hide it from the list, but the files would clutter the folder).
            TryRemoveNewFolder(folder);
            throw;
        }

        return new JobApplication { FolderPath = folder, Meta = meta };
    }

    /// <summary>The attached original resume of an application (resume.original.pdf and so on), or null when there is none.</summary>
    public string? FindOriginalResume(JobApplication app) =>
        Directory.Exists(app.FolderPath)
            ? Directory.EnumerateFiles(app.FolderPath, OriginalResumePrefix + ".*").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            : null;

    private static void KeepOriginalResume(string sourcePath, string folder)
    {
        // Only the extension of a supported file type is kept, so an odd name can never write outside the folder or to an odd file type.
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!Resumes.ResumeFileReader.SupportedExtensions.Contains(extension))
            throw new IOException("That kind of file cannot be kept with the application.");

        var target = Path.Combine(folder, OriginalResumePrefix + extension);
        var temp = target + ".tmp";
        File.WriteAllBytes(temp, Resumes.ResumeFileReader.ReadShared(sourcePath));
        File.Move(temp, target, overwrite: true);
    }

    private static void TryRemoveNewFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            var company = Path.GetDirectoryName(folder);
            if (company is not null && Directory.Exists(company) && !Directory.EnumerateFileSystemEntries(company).Any())
                Directory.Delete(company);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // cleaning up is best effort: the original problem is the one to report
        }
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

    /// <summary>
    /// The pay the job description states, or null. Read from the saved description first; if that has none, from the original paste (clean-up can
    /// drop a "Pay" box that sat outside the description). Nothing is stored: it is worked out from the text each time, so editing the description
    /// updates it.
    /// </summary>
    /// <summary>
    /// The pay for the list: the remembered answer when the description has not changed (a quick check that does not open the file), otherwise read
    /// from the files and remembered. <paramref name="cache"/> is saved by the caller once a batch is done.
    /// </summary>
    /// <summary>True with the remembered pay when it is known and still valid, without reading any file.</summary>
    public bool TryKnownPay(JobApplication app, Pay.PayCache cache, out Pay.PayInfo? pay) => cache.TryGet(Root, app, out pay);

    public Pay.PayInfo? ReadPayCached(JobApplication app, Pay.PayCache cache)
    {
        if (cache.TryGet(Root, app, out var known))
            return known;
        var pay = ReadPay(app);
        cache.Put(Root, app, pay);
        return pay;
    }

    public Pay.PayInfo? ReadPay(JobApplication app) =>
        Pay.PayExtractor.Extract(ReadJd(app)) ?? Pay.PayExtractor.Extract(ReadOrEmpty(Path.Combine(app.FolderPath, OriginalJdFile)));
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
