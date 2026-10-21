namespace JobTracker.Core.Tailoring;

public enum MatchStatus
{
    /// <summary>The resume already says it.</summary>
    InResume,

    /// <summary>Not in the resume, but the person said they have used it (and may have described how).</summary>
    Attested,

    /// <summary>Neither in the resume nor claimed.</summary>
    Missing,
}

/// <param name="ResumeLines">Up to three resume lines that mention the skill, so the person can see where it is covered.</param>
/// <param name="Note">What the person wrote about how they used it (only for Attested).</param>
public sealed record TermMatch(JdTerm Term, MatchStatus Status, IReadOnlyList<string> ResumeLines, string? Note);

/// <summary>The job description's skills laid against the resume. Every number here is counted by code from those two texts.</summary>
public sealed record MatchReport(IReadOnlyList<TermMatch> Matches)
{
    public IEnumerable<TermMatch> Core => Matches.Where(m => m.Term.Weight != TermWeight.Preferred);
    public IEnumerable<TermMatch> Preferred => Matches.Where(m => m.Term.Weight == TermWeight.Preferred);

    public int CoreTotal => Core.Count();
    public int CoreInResume => Core.Count(m => m.Status == MatchStatus.InResume);
    public int CoreAttested => Core.Count(m => m.Status == MatchStatus.Attested);
    public int PreferredTotal => Preferred.Count();
    public int PreferredInResume => Preferred.Count(m => m.Status == MatchStatus.InResume);

    /// <summary>Share of the core skills the resume already says, 0 to 100. Claimed-but-not-written skills are not counted until they are in the resume.</summary>
    public int CoreCoveragePercent => CoreTotal == 0 ? 0 : (int)Math.Round(100.0 * CoreInResume / CoreTotal);
}

public static class ResumeMatcher
{
    public static MatchReport Match(JdAnalysis jd, string resumeText, TailorState? state = null, SkillLexicon? lexicon = null)
    {
        lexicon ??= SkillLexicon.Default;
        state ??= new TailorState();

        var resumeLines = BulletText.Structure(resumeText).Where(l => l.Kind != LineKind.Blank).ToList();
        var inResume = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in resumeLines)
            foreach (var skill in lexicon.Find(line.Display).Select(h => h.Skill).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!inResume.TryGetValue(skill, out var list))
                    inResume[skill] = list = [];
                list.Add(line.Display);
            }

        var matches = new List<TermMatch>();
        foreach (var term in jd.Terms.Where(t => !state.IsDismissed(t.Skill)))
        {
            if (inResume.TryGetValue(term.Skill, out var lines))
                matches.Add(new TermMatch(term, MatchStatus.InResume, lines.Take(3).ToList(), null));
            else if (state.FindAttested(term.Skill) is { } attested)
                matches.Add(new TermMatch(term, MatchStatus.Attested, [], attested.Note));
            else
                matches.Add(new TermMatch(term, MatchStatus.Missing, [], null));
        }

        return new MatchReport(matches);
    }
}
