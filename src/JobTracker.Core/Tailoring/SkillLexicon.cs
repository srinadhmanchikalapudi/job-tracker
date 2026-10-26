using System.Reflection;
using System.Text.RegularExpressions;

namespace JobTracker.Core.Tailoring;

/// <summary>A skill found in a piece of text: its shown name, and where the match starts.</summary>
public sealed record SkillHit(string Skill, int Index, int Length);

/// <summary>
/// The skills the app recognises (skills.txt, embedded). Used the same way on a job description and on a resume, so "In your resume" and
/// "Missing" are decided by one set of rules: whole words only (Java is not JavaScript), punctuation kept (C#, .NET, C++), and a few short
/// words (Go, Spark) only match when written exactly like that.
/// </summary>
public sealed class SkillLexicon
{
    private sealed record Entry(string Skill, Regex Pattern);

    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, Entry> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SkillCategory> _categories = new(StringComparer.OrdinalIgnoreCase);

    public static SkillLexicon Default { get; } = new(LoadEmbedded());

    public SkillLexicon(IEnumerable<string> lines)
    {
        var category = SkillCategory.Other;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            // [Cloud] starts a category; the entries below it belong to it.
            if (line[0] == '[' && line[^1] == ']')
            {
                category = Enum.TryParse<SkillCategory>(line[1..^1].Trim(), ignoreCase: true, out var parsed) ? parsed : SkillCategory.Other;
                continue;
            }

            var parts = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var skill = parts[0].TrimStart('=');
            var aliases = parts.Skip(1).ToList();
            // A "=Go" alias that equals the name replaces the everyday-word version of it.
            if (!aliases.Contains("=" + skill, StringComparer.Ordinal))
                aliases.Insert(0, skill);

            var entry = new Entry(skill, BuildPattern(aliases));
            _entries.Add(entry);
            _byName[skill] = entry;
            _categories[skill] = category;
        }
    }

    /// <summary>What kind of requirement the entry is (cloud, architecture, leadership…), from its [Category] in skills.txt.</summary>
    public SkillCategory CategoryOf(string skill) => _categories.TryGetValue(skill, out var c) ? c : SkillCategory.Other;

    public IReadOnlyList<string> Skills => _entries.Select(e => e.Skill).ToList();

    /// <summary>
    /// Every skill in the text, in the order it first appears, with every place it appears. With <paramref name="dropNested"/>, a skill that
    /// is only part of a longer one is left out ("SQL" inside "SQL Server"): used on a job description, which asked for the longer one.
    /// A resume is read without it, so "SQL Server" there still covers a job description that asks for "SQL".
    /// </summary>
    public IReadOnlyList<SkillHit> Find(string text, bool dropNested = false)
    {
        var hits = new List<SkillHit>();
        if (string.IsNullOrEmpty(text))
            return hits;

        foreach (var entry in _entries)
            foreach (Match m in entry.Pattern.Matches(text))
                hits.Add(new SkillHit(entry.Skill, m.Index, m.Length));
        if (dropNested)
        {
            var all = hits.ToList(); // RemoveAll must not read the list it is shrinking
            hits.RemoveAll(a => all.Any(b => b.Length > a.Length && b.Index <= a.Index && a.Index + a.Length <= b.Index + b.Length &&
                                              !string.Equals(a.Skill, b.Skill, StringComparison.OrdinalIgnoreCase)));
        }

        return hits.OrderBy(h => h.Index).ToList();
    }

    public bool Contains(string text, string skill) =>
        _byName.TryGetValue(skill, out var entry) && entry.Pattern.IsMatch(text);

    public string? Canonical(string name) => _byName.TryGetValue(name, out var entry) ? entry.Skill : null;

    private static Regex BuildPattern(List<string> aliases)
    {
        // Case-sensitive aliases are rare, so each alias carries its own (?i) or (?-i) switch.
        var alternatives = aliases.Select(a =>
        {
            var exact = a.StartsWith('=');
            var word = Regex.Escape(exact ? a[1..] : a);
            // Plurals of ordinary words (microservice / microservices), but not of names that end in a symbol.
            var plural = char.IsLetter(a[^1]) && a.Length > 3 ? "(?:s)?" : "";
            return $"(?{(exact ? "-i" : "i")}:{word}{plural})";
        });
        // Not part of a longer word: no letter or digit just before, and none just after (or a # or + that would make it a different name).
        return new Regex($@"(?<![\p{{L}}\p{{N}}])(?:{string.Join('|', alternatives)})(?![\p{{L}}\p{{N}}#+])",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private static IEnumerable<string> LoadEmbedded()
    {
        var assembly = typeof(SkillLexicon).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("skills.txt", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
    }
}
