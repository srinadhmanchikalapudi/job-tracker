namespace JobTracker.Core.Tailoring;

public enum MatchLevel
{
    /// <summary>The resume says it (or says something that means it).</summary>
    Strong,

    /// <summary>Related wording is there, but not the requirement itself.</summary>
    Partial,

    /// <summary>The person said they have used it, but the resume does not say so, and an ATS reads only the resume.</summary>
    Claimed,

    Missing,
}

public enum Tier { Critical, Important, Optional }

public enum RiskLevel { High, Medium, Low }

/// <summary>Where the case for putting a requirement into the resume comes from. Nothing is added to a resume without the person's own word.</summary>
public enum RewriteSource
{
    /// <summary>The person said they used it.</summary>
    UserConfirmed,

    /// <summary>Not confirmed, but related wording or work was found in the resume.</summary>
    SystemInferred,

    /// <summary>Only the job description asks for it. Nothing in the resume points to it.</summary>
    JobDescriptionOnly,
}

/// <summary>The parts the overall score is made of.</summary>
public enum ScorePart { Core, Cloud, Architecture, Years, Title, Leadership, Domain }

/// <summary>One requirement of the job laid against the resume, with what it is worth.</summary>
/// <param name="Weight">How much it matters: the role's importance for it times how strongly the job description leans on it.</param>
/// <param name="Credit">How much of its weight the resume earns, 0 to 1.</param>
/// <param name="Points">Points of the overall 100 it earns now.</param>
/// <param name="MaxPoints">Points of the overall 100 it could earn.</param>
/// <param name="Risk">How much missing it can hurt with an ATS; null when it is fully matched.</param>
/// <param name="Source">Where the case for adding it comes from; null when it is fully matched.</param>
public sealed record Requirement(
    TermMatch Match,
    SkillCategory Category,
    ScoreGroup Group,
    MatchLevel Level,
    Tier Tier,
    double Weight,
    double Credit,
    double Points,
    double MaxPoints,
    RiskLevel? Risk,
    RewriteSource? Source)
{
    public string Name => Match.Term.Skill;
    public JdTerm Term => Match.Term;
    public bool IsRequired => Match.Term.Weight == TermWeight.Required;
}

/// <param name="Score">How well the resume does on this part, 0 to 1.</param>
/// <param name="Share">How many of the 100 points this part is worth for this job.</param>
/// <param name="Earned">Points it earns.</param>
/// <param name="Detail">One sentence saying how it was worked out.</param>
public sealed record ScoreComponent(ScorePart Part, string Label, string MatchLabel, double Score, double Share, double Earned, string Detail)
{
    public double Lost => Share - Earned;
    public int Percent => (int)Math.Round(Score * 100);
}

/// <summary>One line of "why this score": points gained or lost, and what they came from.</summary>
public sealed record ScoreLine(bool Gained, int Points, string Label, string Detail);

public sealed record RiskItem(RiskLevel Level, string Name, string Text);

