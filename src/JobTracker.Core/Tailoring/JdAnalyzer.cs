using System.Text.RegularExpressions;

namespace JobTracker.Core.Tailoring;

/// <summary>How much a job description leans on a skill. Required beats Duty beats Preferred when it appears more than once.</summary>
public enum TermWeight
{
    /// <summary>Under a requirements heading, or on a line that says required, must have, or "N+ years".</summary>
    Required,

    /// <summary>Part of the work itself (responsibilities, the role, anything not marked either way).</summary>
    Duty,

    /// <summary>Nice to have, a plus, preferred, bonus.</summary>
    Preferred,
}

/// <param name="Skill">The name the app shows (from skills.txt).</param>
/// <param name="Count">How many times the job description says it.</param>
/// <param name="Evidence">The first line of the job description that mentions it, so the person can see why it is listed.</param>
public sealed record JdTerm(string Skill, TermWeight Weight, int Count, string Evidence);

public sealed record JdAnalysis(IReadOnlyList<JdTerm> Terms)
{
    public IEnumerable<JdTerm> Core => Terms.Where(t => t.Weight != TermWeight.Preferred);
    public IEnumerable<JdTerm> Preferred => Terms.Where(t => t.Weight == TermWeight.Preferred);
}

/// <summary>
/// Reads a job description and lists the skills it asks for, with how much it leans on each. Plain rules, no AI: the same job description
/// always gives the same list, and every entry can point at the line it came from.
/// </summary>
public static class JdAnalyzer
{
    private static readonly Regex PreferredHeading = new(@"nice.to.have|preferred|bonus|a plus|desired|good to have|extra credit|would be (?:great|nice)|ideal(?:ly)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RequiredHeading = new(@"requirement|qualification|must|required|what you.?(?:ll)? bring|what we.?re looking|who you are|you have|you.ll have|minimum|your skills|skills|experience|about you",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DutyHeading = new(@"responsib|what you.?(?:ll)? do|you will|the role|about the (?:role|job|position|team)|day.to.day|your impact|in this role|the work",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Sections about the company or the package say nothing about what the person needs to have done.
    private static readonly Regex IgnoredHeading = new(@"about (?:us|the company)|who we are|benefit|perk|compensation|salary|pay |equal opportunity|eeo|how to apply|our (?:values|culture|mission)|why join|why you.ll love",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RequiredLine = new(@"\b(?:required|requires|must[- ]have|must|minimum|\d+\s*\+?\s*years?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PreferredLine = new(@"\b(?:preferred|nice to have|a plus|bonus|ideally|familiarity with|exposure to|is a plus|are a plus)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static JdAnalysis Analyze(string jdText, SkillLexicon? lexicon = null)
    {
        lexicon ??= SkillLexicon.Default;
        var found = new Dictionary<string, (TermWeight Weight, int Count, string Evidence, int Order)>(StringComparer.OrdinalIgnoreCase);
        var current = TermWeight.Duty;
        var ignoring = false;

        foreach (var line in BulletText.Structure(jdText))
        {
            if (line.Kind == LineKind.Blank)
                continue;

            // A heading steers the sections below it, and is also read as text ("Senior .NET Engineer", "Strong SQL experience").
            if (line.Kind == LineKind.Heading)
                (ignoring, current) = Classify(line.Display, current, ignoring);

            if (ignoring)
                continue;

            var weight = current;
            if (PreferredLine.IsMatch(line.Display))
                weight = TermWeight.Preferred;
            else if (RequiredLine.IsMatch(line.Display))
                weight = TermWeight.Required;

            foreach (var group in lexicon.Find(line.Display, dropNested: true).GroupBy(h => h.Skill, StringComparer.OrdinalIgnoreCase))
            {
                var count = group.Count();
                if (found.TryGetValue(group.Key, out var existing))
                    found[group.Key] = (Stronger(existing.Weight, weight), existing.Count + count, existing.Evidence, existing.Order);
                else
                    found[group.Key] = (weight, count, line.Display, found.Count);
            }
        }

        var terms = found
            .OrderBy(kv => kv.Value.Weight)
            .ThenByDescending(kv => kv.Value.Count)
            .ThenBy(kv => kv.Value.Order)
            .Select(kv => new JdTerm(lexicon.Canonical(kv.Key) ?? kv.Key, kv.Value.Weight, kv.Value.Count, kv.Value.Evidence))
            .ToList();
        return new JdAnalysis(terms);
    }

    private static (bool Ignoring, TermWeight Weight) Classify(string heading, TermWeight current, bool ignoring)
    {
        if (PreferredHeading.IsMatch(heading))
            return (false, TermWeight.Preferred);
        if (IgnoredHeading.IsMatch(heading))
            return (true, current);
        if (RequiredHeading.IsMatch(heading))
            return (false, TermWeight.Required);
        if (DutyHeading.IsMatch(heading))
            return (false, TermWeight.Duty);
        return (ignoring, current); // an unrecognised heading keeps whatever section we were in
    }

    private static TermWeight Stronger(TermWeight a, TermWeight b) => a <= b ? a : b;
}
