using System.Text.RegularExpressions;

namespace JobTracker.Core.Tailoring;

/// <summary>What a reworded bullet is allowed to say: only what the bullet, the person's own notes, and their resume already say.</summary>
/// <param name="Original">The bullet as it is now.</param>
/// <param name="Facts">The person's own words for this rewrite (notes on skills they claimed, extra details they typed).</param>
/// <param name="ResumeText">The whole resume, which is where names and tools may already appear.</param>
/// <param name="ClaimedSkills">Skills the person said they have used (their notes are in <paramref name="Facts"/>).</param>
/// <param name="TargetCompany">The company being applied to: a bullet about past work never names it.</param>
/// <param name="Existing">For a brand-new bullet: the bullets the job already has, so a copy of one is rejected.</param>
public sealed record BulletContext(string Original, IReadOnlyList<string> Facts, string ResumeText, IReadOnlyList<string> ClaimedSkills, string TargetCompany = "",
    IReadOnlyList<string>? Existing = null);

/// <summary>
/// Checks a model's proposed bullet before the person ever sees it. The model proposes; this decides. A proposal is rejected when it adds a number,
/// a tool or skill the person has not said they have, a name nobody wrote, or a bigger role than the original claims. Every rejection says why, in plain
/// words, so the person can see that the check was real.
/// </summary>
public static class BulletVerifier
{
    private static readonly Regex Number = new(@"(?<![\w.])\$?\d[\d,]*(?:\.\d+)?\s?(?:%|[kKmMbB]\b|[xX]\b|\+)?", RegexOptions.Compiled);
    private static readonly Regex Word = new(@"[\p{L}][\p{L}'’\-]*", RegexOptions.Compiled);
    private static readonly Regex Capitalised = new(@"(?<![\p{L}\p{N}])[A-Z][\p{L}\p{N}#+.]*", RegexOptions.Compiled);

    private static readonly string[] BiggerRoleVerbs =
    [
        "led", "lead", "leading", "managed", "manage", "managing", "owned", "own", "owning", "architected", "founded", "directed", "spearheaded",
        "headed", "oversaw", "supervised", "mentored", "hired", "launched", "pioneered",
    ];

    private static readonly string[] FirstPerson = ["i ", "i'm ", "i’ve ", "my ", "we ", "our "];

    public static IReadOnlyList<string> Verify(string proposal, BulletContext context, SkillLexicon? lexicon = null)
    {
        lexicon ??= SkillLexicon.Default;
        var problems = new List<string>();
        var text = proposal.Trim();

        if (text.Length == 0)
            return ["It is empty."];
        if (text.Contains('\n'))
            problems.Add("It is more than one line.");

        var sameAsBefore = Normalize(text) == Normalize(BulletText.Clean(context.Original));
        if (sameAsBefore)
            problems.Add("It is the same as your bullet.");

        if (context.Existing is { Count: > 0 } && context.Existing.Any(e => Overlap(e, text) >= 0.8))
            problems.Add("It repeats a bullet that is already there.");

        var originalWords = WordCount(context.Original);
        var words = WordCount(text);
        if (words > Math.Max(45, originalWords * 2 + 10))
            problems.Add($"It is much longer than your bullet ({words} words against {originalWords}).");

        var lower = " " + text.ToLowerInvariant();
        if (FirstPerson.Any(p => lower.StartsWith(" " + p, StringComparison.Ordinal) || lower.Contains(". " + p, StringComparison.Ordinal)))
            problems.Add("It is written in the first person; resume bullets are not.");

        var sources = string.Join("\n", [context.Original, .. context.Facts]);

        // Numbers: a figure only appears if the person wrote it for this bullet.
        var allowedNumbers = Numbers(sources);
        foreach (var number in Number.Matches(text).Select(m => m.Value.Trim()).Where(v => !allowedNumbers.Contains(NormalizeNumber(v))).Distinct())
            problems.Add($"It adds the number {number}, which is not in your bullet or your notes.");

        // Skills and tools: only ones the resume already says, or the person said they have used.
        var allowedSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in lexicon.Find(context.ResumeText + "\n" + sources))
            allowedSkills.Add(hit.Skill);
        foreach (var skill in context.ClaimedSkills)
            allowedSkills.Add(skill);
        foreach (var skill in lexicon.Find(text).Select(h => h.Skill).Distinct(StringComparer.OrdinalIgnoreCase).Where(s => !allowedSkills.Contains(s)))
            problems.Add($"It mentions {skill}, which is not in your resume and you have not said you have used it.");

        // Names: a capitalised word that is not a known skill has to be written somewhere the person wrote it.
        var written = (context.ResumeText + "\n" + sources).ToLowerInvariant();
        // The words of every skill written in the proposal (CI/CD gives CI and CD) are fine: the skill check above already judged them.
        var known = lexicon.Find(text)
            .SelectMany(h => text.Substring(h.Index, h.Length).Split(['/', ' ', '-'], StringSplitOptions.RemoveEmptyEntries))
            .Select(w => w.ToLowerInvariant()).ToHashSet();
        foreach (Match m in Capitalised.Matches(text))
        {
            var token = m.Value.TrimEnd('.', '-', '/');
            if (token.Length < 2 || StartsSentence(text, m.Index) || known.Contains(token.ToLowerInvariant()))
                continue;
            if (!Appears(written, token))
                problems.Add($"It uses the name {token}, which is not in your resume or your notes.");
        }

        if (context.TargetCompany.Trim().Length > 1 && lower.Contains(context.TargetCompany.Trim().ToLowerInvariant(), StringComparison.Ordinal) &&
            !sources.Contains(context.TargetCompany.Trim(), StringComparison.OrdinalIgnoreCase))
            problems.Add($"It names {context.TargetCompany.Trim()}, the company you are applying to, as if you worked there.");

        // Role: no promotion from "helped" to "led".
        var sourceWords = Words(sources);
        foreach (var verb in Words(text).Where(w => BiggerRoleVerbs.Contains(w)).Distinct().Where(v => !sourceWords.Contains(v)))
            problems.Add($"It says you \"{verb}\", which is a bigger role than your bullet claims.");

        return problems.Distinct().ToList();
    }

    /// <summary>Share of words two bullets have in common (0 to 1), ignoring case and punctuation.</summary>
    private static double Overlap(string a, string b)
    {
        var x = Words(BulletText.Clean(a));
        var y = Words(BulletText.Clean(b));
        return x.Count == 0 || y.Count == 0 ? 0 : (double)x.Intersect(y).Count() / x.Union(y).Count();
    }

    private static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant().TrimEnd('.');

    private static int WordCount(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private static HashSet<string> Words(string s) =>
        Word.Matches(s).Select(m => m.Value.ToLowerInvariant().Trim('-')).ToHashSet();

    private static HashSet<string> Numbers(string s) =>
        Number.Matches(s).Select(m => NormalizeNumber(m.Value)).ToHashSet();

    // The figure itself, without its unit or separators, so "63%" and "63 percent" are the same figure but 63 and 36 are not.
    private static string NormalizeNumber(string value) =>
        new string(value.Where(c => char.IsDigit(c) || c == '.').ToArray()).TrimEnd('.');

    private static bool Appears(string haystackLower, string token) =>
        Regex.IsMatch(haystackLower, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(token.ToLowerInvariant())}(?![\p{{L}}\p{{N}}])");

    private static bool StartsSentence(string text, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(text[i]))
                continue;
            return text[i] is '.' or '!' or '?' or ':' or '•' or '-';
        }

        return true;
    }
}
