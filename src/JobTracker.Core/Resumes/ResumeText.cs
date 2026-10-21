using System.Text;
using System.Text.RegularExpressions;

namespace JobTracker.Core.Resumes;

/// <summary>Cleaning shared by every resume reader, so a PDF, a Word file and a text file come out in the same shape.</summary>
internal static partial class ResumeText
{
    // Bullet marks that are really drawing characters. "-", "–" and "*" are handled separately because they are also ordinary text.
    private const string BulletGlyphs = "•●○◦▪■□▫◆◇►▶➤➢✓✔∙‣⁃";

    [GeneratedRegex(@"[ \t   ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^\s*(page\s+)?\d+\s*(of|/)\s*\d+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PageNumber();

    /// <summary>True for a character that is only a list mark: a drawn bullet, or a symbol-font bullet (private use area, as Word writes them).</summary>
    public static bool IsBulletChar(char c) => BulletGlyphs.Contains(c) || c is >= '' and <= '';

    /// <summary>True for a whole word that is just a list mark ("•", "-", "–", "*", or a symbol-font bullet).</summary>
    public static bool IsBulletToken(string word) =>
        word.Length > 0 && (word.All(IsBulletChar) || word is "-" or "–" or "—" or "*");

    /// <summary>"• text" and "● text" become "- text". Lines that are already "- text" or "* text" are left alone.</summary>
    public static string NormaliseBulletGlyph(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length > 1 && IsBulletChar(trimmed[0]))
        {
            var indent = line[..(line.Length - trimmed.Length)];
            var rest = trimmed[1..].TrimStart();
            if (rest.Length > 0)
                return indent + "- " + rest;
        }

        return line;
    }

    /// <summary>
    /// Final tidy of the lines a reader produced: odd spaces and control characters removed, runs of spaces collapsed, page-number lines dropped,
    /// at most one blank line in a row, none at the start or the end.
    /// </summary>
    public static string Finish(IEnumerable<string> lines)
    {
        var result = new List<string>();
        foreach (var raw in lines)
        {
            var clean = new StringBuilder(raw.Length);
            foreach (var ch in raw)
            {
                if (ch == '\0' || (char.IsControl(ch) && ch is not '\t'))
                    continue;
                clean.Append(ch);
            }

            // Keep the leading indentation of nested bullets, but collapse every other run of spaces.
            var text = clean.ToString().TrimEnd();
            var indent = text.Length - text.TrimStart().Length;
            var body = Spaces().Replace(text.TrimStart(), " ");
            var line = (indent >= 2 && body.StartsWith("- ", StringComparison.Ordinal) ? new string(' ', Math.Min(indent, 8)) : "") + body;

            if (PageNumber().IsMatch(line))
                continue;
            if (line.Length == 0 && (result.Count == 0 || result[^1].Length == 0))
                continue;
            result.Add(line);
        }

        while (result.Count > 0 && result[^1].Length == 0)
            result.RemoveAt(result.Count - 1);
        return string.Join('\n', result);
    }
}
