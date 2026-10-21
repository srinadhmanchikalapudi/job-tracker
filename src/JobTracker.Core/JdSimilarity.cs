using System.Text;

namespace JobTracker.Core;

/// <summary>A saved application whose job description is close to one being saved now.</summary>
/// <param name="Score">0 to 1: the share of the wording the two descriptions have in common, in the same order.</param>
public sealed record SimilarJd(JobApplication Application, double Score, string JdText)
{
    public int Percent => (int)Math.Round(Score * 100);
}

/// <summary>One line of a job description, and whether the other description has no matching line.</summary>
public sealed record JdLine(string Text, bool OnlyHere);

/// <summary>
/// Tells whether a pasted job description is a repeat of one already saved for the same company (a re-posting, or the same role listed twice), so
/// the person can be asked before a second copy is filed. It counts words, so it is explained the same way every time and nothing is guessed.
/// </summary>
public static class JdSimilarity
{
    /// <summary>At or above this, the person is asked. "Almost the same" is about this much of the wording.</summary>
    public const double Threshold = 0.70;

    /// <summary>Shorter than this, there is too little text to say two descriptions are the same.</summary>
    public const int MinimumWords = 15;

    /// <summary>
    /// 2 x (words in common, in order) / (words in both). 1 is identical, 0 shares nothing. Capitalisation, punctuation and spacing do not count.
    /// Returns 0 when either side is too short to judge.
    /// </summary>
    public static double Score(string a, string b)
    {
        var x = Words(a);
        var y = Words(b);
        if (x.Length < MinimumWords || y.Length < MinimumWords)
            return 0;

        // Longest common subsequence of words, two rows at a time so a long description costs no more memory than a short one.
        var previous = new int[y.Length + 1];
        var current = new int[y.Length + 1];
        for (var i = 1; i <= x.Length; i++)
        {
            for (var j = 1; j <= y.Length; j++)
                current[j] = x[i - 1] == y[j - 1] ? previous[j - 1] + 1 : Math.Max(previous[j], current[j - 1]);
            (previous, current) = (current, previous);
        }

        return 2.0 * previous[y.Length] / (x.Length + y.Length);
    }

    /// <summary>The saved descriptions of the same company that are at or above the threshold, closest first.</summary>
    public static IReadOnlyList<SimilarJd> FindSimilar(IEnumerable<JobApplication> saved, Func<JobApplication, string> readJd, string company, string jd, double threshold = Threshold)
    {
        var key = CompanyKey(company);
        if (key.Length == 0)
            return [];

        return saved
            .Where(app => CompanyKey(app.Meta.Company) == key)
            .Select(app => { var text = readJd(app); return new SimilarJd(app, Score(jd, text), text); })
            .Where(s => s.Score >= threshold)
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Application.Meta.AppliedDate)
            .ToList();
    }

    /// <summary>"Acme Corp." and "ACME corporation" are the same employer; "Acme Labs" is not.</summary>
    public static string CompanyKey(string company)
    {
        var words = Normalize(company).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && Suffixes.Contains(words[^1]))
            words.RemoveAt(words.Count - 1);
        return string.Concat(words);
    }

    private static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal)
        { "inc", "llc", "ltd", "limited", "corp", "corporation", "co", "company", "plc", "lp", "llp", "gmbh" };

    /// <summary>The lines of <paramref name="mine"/>, each marked when <paramref name="other"/> has no line that says the same. Blank lines are kept for layout and never marked.</summary>
    public static IReadOnlyList<JdLine> MarkLines(string mine, string other)
    {
        var lines = mine.Replace("\r\n", "\n").Split('\n');
        var theirs = other.Replace("\r\n", "\n").Split('\n').Select(Normalize).ToArray();
        var ours = lines.Select(Normalize).ToArray();

        // Longest common subsequence of lines: lines that are not part of it are the ones the other side lacks.
        var lcs = new int[ours.Length + 1, theirs.Length + 1];
        for (var i = ours.Length - 1; i >= 0; i--)
            for (var j = theirs.Length - 1; j >= 0; j--)
                lcs[i, j] = ours[i].Length > 0 && ours[i] == theirs[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var matched = new bool[ours.Length];
        int x = 0, y = 0;
        while (x < ours.Length && y < theirs.Length)
        {
            if (ours[x].Length > 0 && ours[x] == theirs[y])
            {
                matched[x++] = true;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
                x++;
            else
                y++;
        }

        return lines.Select((text, i) => new JdLine(text, ours[i].Length > 0 && !matched[i])).ToList();
    }

    private static string[] Words(string text) => Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Lower case, with every run of anything that is not a letter or digit turned into one space.</summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = true;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space)
            {
                sb.Append(' ');
                space = true;
            }
        }

        return sb.ToString().Trim();
    }
}