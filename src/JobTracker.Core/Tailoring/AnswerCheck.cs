namespace JobTracker.Core.Tailoring;

/// <summary>Whether what the person typed about their work is enough to write a bullet from, and what it amounts to.</summary>
public static class AnswerCheck
{
    public const int MinimumWords = 3;

    public static int Words(string? text) => (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>True for something typed that is too short to say what was done and is not a skill either ("banana"). Blank is a different case.</summary>
    public static bool IsTooThin(string? answers, SkillLexicon? lexicon = null) =>
        Words(answers) is > 0 and < MinimumWords && SkillsOnly(answers, out _, lexicon) is false;

    /// <summary>True for one or two words, skills included. Where there is no job and no existing bullet to tie a skill to, a lone skill says nothing to write from.</summary>
    public static bool IsTooShort(string? answers) => Words(answers) is > 0 and < MinimumWords;

    /// <summary>
    /// True when the text is nothing but skills ("Kubernetes", "Kubernetes, Terraform & Helm"): the person has said what they used, not what they did.
    /// <paramref name="skills"/> are those skills, by the names the app uses.
    /// </summary>
    public static bool SkillsOnly(string? text, out IReadOnlyList<string> skills, SkillLexicon? lexicon = null)
    {
        lexicon ??= SkillLexicon.Default;
        skills = [];
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var hits = lexicon.Find(text, dropNested: true);
        if (hits.Count == 0)
            return false;

        // Everything that is left after the skills are taken out must be only separators.
        var rest = text;
        foreach (var hit in hits.OrderByDescending(h => h.Index))
            rest = rest.Remove(hit.Index, hit.Length);
        var leftover = rest.Split([' ', ',', ';', '/', '&', '+', '.', '|', '\n', '\r', '\t', '-', '•'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.Equals("and", StringComparison.OrdinalIgnoreCase) && !w.Equals("or", StringComparison.OrdinalIgnoreCase)).ToList();
        if (leftover.Count > 0)
            return false;

        skills = hits.Select(h => h.Skill).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return true;
    }

    /// <summary>What to tell the person who typed a word or two that is not a skill the app knows.</summary>
    public static string ThinMessage(string answers) =>
        $"“{answers.Trim()}” on its own is not enough to write a bullet from, and a bullet should not be made up. " +
        "Type a skill from the job description (like Kubernetes), or say what you did in a sentence, for example “ran the Kubernetes deployments for the claims API, about 40 services”.";

    /// <summary>The same, for a place with no job to draw on (the resume builder): say what you did, in a sentence.</summary>
    public static string ShortMessage(string answers) =>
        $"“{answers.Trim()}” on its own is not enough to write a bullet from, and a bullet should not be made up. " +
        "Say what you did in a sentence, for example “ran the nightly claim batches for 8 regional plans”.";
}
