using System.Text.RegularExpressions;

namespace JobTracker.Core.Export;

/// <summary>
/// Decides whether two versions of a resume say the same thing, ignoring how they are laid out: blank lines, bullet marks, heading marks and runs
/// of spaces do not count. Used to check a Word document against the text it was made from.
/// </summary>
public static partial class ResumeTextCompare
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static string Normalize(string line) =>
        Spaces().Replace(BulletText.Clean(line.Trim().TrimStart('#', ' ')), " ").Trim();

    public static List<string> Lines(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(Normalize).Where(l => l.Length > 0).ToList();

    /// <summary>Empty when the two read the same; otherwise the first few differences, in plain words.</summary>
    public static IReadOnlyList<string> Differences(string expected, string actual)
    {
        var a = Lines(expected);
        var b = Lines(actual);
        var problems = new List<string>();
        for (var i = 0; i < Math.Max(a.Count, b.Count) && problems.Count < 5; i++)
        {
            var x = i < a.Count ? a[i] : null;
            var y = i < b.Count ? b[i] : null;
            if (x != y)
                problems.Add($"Line {i + 1}: expected “{Shorten(x)}” but the document has “{Shorten(y)}”.");
        }

        return problems;
    }

    private static string Shorten(string? s) => s is null ? "nothing" : s.Length > 70 ? s[..70] + "…" : s;
}
