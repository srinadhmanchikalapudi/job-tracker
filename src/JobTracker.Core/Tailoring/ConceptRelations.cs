using System.Text.RegularExpressions;

namespace JobTracker.Core.Tailoring;

/// <summary>How strongly a piece of wording evidences a requirement it does not name.</summary>
public enum RelationKind
{
    /// <summary>Saying this means the person has done the target (Terraform is infrastructure as code).</summary>
    Strong,

    /// <summary>Closely related wording ("serverless" on Azure for Azure Functions). Counts half.</summary>
    Partial,

    /// <summary>Adjacent work worth pointing out. Never scored.</summary>
    Related,
}

/// <param name="Line">The resume line that carries the wording.</param>
/// <param name="Reason">What in the line was recognised, for example "Serverless + Azure".</param>
public sealed record RelationHit(RelationKind Kind, string Line, string Reason);

/// <summary>
/// The rules in relations.txt: which wording on a resume counts as evidence for a requirement that it does not name. Plain data, read the same
/// way every time, so every partial or implied match can say exactly which words it came from.
/// </summary>
public sealed class ConceptRelations
{
    private sealed record Rule(string Target, RelationKind Kind, IReadOnlyList<IReadOnlyList<string>> Alternatives);

    private static readonly Regex RuleLine = new(@"^(?<t>.+?)\s*::\s*(?<k>strong|partial|related)\s*:\s*(?<a>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly List<Rule> _rules = [];
    private readonly SkillLexicon _lexicon;
    private readonly Dictionary<string, Regex> _phrases = new(StringComparer.Ordinal);

    public static ConceptRelations Default { get; } = new(LoadEmbedded(), SkillLexicon.Default);

    public ConceptRelations(IEnumerable<string> lines, SkillLexicon lexicon)
    {
        _lexicon = lexicon;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (RuleLine.Match(line) is not { Success: true } m)
                throw new FormatException($"A line in relations.txt could not be read: {line}");

            var alternatives = m.Groups["a"].Value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(a => (IReadOnlyList<string>)a.Split(" + ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList())
                .ToList();
            _rules.Add(new Rule(m.Groups["t"].Value.Trim(), Enum.Parse<RelationKind>(m.Groups["k"].Value, ignoreCase: true), alternatives));
        }
    }

    /// <summary>Every target and every thing named in a rule, so a test can check they all exist in the catalogue.</summary>
    public IEnumerable<string> Targets => _rules.Select(r => r.Target).Distinct(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> SkillTokens => _rules.SelectMany(r => r.Alternatives).SelectMany(a => a).Where(t => !IsPhrase(t)).Distinct(StringComparer.OrdinalIgnoreCase);

    public bool HasRules(string target, RelationKind kind) =>
        _rules.Any(r => r.Kind == kind && string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase));

    /// <summary>The lines that evidence <paramref name="target"/> by rules of this kind, each with the words that matched. One hit per line.</summary>
    public IReadOnlyList<RelationHit> Find(string target, IEnumerable<string> lines, RelationKind kind)
    {
        var rules = _rules.Where(r => r.Kind == kind && string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase)).ToList();
        var hits = new List<RelationHit>();
        if (rules.Count == 0)
            return hits;

        foreach (var line in lines)
        {
            foreach (var alternative in rules.SelectMany(r => r.Alternatives))
            {
                if (alternative.All(token => Satisfied(token, line)))
                {
                    hits.Add(new RelationHit(kind, line, string.Join(" + ", alternative.Select(t => t.Trim('"').TrimEnd('*')))));
                    break; // one reason per line is enough
                }
            }
        }

        return hits;
    }

    private static bool IsPhrase(string token) => token.Length > 1 && token[0] == '"' && token[^1] == '"';

    private bool Satisfied(string token, string line)
    {
        if (!IsPhrase(token))
            return _lexicon.Contains(line, token);

        if (!_phrases.TryGetValue(token, out var regex))
        {
            var phrase = token[1..^1];
            var open = phrase.EndsWith('*'); // "claim*": the word may carry on
            var body = Regex.Escape(open ? phrase[..^1] : phrase);
            regex = new Regex($@"(?<![\p{{L}}\p{{N}}]){body}{(open ? @"[\p{L}\p{N}]*" : "")}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            _phrases[token] = regex;
        }

        return regex.IsMatch(line);
    }

    private static IEnumerable<string> LoadEmbedded()
    {
        var assembly = typeof(ConceptRelations).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("relations.txt", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
    }
}