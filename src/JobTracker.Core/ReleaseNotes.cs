using System.Reflection;
using System.Text.RegularExpressions;
using JobTracker.Core.Updates;

namespace JobTracker.Core;

/// <summary>One version's notes: what changed, as short lines for a person to read.</summary>
public sealed record ReleaseSection(Version Version, IReadOnlyList<string> Items)
{
    public string Title => $"Version {UpdateVersions.Display(Version)}";
}

public static partial class ReleaseNotes
{
    private const string ResourceName = "JobTracker.Core.WhatsNew.md";

    [GeneratedRegex(@"^##\s+v?(?<v>\d+\.\d+(?:\.\d+)?)\b")]
    private static partial Regex VersionHeading();

    [GeneratedRegex(@"\*\*(?<t>.+?)\*\*|`(?<t>[^`]+)`")]
    private static partial Regex Emphasis();

    /// <summary>The program shows plain text, while the same notes are markdown on GitHub: drop **bold** and `code` markers.</summary>
    private static string Plain(string text) => Emphasis().Replace(text, m => m.Groups["t"].Value);

    [GeneratedRegex(@"^(?:app|core|installer|docs|tools|ci|build|fix|feat)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex AreaPrefix();

    /// <summary>
    /// The notes that ship inside the program (WhatsNew.md), newest version first. They are written for people, one section per version
    /// ("## 1.3.0" then "- a line" for each change), so they can be read offline, including the version just installed.
    /// </summary>
    public static IReadOnlyList<ReleaseSection> History()
    {
        using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            return [];
        using var reader = new StreamReader(stream);
        return ParseHistory(reader.ReadToEnd());
    }

    /// <summary>Reads "## 1.2.0" headings followed by "- item" lines. Anything else is ignored. Newest version first.</summary>
    public static IReadOnlyList<ReleaseSection> ParseHistory(string markdown)
    {
        var sections = new List<(Version Version, List<string> Items)>();
        List<string>? current = null;

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (VersionHeading().Match(line) is { Success: true } heading && UpdateVersions.Parse(heading.Groups["v"].Value) is { } version)
            {
                current = [];
                sections.Add((version, current));
            }
            else if (current is not null && line.StartsWith("- ", StringComparison.Ordinal) && line.Length > 2)
            {
                current.Add(Plain(line[2..].Trim()));
            }
        }

        return sections
            .OrderByDescending(s => s.Version)
            .Select(s => new ReleaseSection(s.Version, s.Items))
            .ToList();
    }

    /// <summary>
    /// The notes of a GitHub release (its body), for an update that is available but not installed yet. It keeps the change lines and drops the
    /// install instructions and the "Changes since" footer. A leading "app:" or "core:" (from a commit subject) is dropped and the line capitalised.
    /// Null when the body has no change lines.
    /// </summary>
    public static ReleaseSection? FromGitHubBody(string? body, Version version)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        var items = new List<string>();
        foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("**Install", StringComparison.OrdinalIgnoreCase) || line.StartsWith("Windows may show", StringComparison.OrdinalIgnoreCase))
                break;
            if (!line.StartsWith("- ", StringComparison.Ordinal) || line.Length <= 2)
                continue;

            var text = Plain(AreaPrefix().Replace(line[2..].Trim(), ""));
            if (text.Length == 0)
                continue;
            items.Add(char.ToUpperInvariant(text[0]) + text[1..]);
        }

        return items.Count == 0 ? null : new ReleaseSection(version, items);
    }
}

public enum WhatsNewAction
{
    /// <summary>Nothing to show or record.</summary>
    Nothing,

    /// <summary>Remember the running version without showing anything (a brand-new install).</summary>
    RecordOnly,

    /// <summary>Show what is new, then remember the running version.</summary>
    Show,
}

public static class WhatsNewPolicy
{
    /// <summary>
    /// Decides whether to announce the running version. A brand-new install says nothing (there is nothing to compare with). An existing install
    /// with no record yet (it updated into this feature) is shown the notes once. After that, a newer version is announced once.
    /// </summary>
    public static WhatsNewAction Decide(string? lastSeenVersion, bool isFirstRun, Version current)
    {
        if (UpdateVersions.Parse(lastSeenVersion) is not { } seen)
            return isFirstRun ? WhatsNewAction.RecordOnly : WhatsNewAction.Show;

        return UpdateVersions.IsNewer(current, seen) ? WhatsNewAction.Show : WhatsNewAction.Nothing;
    }
}
