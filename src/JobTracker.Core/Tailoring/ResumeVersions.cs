using System.Text.RegularExpressions;

namespace JobTracker.Core.Tailoring;

public sealed record ResumeVersion(int Number, string Path, DateTime SavedAt)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Tailored copies of a resume, kept next to it as resume.v2.md, resume.v3.md and so on. resume.md itself is only replaced when the person
/// asks, and the words it held are kept as another version first, so no text is ever lost.
/// </summary>
public static class ResumeVersions
{
    private static readonly Regex NameFormat = new(@"^resume\.v(?<n>\d+)\.md$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<ResumeVersion> List(string applicationFolder)
    {
        if (!Directory.Exists(applicationFolder))
            return [];

        return Directory.EnumerateFiles(applicationFolder, "resume.v*.md")
            .Select(path => (path, match: NameFormat.Match(Path.GetFileName(path))))
            .Where(x => x.match.Success)
            .Select(x => new ResumeVersion(int.Parse(x.match.Groups["n"].Value), x.path, File.GetLastWriteTime(x.path)))
            .OrderBy(v => v.Number)
            .ToList();
    }

    public static string Read(ResumeVersion version) => File.ReadAllText(version.Path);

    /// <summary>Writes the text as the next version (the first one is resume.v2.md, because resume.md is the first).</summary>
    public static ResumeVersion SaveNew(string applicationFolder, string text)
    {
        var next = Math.Max(2, List(applicationFolder).Select(v => v.Number).DefaultIfEmpty(1).Max() + 1);
        var path = Path.Combine(applicationFolder, $"resume.v{next}.md");
        AtomicFile.WriteAllText(path, text);
        return new ResumeVersion(next, path, File.GetLastWriteTime(path));
    }

    /// <summary>Makes a version the resume. What resume.md held is saved as a new version first.</summary>
    public static ResumeVersion? UseAsResume(string applicationFolder, ResumeVersion version)
    {
        var resumePath = Path.Combine(applicationFolder, ApplicationStore.ResumeFile);
        var current = File.Exists(resumePath) ? File.ReadAllText(resumePath) : "";
        var chosen = Read(version);
        if (current == chosen)
            return null;

        var backup = current.Trim().Length > 0 && !List(applicationFolder).Any(v => Read(v) == current)
            ? SaveNew(applicationFolder, current)
            : null;
        AtomicFile.WriteAllText(resumePath, chosen);
        return backup;
    }
}
