namespace JobTracker.Core.Tailoring;

public enum DiffKind
{
    Same,
    Added,
    Removed,
}

public sealed record DiffPart(string Text, DiffKind Kind);

/// <summary>A word-by-word before and after, so the person sees exactly which words changed.</summary>
public static class WordDiff
{
    public static IReadOnlyList<DiffPart> Compare(string before, string after)
    {
        var a = Tokens(before);
        var b = Tokens(after);

        // Longest common subsequence of words.
        var lcs = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                lcs[i, j] = Same(a[i], b[j]) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var raw = new List<DiffPart>();
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (Same(a[x], b[y]))
            {
                raw.Add(new DiffPart(b[y], DiffKind.Same)); // the new spelling, so "workers" becoming "workers," reads as the same word
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                raw.Add(new DiffPart(a[x++], DiffKind.Removed));
            }
            else
            {
                raw.Add(new DiffPart(b[y++], DiffKind.Added));
            }
        }

        while (x < a.Length)
            raw.Add(new DiffPart(a[x++], DiffKind.Removed));
        while (y < b.Length)
            raw.Add(new DiffPart(b[y++], DiffKind.Added));

        // Neighbouring words of the same kind read better as one run.
        var merged = new List<DiffPart>();
        foreach (var part in raw)
        {
            if (merged.Count > 0 && merged[^1].Kind == part.Kind)
                merged[^1] = merged[^1] with { Text = merged[^1].Text + " " + part.Text };
            else
                merged.Add(part);
        }

        return merged;
    }

    private static string[] Tokens(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // A trailing comma or full stop does not make a different word.
    private static bool Same(string x, string y) =>
        string.Equals(x.TrimEnd(',', '.', ';', ':'), y.TrimEnd(',', '.', ';', ':'), StringComparison.Ordinal);
}
