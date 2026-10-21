using System.Text.RegularExpressions;

namespace JobTracker.Core.Builder;

/// <summary>
/// Plain, fixed-rule advice for a resume being built: nothing is generated and nothing is blocked. Each piece of advice names what is wrong and what to
/// do, in words a person can act on.
/// </summary>
public static partial class ResumeAdvice
{
    private const int LongBulletWords = 35;

    private static readonly string[] WeakOpenings =
    [
        "responsible for", "duties included", "duties include", "tasked with", "worked on", "helped with", "in charge of", "assisted with", "involved in",
    ];

    // A figure written as a digit or as a word ("four engineers", "a dozen", "two million").
    [GeneratedRegex(@"\d|\b(?:one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|dozen|dozens|hundred|hundreds|thousand|thousands|million|millions|half|double|doubled|triple|tripled)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Digit();

    private static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>The one most useful tip for a bullet, or null when it reads well.</summary>
    public static string? BulletTip(string? bullet)
    {
        var text = BulletText.Clean(bullet ?? "");
        if (text.Length == 0)
            return null;

        var lower = text.ToLowerInvariant();
        var words = Words(text);

        if (lower.StartsWith("i ", StringComparison.Ordinal) || lower.StartsWith("my ", StringComparison.Ordinal) || lower.StartsWith("we ", StringComparison.Ordinal))
            return "Start with what you did (Built, Led, Cut…), not “I”, “my” or “we”.";
        if (WeakOpenings.FirstOrDefault(w => lower.StartsWith(w, StringComparison.Ordinal)) is { } weak)
            return $"Start with what you did (Built, Led, Cut…) instead of “{text[..weak.Length].ToLowerInvariant()}”.";
        if (words > LongBulletWords)
            return $"This is {words} words. One or two lines (under 30 words) reads better.";
        if (words >= 6 && !Digit().IsMatch(text))
            return "Add a number if you have one: how many, how much, how fast.";
        return null;
    }

    /// <summary>Everything worth fixing in a whole resume, most important first. Empty when there is nothing to say.</summary>
    public static IReadOnlyList<string> Problems(ResumeProfile profile)
    {
        var problems = new List<string>();

        if (profile.Name.Trim().Length == 0)
            problems.Add("Add your name.");
        if (profile.Email.Trim().Length == 0 && profile.Phone.Trim().Length == 0)
            problems.Add("Add an email address or a phone number so employers can reach you.");

        var jobs = profile.Jobs.Where(j => !j.IsBlank).ToList();
        if (jobs.Count == 0)
            problems.Add("Add at least one job under Experience.");

        foreach (var job in jobs)
        {
            var label = job.Company.Trim().Length > 0 ? job.Company.Trim() : job.Title.Trim().Length > 0 ? job.Title.Trim() : "A job";
            if (job.Company.Trim().Length == 0 || job.Title.Trim().Length == 0)
                problems.Add($"{label}: add both the company and your job title.");
            if (job.Start.Trim().Length == 0)
                problems.Add($"{label}: add when you started.");
            if (!job.IsCurrent && job.End.Trim().Length == 0)
                problems.Add($"{label}: add when it ended, or tick that it is your current job.");
            if (YearsBackwards(job))
                problems.Add($"{label}: the end date is before the start date.");

            var bullets = job.Bullets.Where(b => b.Trim().Length > 0).ToList();
            if (bullets.Count == 0)
                problems.Add($"{label}: add a few bullets saying what you did.");
            else if (bullets.Count == 1)
                problems.Add($"{label}: one bullet is thin. Two to five is usual.");
            else if (bullets.Count > 8)
                problems.Add($"{label}: {bullets.Count} bullets is a lot. Keep the strongest five or six.");

            var tips = bullets.Count(b => BulletTip(b) is not null);
            if (tips > 0)
                problems.Add($"{label}: {tips} bullet{(tips == 1 ? " has" : "s have")} a tip beside {(tips == 1 ? "it" : "them")}.");

            if (bullets.GroupBy(b => BulletText.Key(b)).Any(g => g.Count() > 1))
                problems.Add($"{label}: two bullets say the same thing.");
        }

        if (profile.Skills.Trim().Length == 0)
            problems.Add("Add your skills. Employers' tracking systems look for them by name.");

        if (profile.Education.All(e => e.IsBlank))
            problems.Add("Add your education, or skip it if it does not help for these jobs.");

        return problems;
    }

    private static bool YearsBackwards(ProfileJob job)
    {
        if (job.IsCurrent)
            return false;
        var start = Year(job.Start);
        var end = Year(job.End);
        return start is not null && end is not null && end < start;
    }

    private static int? Year(string text)
    {
        var match = Regex.Match(text, @"(?:19|20)\d{2}");
        return match.Success ? int.Parse(match.Value) : null;
    }
}