/// <param name="Bars">The bars shown beside the overall score: Skill Match, Cloud Match and so on. Years and title are shown as one "Experience Match".</param>
public sealed record ScoreReport(
    int Overall,
    IReadOnlyList<ScoreComponent> Components,
    IReadOnlyList<Requirement> Requirements,
    IReadOnlyList<ScoreLine> Breakdown,
    IReadOnlyList<RiskItem> Risks,
    IReadOnlyList<Requirement> TopMissing,
    IReadOnlyList<(string Label, int Percent)> Bars,
    RoleChoice Role,
    double? YearsAsked,
    ResumeYears? YearsShown,
    string JobTitle,
    TitleMatch Title,
    int LeadershipLinesShown)
{
    public bool HasRequirements => Requirements.Count > 0;
    public int Total => Requirements.Count;
    public int Matched => Requirements.Count(r => r.Level == MatchLevel.Strong);
    public int PartlyMatched => Requirements.Count(r => r.Level == MatchLevel.Partial);
    public int Claimed => Requirements.Count(r => r.Level == MatchLevel.Claimed);
    public int Missing => Requirements.Count(r => r.Level == MatchLevel.Missing);

    public Requirement? Find(string name) => Requirements.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Scores a resume against a job description the way an experienced reader of both would, and says why. Not a count of keywords: every requirement
/// earns points in proportion to its own weight (what the kind of job cares about, times how strongly the description leans on it), a closely related
/// phrase earns half, a skill that is only listed earns a little less than one that was used, and years of experience, job title and leadership shown
/// in the work count beside them. The same two texts always give the same score, and every point can be traced to a line.
/// </summary>
public static class AtsScorer
{
    // How many of the 100 points each kind of evidence is worth when every kind applies. The kinds the job does not ask about are left out and the rest scale up.
    private const double RequirementsShare = 70, YearsShare = 10, TitleShare = 10, LeadershipShownShareMax = 10;

    public static ScoreReport Score(JdAnalysis jd, string resumeText, string jobTitle, TailorState? state = null, SkillLexicon? lexicon = null,
        DateOnly? today = null, ConceptRelations? relations = null)
    {
        lexicon ??= SkillLexicon.Default;
        state ??= new TailorState();
        var day = today ?? DateOnly.FromDateTime(DateTime.Today);

        var role = RoleProfiles.ById(state.RoleId) is { } chosen
            ? new RoleChoice(chosen, false, "You chose it.")
            : RoleProfiles.Detect(jobTitle, jd);
        var match = ResumeMatcher.Match(jd, resumeText, state, lexicon, relations);
        var lines = ResumeLineReader.Read(resumeText);
        var drafts = match.Matches.Select(m => Draft(m, role.Profile, lexicon)).ToList();

        // A job description with nothing the app can recognise has nothing to score against (a title alone would say nothing true).
        if (drafts.Count == 0)
            return Empty(role, jobTitle, new TitleMatch(0, ""));

        var years = ExperienceReader.YearsOf(lines, day);
        var titleMatch = jobTitle.Trim().Length > 0 ? ExperienceReader.TitleSimilarity(jobTitle, ExperienceReader.TitlesOf(resumeText)) : new TitleMatch(0, "");
        var leadershipShown = ExperienceReader.LeadershipShownIn(lines);
        var leadershipAsked = drafts.Any(d => d.Group == ScoreGroup.Leadership) || jd.Leadership.Count > 0;

        // ---- the points: the requirements share one pool, and each of the other kinds of evidence has a slice of its own
        var yearsShare = jd.RequiredYears is not null ? YearsShare : 0;
        var titleShare = jobTitle.Trim().Length > 0 ? TitleShare : 0;
        // Leadership shown in the work is worth more the more the job asks for it: 3 points for each ask, up to 10. One passing line about mentoring is not a third of the job.
        var leadershipAsks = Math.Max(drafts.Count(d => d.Group == ScoreGroup.Leadership), jd.Leadership.Count);
        var indicatorShare = leadershipAsked ? Math.Min(LeadershipShownShareMax, 3.0 * leadershipAsks) : 0;
        var scale = 100 / (RequirementsShare + yearsShare + titleShare + indicatorShare);
        var pool = RequirementsShare * scale;
        var totalWeight = drafts.Sum(d => d.Weight);

        var requirements = drafts.Select(d =>
        {
            var max = pool * d.Weight / totalWeight;
            return d.ToRequirement(max * d.Credit, max);
        }).ToList();

        var components = new List<ScoreComponent>();
        foreach (var group in new[] { ScoreGroup.Core, ScoreGroup.Cloud, ScoreGroup.Architecture, ScoreGroup.Leadership, ScoreGroup.Domain })
        {
            var inGroup = requirements.Where(r => r.Group == group).ToList();
            var withIndicator = group == ScoreGroup.Leadership && leadershipAsked;
            if (inGroup.Count == 0 && !withIndicator)
                continue;

            var share = inGroup.Sum(r => r.MaxPoints);
            var earned = inGroup.Sum(r => r.Points);
            var detail = $"{inGroup.Count(r => r.Level == MatchLevel.Strong)} of {inGroup.Count} requirements matched" +
                         (inGroup.Any(r => r.Level == MatchLevel.Partial) ? $", {inGroup.Count(r => r.Level == MatchLevel.Partial)} partly (counted as half)" : "") + ".";
            if (withIndicator)
            {
                var slice = indicatorShare * scale;
                share += slice;
                earned += slice * Math.Min(1.0, leadershipShown.Count / 3.0);
                detail = $"{leadershipShown.Count} line{(leadershipShown.Count == 1 ? "" : "s")} in your work history show leading or mentoring" +
                         (inGroup.Count > 0 ? $"; {inGroup.Count(r => r.Level == MatchLevel.Strong)} of {inGroup.Count} leadership requirements matched." : ".");
            }

            components.Add(new ScoreComponent(PartOf(group), Label(PartOf(group)), MatchLabel(PartOf(group)), share == 0 ? 0 : earned / share, share, earned, detail));
        }

        if (jd.RequiredYears is { } asked)
        {
            var share = yearsShare * scale;
            var score = years is null ? 0 : Math.Min(1.0, years.Years / asked);
            components.Add(new ScoreComponent(ScorePart.Years, Label(ScorePart.Years), MatchLabel(ScorePart.Years), score, share, share * score,
                years is null
                    ? $"The job asks for {asked:0.#}+ years; no dates or years were found in your resume."
                    : $"The job asks for {asked:0.#}+ years; your resume shows {years.Years:0.#} years{(years.FromDates ? " from the dates of your jobs" : " (stated, with no dates to check)")}."));
        }

        if (titleShare > 0)
        {
            var share = titleShare * scale;
            components.Add(new ScoreComponent(ScorePart.Title, Label(ScorePart.Title), MatchLabel(ScorePart.Title), titleMatch.Score, share, share * titleMatch.Score,
                titleMatch.BestResumeTitle.Length > 0
                    ? $"The job is “{jobTitle.Trim()}”; the closest title on your resume is “{titleMatch.BestResumeTitle}”."
                    : $"The job is “{jobTitle.Trim()}”; no job title was found on your resume."));
        }

        var overall = (int)Math.Round(components.Sum(c => c.Earned));
        var breakdown = Explain(components, requirements, overall, years, jd.RequiredYears, titleMatch, jobTitle, leadershipShown.Count);
        var risks = requirements.Where(r => r.Risk is not null)
            .OrderBy(r => r.Risk).ThenByDescending(r => r.Weight).ThenBy(r => r.Name)
            .Select(r => new RiskItem(r.Risk!.Value, r.Name, RiskText(r))).ToList();
        var topMissing = requirements.Where(r => r.Level is MatchLevel.Missing or MatchLevel.Claimed)
            .OrderByDescending(r => r.Weight).ThenBy(r => r.Name).Take(8).ToList();

        // The bars, in a fixed order. Years and title are read together as one "Experience Match".
        var bars = new List<(string Label, int Percent)>();
        void Bar(ScorePart part)
        {
            if (components.FirstOrDefault(c => c.Part == part) is { } c)
                bars.Add((c.MatchLabel, c.Percent));
        }

        Bar(ScorePart.Core);
        Bar(ScorePart.Cloud);
        Bar(ScorePart.Architecture);
        var experience = components.Where(c => c.Part is ScorePart.Years or ScorePart.Title).ToList();
        if (experience.Count > 0)
            bars.Add(("Experience Match", (int)Math.Round(100 * experience.Sum(c => c.Earned) / experience.Sum(c => c.Share))));
        Bar(ScorePart.Leadership);
        Bar(ScorePart.Domain);

        return new ScoreReport(overall, components, requirements, breakdown, risks, topMissing, bars, role, jd.RequiredYears, years, jobTitle.Trim(), titleMatch, leadershipShown.Count);
    }
    // ---------------------------------------------------------------- one requirement

    private sealed record Drafted(TermMatch Match, SkillCategory Category, ScoreGroup Group, MatchLevel Level, Tier Tier, double Weight, double Credit,
        RiskLevel? Risk, RewriteSource? Source)
    {
        public Requirement ToRequirement(double points, double max) =>
            new(Match, Category, Group, Level, Tier, Weight, Credit, points, max, Risk, Source);
    }

    private static Drafted Draft(TermMatch m, RoleProfile role, SkillLexicon lexicon)
    {
        var category = lexicon.CategoryOf(m.Term.Skill);
        var importance = (int)role.Of(m.Term.Skill, category);
        var asked = m.Term.Weight switch { TermWeight.Required => 1.0, TermWeight.Duty => 0.8, _ => 0.4 };
        var emphasis = 1 + 0.1 * Math.Min(Math.Max(m.Term.Count - 1, 0), 3); // said again and again: a little more weight, never much
        var weight = importance * asked * emphasis;
        var tier = weight >= 3.5 ? Tier.Critical : weight >= 1.5 ? Tier.Important : Tier.Optional;

        var level = m.Status switch
        {
            MatchStatus.InResume => MatchLevel.Strong,
            MatchStatus.Partial => MatchLevel.Partial,
            MatchStatus.Attested => MatchLevel.Claimed,
            _ => MatchLevel.Missing,
        };

        // A skill that was used in the work history earns all of it; one only listed in a skills section earns a little less; related wording earns half.
        var credit = level switch
        {
            MatchLevel.Strong => (m.UsedInExperience ? 1.0 : 0.8) * (m.ImpliedBy is null ? 1.0 : 0.9),
            MatchLevel.Partial => m.UsedInExperience ? 0.5 : 0.4,
            MatchLevel.Claimed when m.EvidenceLines.Count > 0 => m.UsedInExperience ? 0.5 : 0.4, // the wording is on the resume; the person's word is not
            _ => 0.0,
        };

        RiskLevel? risk = null;
        RewriteSource? source = null;
        if (level != MatchLevel.Strong)
        {
            var required = m.Term.Weight == TermWeight.Required;
            risk = tier == Tier.Critical && m.Term.Weight != TermWeight.Preferred || tier == Tier.Important && required ? RiskLevel.High
                : tier == Tier.Important || tier == Tier.Optional && required ? RiskLevel.Medium
                : RiskLevel.Low;
            if (level == MatchLevel.Partial)
                risk = risk switch { RiskLevel.High => RiskLevel.Medium, _ => RiskLevel.Low }; // part of the way there
            source = SourceOf(m);
        }

        return new Drafted(m, category, SkillCategories.GroupOf(category), level, tier, weight, credit, risk, source);
    }

    /// <summary>
    /// Where the case for putting a requirement into the resume comes from. The person said they used it (USER CONFIRMED); or related wording or work is
    /// already in the resume (SYSTEM INFERRED); or only the job description asks for it (JOB DESCRIPTION ONLY). Only the first is the person's word.
    /// </summary>
    public static RewriteSource SourceOf(TermMatch m) =>
        m.Status == MatchStatus.Attested ? RewriteSource.UserConfirmed
        : m.Status == MatchStatus.Partial || m.RelatedLines.Count > 0 ? RewriteSource.SystemInferred
        : RewriteSource.JobDescriptionOnly;

    private static string RiskText(Requirement r) => r.Level switch
    {
        MatchLevel.Partial => $"Only partly matched: {r.Name}" + (r.IsRequired ? " (the job requires it)" : ""),
        MatchLevel.Claimed => $"{r.Name} is not in your resume, so an ATS cannot see it" + (r.IsRequired ? " (the job requires it)" : ""),
        _ => (r.IsRequired ? "Missing required " : "Missing ") + r.Name,
    };

    // ---------------------------------------------------------------- parts

    private static ScorePart PartOf(ScoreGroup group) => group switch
    {
        ScoreGroup.Cloud => ScorePart.Cloud,
        ScoreGroup.Architecture => ScorePart.Architecture,
        ScoreGroup.Leadership => ScorePart.Leadership,
        ScoreGroup.Domain => ScorePart.Domain,
        _ => ScorePart.Core,
    };



    private static string Label(ScorePart part) => part switch
    {
        ScorePart.Core => "core skills",
        ScorePart.Cloud => "cloud and DevOps",
        ScorePart.Architecture => "architecture and design",
        ScorePart.Years => "years of experience",
        ScorePart.Title => "job title",
        ScorePart.Leadership => "leadership",
        _ => "domain knowledge",
    };

    private static string MatchLabel(ScorePart part) => part switch
    {
        ScorePart.Core => "Skill Match",
        ScorePart.Cloud => "Cloud Match",
        ScorePart.Architecture => "Architecture Match",
        ScorePart.Years => "Experience Match",
        ScorePart.Title => "Experience Match",
        ScorePart.Leadership => "Leadership Match",
        _ => "Domain Match",
    };

    // ---------------------------------------------------------------- why this score

    private static IReadOnlyList<ScoreLine> Explain(IReadOnlyList<ScoreComponent> components, IReadOnlyList<Requirement> requirements, int overall,
        ResumeYears? years, double? yearsAsked, TitleMatch title, string jobTitle, int leadershipShown)
    {
        // Whole numbers that add up exactly: the points gained add up to the score, the points lost to what is left of 100.
        var gained = Apportion(components.Select(c => c.Earned).ToList(), overall);
        var lost = Apportion(components.Select(c => c.Lost).ToList(), 100 - overall);

        var lines = new List<ScoreLine>();
        for (var i = 0; i < components.Count; i++)
        {
            var c = components[i];
            var reqs = requirements.Where(r => PartOf(r.Group) == c.Part).ToList();
            if (gained[i] > 0)
                lines.Add(new ScoreLine(true, gained[i], c.Label, GainedDetail(c, reqs, years, yearsAsked, title, leadershipShown)));
        }

        for (var i = 0; i < components.Count; i++)
        {
            var c = components[i];
            var reqs = requirements.Where(r => PartOf(r.Group) == c.Part).ToList();
            if (lost[i] > 0)
                lines.Add(new ScoreLine(false, lost[i], c.Label, LostDetail(c, reqs, years, yearsAsked, title, jobTitle)));
        }

        return lines;
    }

    private static string GainedDetail(ScoreComponent c, List<Requirement> reqs, ResumeYears? years, double? asked, TitleMatch title, int leadershipShown) => c.Part switch
    {
        ScorePart.Years => years is null ? c.Detail : $"{years.Years:0.#} years shown, {asked:0.#}+ asked",
        ScorePart.Title => $"“{title.BestResumeTitle}” fits the job title",
        ScorePart.Leadership => $"{leadershipShown} line{(leadershipShown == 1 ? "" : "s")} show leading or mentoring" + (reqs.Count > 0 ? $"; {reqs.Count(r => r.Level == MatchLevel.Strong)} of {reqs.Count} requirements matched" : ""),
        _ => $"{reqs.Count(r => r.Level == MatchLevel.Strong)} of {reqs.Count} requirements matched" + (reqs.Any(r => r.Level == MatchLevel.Partial) ? $", {reqs.Count(r => r.Level == MatchLevel.Partial)} partly" : ""),
    };

    private static string LostDetail(ScoreComponent c, List<Requirement> reqs, ResumeYears? years, double? asked, TitleMatch title, string jobTitle) => c.Part switch
    {
        ScorePart.Years => years is null ? $"no years or dates found; {asked:0.#}+ asked" : $"{years.Years:0.#} years shown against {asked:0.#}+ asked",
        ScorePart.Title => title.BestResumeTitle.Length > 0 ? $"no title on your resume is a closer fit to “{jobTitle.Trim()}”" : "no job title found on your resume",
        _ => reqs.Any(r => r.Level != MatchLevel.Strong) ? Names(reqs.Where(r => r.Level != MatchLevel.Strong).OrderByDescending(r => r.Weight).ToList()) : c.Detail,
    };

    private static string Names(IReadOnlyList<Requirement> reqs)
    {
        var shown = string.Join(", ", reqs.Take(4).Select(r => r.Name + (r.Level == MatchLevel.Partial ? " (partly)" : r.Level == MatchLevel.Claimed ? " (not written)" : "")));
        return reqs.Count > 4 ? $"{shown} and {reqs.Count - 4} more" : shown;
    }

    /// <summary>Rounds a list of amounts to whole numbers that add up to exactly <paramref name="total"/>, giving the leftover to the largest remainders.</summary>
    public static int[] Apportion(IReadOnlyList<double> amounts, int total)
    {
        var floors = amounts.Select(a => (int)Math.Floor(a)).ToArray();
        var left = total - floors.Sum();
        foreach (var i in Enumerable.Range(0, amounts.Count).OrderByDescending(i => amounts[i] - floors[i]).Take(Math.Max(0, left)))
            floors[i]++;
        return floors;
    }

    private static ScoreReport Empty(RoleChoice role, string jobTitle, TitleMatch title) =>
        new(0, [], [], [], [], [], [], role, null, null, jobTitle.Trim(), title, 0);
}