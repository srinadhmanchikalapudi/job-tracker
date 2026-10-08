using System.Text.RegularExpressions;

namespace JobTracker.Core;

public sealed record CleanedJd(string Text, int RemovedLines);

/// <summary>
/// Strips page clutter (menus before the title, buttons, "Similar jobs" footers, cookie banners) from a
/// pasted job page. Every line it keeps is left exactly as pasted, so blank lines, headings and bullets
/// survive. The caller should keep the original text too, since this is a heuristic.
/// </summary>
public static class JdCleaner
{
    private const int HeadScanLines = 40;
    private const int MinLinesBeforeFooter = 8;
    private const int MaxMarkerWords = 8;

    private static readonly HashSet<string> ButtonLines = new(StringComparer.OrdinalIgnoreCase)
    {
        "skip to main content", "skip to content", "easy apply", "apply", "apply now", "apply here",
        "apply on company website", "apply on employer site", "save", "saved", "share", "show more", "show less",
        "see more", "see less", "…more", "...more", "… more", "report this job", "report job", "sign in",
        "join now", "log in", "back to search", "back to jobs", "back to all jobs", "submit application",
        "promoted", "reposted", "actively recruiting", "actively hiring", "be an early applicant",
        "responses managed off linkedin", "copy link", "accept", "accept all", "reject all", "decline",
        "manage preferences", "no thanks", "dismiss",
    };

    private static readonly Regex NoisePattern = new(
        @"^(over\s+)?\d[\d,]*\+?\s+(applicants?|people clicked apply|views?)\b|^.{0,50}\b\d[\d,]*\+?\s+(applicants?|people clicked apply)\s*$|\bcookies?\b|^(sign in|log in) to\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FooterMarker = new(
        @"^(similar (jobs|roles)|people also (viewed|applied)|show more jobs|set alert|get notified|jobs you may be interested in|more jobs\b|related jobs|explore more|see more jobs|job alerts?|create (a )?job alert|about (linkedin|indeed|glassdoor)|privacy( policy| notice)?|terms( of (use|service))?|user agreement|accessibility|do not sell|©|copyright|all rights reserved)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LabelLine = new(@"^\w[\w ]{1,20}:\s*\S", RegexOptions.Compiled);

    public static CleanedJd Clean(string? text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
        var originalCount = lines.Count(l => l.Trim().Length > 0);

        var start = FindStart(lines);
        var kept = new List<string>();
        var keptNonBlank = 0;

        for (var i = start; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length > 0)
            {
                if (keptNonBlank >= MinLinesBeforeFooter && IsFooterMarker(trimmed))
                    break;
                if (IsNoise(trimmed))
                    continue;
                keptNonBlank++;
            }

            // Collapse runs of blank lines into one; never start with a blank.
            if (trimmed.Length == 0 && (kept.Count == 0 || kept[^1].Length == 0))
                continue;

            kept.Add(line);
        }

        while (kept.Count > 0 && kept[^1].Length == 0)
            kept.RemoveAt(kept.Count - 1);

        return new CleanedJd(string.Join('\n', kept), Math.Max(0, originalCount - keptNonBlank));
    }

    /// <summary>Index of the job-title line when everything above it looks like menu text, otherwise 0.</summary>
    private static int FindStart(List<string> lines)
    {
        for (var i = 0; i < Math.Min(lines.Count, HeadScanLines); i++)
        {
            if (!JdExtractor.IsTitleLine(lines[i].Trim()))
                continue;

            var dropped = lines.Take(i).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var looksLikeMenu = dropped.All(l =>
                l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5 &&
                !l.EndsWith('.') &&
                !LabelLine.IsMatch(l));
            return looksLikeMenu ? i : 0;
        }

        return 0;
    }

    private static bool IsNoise(string line) => ButtonLines.Contains(line) || NoisePattern.IsMatch(line);

    private static bool IsFooterMarker(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxMarkerWords && FooterMarker.IsMatch(line);
}
