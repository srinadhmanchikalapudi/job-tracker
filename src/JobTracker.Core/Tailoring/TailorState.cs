namespace JobTracker.Core.Tailoring;

/// <summary>A skill the person says they have used although the resume does not mention it, with their own words about it.</summary>
public sealed class AttestedSkill
{
    public string Skill { get; set; } = "";

    /// <summary>One or two sentences in the person's own words ("Used it at Contoso to run nightly claim batches"). Rewrites may draw on this and nothing else.</summary>
    public string Note { get; set; } = "";
}

/// <summary>What the person decided for one application, kept in tailor.json next to the job description and resume.</summary>
public sealed class TailorState
{
    public const string FileName = "tailor.json";

    public List<AttestedSkill> Attested { get; set; } = [];

    /// <summary>The kind of job to score for (a <see cref="RoleProfile.Id"/>) when the person overrode the one detected from the job title; null means detect it.</summary>
    public string? RoleId { get; set; }

    /// <summary>Skills the person marked "not relevant", so they stop counting as missing.</summary>
    public List<string> Dismissed { get; set; } = [];

    public AttestedSkill? FindAttested(string skill) =>
        Attested.FirstOrDefault(a => string.Equals(a.Skill, skill, StringComparison.OrdinalIgnoreCase));

    public bool IsDismissed(string skill) => Dismissed.Contains(skill, StringComparer.OrdinalIgnoreCase);

    public void Attest(string skill, string note)
    {
        Unattest(skill);
        Dismissed.RemoveAll(d => string.Equals(d, skill, StringComparison.OrdinalIgnoreCase));
        Attested.Add(new AttestedSkill { Skill = skill, Note = note.Trim() });
    }

    public void Unattest(string skill) =>
        Attested.RemoveAll(a => string.Equals(a.Skill, skill, StringComparison.OrdinalIgnoreCase));

    public void Dismiss(string skill)
    {
        Unattest(skill);
        if (!IsDismissed(skill))
            Dismissed.Add(skill);
    }

    public void Restore(string skill) =>
        Dismissed.RemoveAll(d => string.Equals(d, skill, StringComparison.OrdinalIgnoreCase));

    public static TailorState Load(string applicationFolder) =>
        AtomicFile.ReadJson<TailorState>(Path.Combine(applicationFolder, FileName)) ?? new TailorState();

    public void Save(string applicationFolder) =>
        AtomicFile.WriteJson(Path.Combine(applicationFolder, FileName), this);
}
