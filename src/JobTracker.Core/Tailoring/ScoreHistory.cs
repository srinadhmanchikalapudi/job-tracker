namespace JobTracker.Core.Tailoring;

/// <summary>One version of the resume, as text, to be scored for the history.</summary>
/// <param name="Label">What it is called: "Version 3", or "Your resume now".</param>
/// <param name="At">When it was saved, so the history runs in the order things happened.</param>
public sealed record HistorySource(string Label, DateTime At, string Text, bool IsCurrent);

/// <param name="Points">Overall points gained (positive) or lost (negative) because of it.</param>
public sealed record ScoreChange(string Name, string Text, double Points);

/// <param name="Delta">Change in the overall score since the entry before it; null for the first.</param>
/// <param name="Changes">What moved the score since the entry before it, biggest first.</param>
public sealed record HistoryEntry(string Label, DateTime At, bool IsCurrent, int Overall, int? Delta, IReadOnlyList<ScoreChange> Changes)
{
    public string Summary => Delta is null ? "first score" : Delta > 0 ? $"+{Delta}" : Delta < 0 ? $"{Delta}" : "no change";
}

/// <summary>How the score moved from one version of the resume to the next, and which changes moved it.</summary>
public static class ScoreHistory
{
    public const string OriginalLabel = "Original resume";

    /// <summary>
    /// Scores each source, oldest first. Sources with exactly the same text are one entry (the version in use is usually also a saved version), which keeps
    /// the name of the saved version and is marked as in use. The earliest entry is the original resume.
    /// </summary>
    public static IReadOnlyList<HistoryEntry> Build(IEnumerable<HistorySource> sources, Func<string, ScoreReport> score)
    {
        var merged = new List<(string Label, DateTime At, bool IsCurrent, string Text)>();
        foreach (var source in sources.OrderBy(s => s.At))
        {
            var key = Normalize(source.Text);
            var existing = merged.FindIndex(m => Normalize(m.Text) == key);
            if (existing < 0)
                merged.Add((source.Label, source.At, source.IsCurrent, source.Text));
            else
            {
                var (label, at, current, text) = merged[existing];
                // keep the name of a saved version over "Your resume now", and the earlier date
                merged[existing] = (label.StartsWith("Version", StringComparison.Ordinal) ? label : source.Label, at < source.At ? at : source.At, current || source.IsCurrent, text);
            }
        }

        var entries = new List<HistoryEntry>();
        ScoreReport? previous = null;
        for (var i = 0; i < merged.Count; i++)
        {
            var m = merged[i];
            var report = score(m.Text);
            var label = i == 0 && merged.Count > 1 ? OriginalLabel : m.Label;
            entries.Add(new HistoryEntry(label, m.At, m.IsCurrent, report.Overall, previous is null ? null : report.Overall - previous.Overall,
                previous is null ? [] : Changes(previous, report)));
            previous = report;
        }

        return entries;
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>What moved between two scores: each requirement whose level changed, and the experience, title and leadership parts.</summary>
    private static IReadOnlyList<ScoreChange> Changes(ScoreReport before, ScoreReport after)
    {
        var changes = new List<ScoreChange>();
        foreach (var req in after.Requirements)
        {
            var was = before.Find(req.Name);
            var wasLevel = was?.Level ?? MatchLevel.Missing;
            if (wasLevel == req.Level)
                continue;
            changes.Add(new ScoreChange(req.Name, $"{req.Name}: {Describe(wasLevel)} → {Describe(req.Level)}", req.Points - (was?.Points ?? 0)));
        }

        foreach (var part in new[] { ScorePart.Years, ScorePart.Title, ScorePart.Leadership })
        {
            var b = before.Components.FirstOrDefault(c => c.Part == part);
            var a = after.Components.FirstOrDefault(c => c.Part == part);
            if (b is null || a is null)
                continue;
            // For leadership, the requirements are already listed one by one; what is left is the leadership shown in the work.
            var reqPoints = part == ScorePart.Leadership
                ? after.Requirements.Where(r => r.Group == ScoreGroup.Leadership).Sum(r => r.Points) - before.Requirements.Where(r => r.Group == ScoreGroup.Leadership).Sum(r => r.Points)
                : 0;
            var delta = a.Earned - b.Earned - reqPoints;
            if (Math.Abs(delta) < 0.05)
                continue;
            changes.Add(part switch
            {
                ScorePart.Years => new ScoreChange("Years of experience", $"Years of experience: {before.YearsShown?.Years:0.#} → {after.YearsShown?.Years:0.#}", delta),
                ScorePart.Title => new ScoreChange("Job title", $"Job title: “{before.Title.BestResumeTitle}” → “{after.Title.BestResumeTitle}”", delta),
                _ => new ScoreChange("Leadership shown", $"Leadership shown in your work: {before.LeadershipLinesShown} → {after.LeadershipLinesShown} lines", delta),
            });
        }

        return changes.Where(c => Math.Abs(c.Points) >= 0.05).OrderByDescending(c => Math.Abs(c.Points)).ThenBy(c => c.Name).Take(8).ToList();
    }

    private static string Describe(MatchLevel level) => level switch
    {
        MatchLevel.Strong => "matched",
        MatchLevel.Partial => "partly matched",
        MatchLevel.Claimed => "not written yet",
        _ => "missing",
    };
}