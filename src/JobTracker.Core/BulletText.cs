using System.Text.RegularExpressions;

namespace JobTracker.Core;

public enum LineKind
{
    Blank,
    Heading,
    Bullet,
    Text,
}

/// <param name="Raw">The line as stored in the file (used for starring/copying).</param>
/// <param name="Display">What to show: heading text without "#", bullet text without its marker.</param>
public sealed record StructuredLine(int LineNumber, string Raw, string Display, LineKind Kind)
{
    /// <summary>Only paragraphs and bullets can be starred or copied.</summary>
    public bool IsActionable => Kind is LineKind.Bullet or LineKind.Text;
}

public static class BulletText
{
    private static readonly Regex BulletLine = new(@"^\s*[-*•·▪●○◦‣]\s+(?<t>\S.*)$", RegexOptions.Compiled);
    private static readonly Regex MarkdownHeading = new(@"^\s*#{1,6}\s+(?<t>\S.*?)\s*#*$", RegexOptions.Compiled);
    private static readonly Regex BoldHeading = new(@"^\s*\*\*(?<t>[^*]+?)\*\*:?\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Classifies every line (blank, heading, bullet, text) so the viewer can show the document the way
    /// it reads in the file. Leading blanks are dropped and runs of blanks collapse to one.
    /// </summary>
    public static IReadOnlyList<StructuredLine> Structure(string text)
    {
        var raw = new List<(int Number, string Text)>();
        var number = 0;
        foreach (var line in text.Split('\n'))
        {
            number++;
            var trimmed = line.TrimEnd('\r', ' ', '\t');
            var isBlank = trimmed.Trim().Length == 0;
            if (isBlank && (raw.Count == 0 || raw[^1].Text.Length == 0))
                continue;
            raw.Add((number, isBlank ? "" : trimmed));
        }

        while (raw.Count > 0 && raw[^1].Text.Length == 0)
            raw.RemoveAt(raw.Count - 1);

        var result = new List<StructuredLine>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var (n, line) = raw[i];
            if (line.Length == 0)
            {
                result.Add(new StructuredLine(n, "", "", LineKind.Blank));
                continue;
            }

            var bullet = BulletLine.Match(line);
            if (bullet.Success)
            {
                result.Add(new StructuredLine(n, line, bullet.Groups["t"].Value.Trim(), LineKind.Bullet));
                continue;
            }

            var heading = MarkdownHeading.Match(line);
            if (!heading.Success)
                heading = BoldHeading.Match(line);
            if (heading.Success)
            {
                result.Add(new StructuredLine(n, line, heading.Groups["t"].Value.Trim(), LineKind.Heading));
                continue;
            }

            var prevBlank = i == 0 || raw[i - 1].Text.Length == 0;
            // Blank runs are collapsed, so content follows either directly or after a single blank line.
            var nextHasText = (i + 1 < raw.Count && raw[i + 1].Text.Length > 0) ||
                              (i + 2 < raw.Count && raw[i + 2].Text.Length > 0);
            var display = line.Trim();
            result.Add(new StructuredLine(n, line, display,
                LooksLikeHeading(display, prevBlank, nextHasText) ? LineKind.Heading : LineKind.Text));
        }

        return result;
    }

    private static bool LooksLikeHeading(string line, bool prevBlank, bool nextHasText)
    {
        if (line.EndsWith(':') && line.Length <= 80)
            return true;
        if (!prevBlank || !nextHasText || line.Length > 60)
            return false;
        if (line.EndsWith('.') || line.EndsWith(',') || line.EndsWith(';'))
            return false;
        return line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 8;
    }

    private static readonly char[] Markers = ['-', '*', '•', '·', '▪', '●', '○', '–', '—', '>'];

    /// <summary>Strips a leading list marker ("- ", "* ", "• ") and surrounding whitespace.</summary>
    public static string Clean(string line)
    {
        var text = line.Trim();
        while (text.Length > 0 && Array.IndexOf(Markers, text[0]) >= 0)
            text = text[1..].TrimStart();
        return text;
    }

    /// <summary>Non-empty lines with their 1-based line numbers in the original text.</summary>
    public static IEnumerable<(int LineNumber, string Text)> Lines(string text)
    {
        var number = 0;
        foreach (var line in text.Split('\n'))
        {
            number++;
            var trimmed = line.TrimEnd('\r');
            if (Clean(trimmed).Length > 0)
                yield return (number, trimmed);
        }
    }

    /// <summary>Case- and whitespace-insensitive key used to tell whether two lines are the same bullet.</summary>
    public static string Key(string line) =>
        string.Join(' ', Clean(line).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
