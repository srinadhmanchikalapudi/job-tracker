using System.Text;

namespace JobTracker.Core;

public static class FolderNamer
{
    private const int MaxSegmentLength = 80;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Makes free text safe to use as one Windows path segment.</summary>
    public static string Sanitize(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        var lastWasSpace = false;
        foreach (var ch in name ?? "")
        {
            if (Array.IndexOf(invalid, ch) >= 0)
                continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && sb.Length > 0)
                    sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }

        var result = sb.ToString();
        if (result.Length > MaxSegmentLength)
            result = result[..MaxSegmentLength];
        result = result.TrimEnd('.', ' ').TrimStart('.');

        if (result.Length == 0)
            return "Untitled";

        // "CON.txt" is reserved too, so check the part before the first dot.
        var stem = result.Split('.')[0].TrimEnd();
        return ReservedNames.Contains(stem) ? result + "_" : result;
    }

    public static string ApplicationFolderName(DateOnly date, string role) =>
        $"{date:yyyy-MM-dd}_{Sanitize(role)}";

    /// <summary>Returns <paramref name="path"/>, or "path (2)", "path (3)"... if it is already taken.</summary>
    public static string MakeUnique(string path)
    {
        if (!Directory.Exists(path))
            return path;

        for (var i = 2; ; i++)
        {
            var candidate = $"{path} ({i})";
            if (!Directory.Exists(candidate))
                return candidate;
        }
    }
}
