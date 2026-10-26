namespace JobTracker.Core.Tailoring;

/// <summary>What kind of requirement a catalogue entry is. Set by the [Category] lines in skills.txt.</summary>
public enum SkillCategory
{
    Other,
    Language,
    Framework,
    Web,
    Architecture,
    Data,
    AI,
    Cloud,
    DevOps,
    Testing,
    Practice,
    Leadership,
    Domain,
    Tool,
}

/// <summary>The parts the score is made of. Each is measured on its own, then they are blended (see <see cref="AtsScorer"/>).</summary>
public enum ScoreGroup
{
    /// <summary>Languages, frameworks, data, testing, practices and tools: the technical core of the job.</summary>
    Core,

    /// <summary>Cloud platforms and the DevOps tooling around them.</summary>
    Cloud,

    /// <summary>Architecture and design: microservices, event-driven, REST, OAuth, messaging.</summary>
    Architecture,

    /// <summary>Mentoring, technical leadership, stakeholders, cross-functional work.</summary>
    Leadership,

    /// <summary>The industry the job is in: healthcare, insurance, fintech, compliance.</summary>
    Domain,
}

public static class SkillCategories
{
    public static ScoreGroup GroupOf(SkillCategory category) => category switch
    {
        SkillCategory.Cloud or SkillCategory.DevOps => ScoreGroup.Cloud,
        SkillCategory.Architecture => ScoreGroup.Architecture,
        SkillCategory.Leadership => ScoreGroup.Leadership,
        SkillCategory.Domain => ScoreGroup.Domain,
        _ => ScoreGroup.Core,
    };

    public static string Label(ScoreGroup group) => group switch
    {
        ScoreGroup.Core => "Core skills",
        ScoreGroup.Cloud => "Cloud and DevOps",
        ScoreGroup.Architecture => "Architecture",
        ScoreGroup.Leadership => "Leadership",
        _ => "Domain",
    };

    /// <summary>The name on the score bars ("Skill Match", "Cloud Match").</summary>
    public static string MatchLabel(ScoreGroup group) => group switch
    {
        ScoreGroup.Core => "Skill Match",
        ScoreGroup.Cloud => "Cloud Match",
        ScoreGroup.Architecture => "Architecture Match",
        ScoreGroup.Leadership => "Leadership Match",
        _ => "Domain Match",
    };

    /// <summary>The finer headings used on the coverage map.</summary>
    public static string MapLabel(SkillCategory category) => category switch
    {
        SkillCategory.Language or SkillCategory.Framework or SkillCategory.Web => "Languages and frameworks",
        SkillCategory.Data or SkillCategory.AI => "Data and AI",
        SkillCategory.Cloud or SkillCategory.DevOps => "Cloud and DevOps",
        SkillCategory.Architecture => "Architecture",
        SkillCategory.Testing or SkillCategory.Practice => "Practices and testing",
        SkillCategory.Leadership => "Leadership",
        SkillCategory.Domain => "Domain",
        _ => "Tools",
    };

    public static int MapOrder(string label) => label switch
    {
        "Languages and frameworks" => 0,
        "Data and AI" => 1,
        "Cloud and DevOps" => 2,
        "Architecture" => 3,
        "Practices and testing" => 4,
        "Leadership" => 5,
        "Domain" => 6,
        _ => 7,
    };
}