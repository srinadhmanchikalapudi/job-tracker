using JobTracker.Core.Resumes;

namespace JobTracker.Core.Export;

/// <summary>
/// Keeps one upload-ready Word file in an application's folder, always for the resume that is in use: copied from the person's own Word file, or laid
/// out clean, and named First_Last_Resume.docx. When the resume changes (another version is chosen, or it is edited) the file is made again and the old
/// one is removed if it had another name. Only a file this app recorded in meta.json is ever replaced or removed, never anything the person put there.
/// </summary>
public static class CurrentResumeWord
{
    /// <summary>
    /// Makes the file for the resume as it is now. Returns null when there is no resume text. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the file cannot be written (usually because it is open in Word); the previous file is then left as it was.
    /// </summary>
    public static DocxExport? Refresh(ApplicationStore store, JobApplication app)
    {
        var text = store.ReadResume(app);
        if (text.Trim().Length == 0)
            return null;

        var name = DownloadNames.Suggest(text);
        var export = ResumeDocxExporter.Export(app.FolderPath, text, Path.Combine(app.FolderPath, name));

        // The old file goes only after the new one exists, and only if this app made it.
        var previous = app.Meta.ResumeWordFile;
        if (!string.IsNullOrEmpty(previous) && !string.Equals(previous, name, StringComparison.OrdinalIgnoreCase))
            TryDelete(Path.Combine(app.FolderPath, Path.GetFileName(previous))); // GetFileName: a meta.json cannot point outside the folder

        if (!string.Equals(previous, name, StringComparison.Ordinal))
        {
            app.Meta.ResumeWordFile = name;
            store.SaveMeta(app);
        }

        return export;
    }

    /// <summary>The path of the file for the resume in use, or null when none has been made yet.</summary>
    public static string? ExistingPath(JobApplication app) =>
        app.Meta.ResumeWordFile is { Length: > 0 } name && Path.Combine(app.FolderPath, Path.GetFileName(name)) is var path && File.Exists(path) ? path : null;

    private static void TryDelete(string path)
    {
        try
        {
            if (path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // an old file left behind (open in Word) is only untidy; the new one is already there
        }
    }
}