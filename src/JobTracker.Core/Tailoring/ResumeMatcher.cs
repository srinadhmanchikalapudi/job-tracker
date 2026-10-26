namespace JobTracker.Core.Tailoring;

public enum MatchStatus
{
    /// <summary>The resume already says it (or says something that means it: Terraform for "infrastructure as code").</summary>
    InResume,

    /// <summary>Closely related wording is there ("serverless applications on Azure" for Azure Functions), but not the requirement itself.</summary>
    Partial,

    /// <summary>Not in the resume, but the person said they have used it (and may have described how).</summary>
    Attested,

    /// <summary>Neither in the resume nor claimed.</summary>
    Missing,
}

/// <param name="Text">The resume line.</param>
/// <param name="Why">What in it counts: the skill itself, or the related words that were recognised.</param>
/// <param name="InSkillsList">The line is in a skills section (a mention), not in the work history.</param>
public sealed record EvidenceLine(string Text, string Why, bool InSkillsList);

/// <param name="ResumeLines">Up to three resume lines that mention the skill, so the person can see where it is covered.</param>
/// <param name="Note">What the person wrote about how they used it (only for Attested).</param>
/// <param name="Evidence">Every resume line that supports the status: the skill itself for InResume, the related wording for Partial.</param>
/// <param name="Related">Adjacent work found for something that is not in the resume. Shown as "potential evidence"; never scored.</param>
/// <param name="ImpliedBy">When InResume came from other wording that means it, what that wording is ("Terraform"); null when the resume names it.</param>
/// <param name="UsedInExperience">At least one of the lines is in the work history, not only in a skills list.</param>
public sealed record TermMatch(
    JdTerm Term,
    MatchStatus Status,
    IReadOnlyList<string> ResumeLines,
    string? Note,
    IReadOnlyList<EvidenceLine>? Evidence = null,
    IReadOnlyList<EvidenceLine>? Related = null,
    string? ImpliedBy = null,
    bool UsedInExperience = false)
{
    public IReadOnlyList<EvidenceLine> EvidenceLines => Evidence ?? [];
    public IReadOnlyList<EvidenceLine> RelatedLines => Related ?? [];
}

/// <summary>The job description's skills laid against the resume. Every number here is counted by code from those two texts.</summary>
public sealed record MatchReport(IReadOnlyList<TermMatch> Matches)
{
    public IEnumerable<TermMatch> Core => Matches.Where(m => m.Term.Weight != TermWeight.Preferred);
    public IEnumerable<TermMatch> Preferred => Matches.Where(m => m.Term.Weight == TermWeight.Preferred);

    public int CoreTotal => Core.Count();
    public int CoreInResume => Core.Count(m => m.Status == MatchStatus.InResume);
    public int CorePartial => Core.Count(m => m.Status == MatchStatus.Partial);
    public int CoreAttested => Core.Count(m => m.Status == MatchStatus.Attested);
    public int PreferredTotal => Preferred.Count();
    public int PreferredInResume => Preferred.Count(m => m.Status == MatchStatus.InResume);

    /// <summary>Share of the core skills the resume already says, 0 to 100. Claimed-but-not-written skills are not counted until they are in the resume.</summary>
    public int CoreCoveragePercent => CoreTotal == 0 ? 0 : (int)Math.Round(100.0 * CoreInResume / CoreTotal);
}

public static class ResumeMatcher
{
    public static MatchReport Match(JdAnalysis jd, string resumeText, TailorState? state = null, SkillLexicon? lexicon = null, ConceptRelations? relations = null)
    {
        lexicon ??= SkillLexicon.Default;
        relations ??= ReferenceEquals(lexicon, SkillLexicon.Default) ? ConceptRelations.Default : null;
        state ??= new TailorState();

        var resumeLines = ResumeLineReader.Read(resumeText);
        var texts = resumeLines.Select(l => l.Text).ToList();
        bool Listed(string text) => resumeLines.First(l => l.Text == text).InSkillsList;

        // Where the resume names each skill itself.
        var named = new Dictionary<string, List<ResumeLine>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in resumeLines)
            foreach (var skill in lexicon.Find(line.Text).Select(h => h.Skill).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!named.TryGetValue(skill, out var list))
                    named[skill] = list = [];
                list.Add(line);
            }

        EvidenceLine Evidence(ResumeLine line, string why) => new(line.Text, why, line.InSkillsList);
        IReadOnlyList<EvidenceLine> FromHits(IEnumerable<RelationHit> hits) =>
            hits.Select(h => new EvidenceLine(h.Line, h.Reason, Listed(h.Line))).ToList();

        var matches = new List<TermMatch>();
        foreach (var term in jd.Terms.Where(t => !state.IsDismissed(t.Skill)))
        {
            var partial = relations?.Find(term.Skill, texts, RelationKind.Partial) ?? [];
            // Adjacent work is shown as potential evidence only when it is work: a line in a skills list that happens to name a related tool says nothing about what was done.
            var related = (relations?.Find(term.Skill, texts, RelationKind.Related) ?? []).Where(h => !Listed(h.Line)).ToList();

            if (named.TryGetValue(term.Skill, out var lines))
            {
                // Lines in the work history first: they show the skill was used, not only listed.
                var ordered = lines.OrderBy(l => l.InSkillsList).ToList();
                matches.Add(new TermMatch(term, MatchStatus.InResume, ordered.Take(3).Select(l => l.Text).ToList(), null,
                    ordered.Select(l => Evidence(l, term.Skill)).ToList(), [], null, ordered.Any(l => !l.InSkillsList)));
                continue;
            }

            var implied = relations?.Find(term.Skill, texts, RelationKind.Strong) ?? [];
            if (implied.Count > 0)
            {
                var evidence = FromHits(implied).OrderBy(e => e.InSkillsList).ToList();
                matches.Add(new TermMatch(term, MatchStatus.InResume, evidence.Take(3).Select(e => e.Text).ToList(), null, evidence, [],
                    string.Join(", ", implied.Select(h => h.Reason).Distinct(StringComparer.OrdinalIgnoreCase).Take(3)), evidence.Any(e => !e.InSkillsList)));
            }
            else if (state.FindAttested(term.Skill) is { } attested)
            {
                // The person's own word. It does not make the resume say it, so for an ATS it is still only as good as any related wording.
                var evidence = FromHits(partial).OrderBy(e => e.InSkillsList).ToList();
                matches.Add(new TermMatch(term, MatchStatus.Attested, [], attested.Note, evidence, FromHits(related), null, evidence.Any(e => !e.InSkillsList)));
            }
            else if (partial.Count > 0)
            {
                var evidence = FromHits(partial).OrderBy(e => e.InSkillsList).ToList();
                matches.Add(new TermMatch(term, MatchStatus.Partial, evidence.Take(3).Select(e => e.Text).ToList(), null, evidence, FromHits(related), null,
                    evidence.Any(e => !e.InSkillsList)));
            }
            else
            {
                matches.Add(new TermMatch(term, MatchStatus.Missing, [], null, [], FromHits(related)));
            }
        }

        return new MatchReport(matches);
    }
}