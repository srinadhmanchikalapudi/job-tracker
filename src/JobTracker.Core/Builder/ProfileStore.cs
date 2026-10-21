using JobTracker.Core.Export;

namespace JobTracker.Core.Builder;

/// <summary>Where the resume built from scratch lives: &lt;data folder&gt;\.jobtracker\resume-builder, so it is backed up and moved with everything else.</summary>
public static class ResumeBuilderFiles
{
    public const string FolderName = "resume-builder";
    public const string ProfileFile = "profile.json";
    public const string WordFile = "My resume.docx";

    public static string Folder(string root) => Path.Combine(root, ApplicationStore.AppDataFolder, FolderName);

    /// <summary>True when a resume has been started (so changing the data folder has something to carry over).</summary>
    public static bool Exists(string root) => File.Exists(Path.Combine(Folder(root), ProfileFile));
}

/// <summary>Saves and loads the resume being built. Writes are atomic, so a crash cannot leave half a resume.</summary>
public sealed class ProfileStore(string root)
{
    private string ProfilePath => Path.Combine(ResumeBuilderFiles.Folder(root), ResumeBuilderFiles.ProfileFile);

    public string WordPath => Path.Combine(ResumeBuilderFiles.Folder(root), ResumeBuilderFiles.WordFile);

    public bool Exists => ResumeBuilderFiles.Exists(root);

    public ResumeProfile Load() => AtomicFile.ReadJson<ResumeProfile>(ProfilePath) ?? new ResumeProfile();

    public void Save(ResumeProfile profile) => AtomicFile.WriteJson(ProfilePath, profile);

    /// <summary>Deletes the resume being built (and its Word file), for "start over".</summary>
    public void Delete()
    {
        var folder = ResumeBuilderFiles.Folder(root);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    /// <summary>
    /// The resume as a Word document, made and read back to check it, ready to look at or download. The document is always the clean one-column
    /// layout (there is no original file to keep the look of).
    /// </summary>
    public DocxExport MakeWord(ResumeProfile profile) =>
        ResumeDocxExporter.Export(ResumeBuilderFiles.Folder(root), ResumeProfileText.Build(profile), WordPath);
}

/// <summary>Puts a bullet together from its parts, for people who do not want to start from a blank box. Only the person's own words go in.</summary>
public static class BulletComposer
{
    /// <summary>Strong ways to open a bullet, offered as suggestions (the person can type any verb).</summary>
    public static IReadOnlyList<string> Verbs { get; } =
    [
        "Built", "Designed", "Led", "Managed", "Created", "Developed", "Delivered", "Launched", "Improved", "Reduced", "Increased", "Cut", "Automated",
        "Migrated", "Implemented", "Maintained", "Analyzed", "Coordinated", "Trained", "Mentored", "Negotiated", "Resolved", "Streamlined", "Wrote",
        "Organized", "Supported", "Planned", "Tested", "Owned", "Ran",
    ];

    /// <summary>"Built a billing service using C# and SQL Server, cutting invoice errors by 40%". Empty when the verb or the what is missing.</summary>
    public static string Compose(string verb, string what, string tools, string result)
    {
        verb = verb.Trim();
        what = what.Trim().TrimEnd('.', ',', ';');
        if (verb.Length == 0 || what.Length == 0)
            return "";

        var text = char.ToUpperInvariant(verb[0]) + verb[1..] + " " + what;

        tools = tools.Trim().TrimEnd('.', ',', ';');
        if (tools.StartsWith("using ", StringComparison.OrdinalIgnoreCase))
            tools = tools[6..].Trim();
        if (tools.Length > 0)
            text += " using " + tools;

        result = result.Trim().TrimStart(',', ';', ' ').TrimEnd('.', ',', ';');
        if (result.Length > 0)
        {
            // "Cutting latency by 63%" reads after a comma as "cutting"; an acronym or a number keeps its form.
            var keepCase = result.Length > 1 && (char.IsUpper(result[1]) || char.IsDigit(result[0]));
            text += ", " + (keepCase ? result : char.ToLowerInvariant(result[0]) + result[1..]);
        }

        return text;
    }
}