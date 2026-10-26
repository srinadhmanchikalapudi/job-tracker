using System.Text.RegularExpressions;

namespace JobTracker.Core.Tailoring;

/// <param name="Years">How many years of experience the resume shows.</param>
/// <param name="FromDates">True when counted from the date ranges of the jobs; false when only a sentence like "8+ years" was found.</param>
public sealed record ResumeYears(double Years, bool FromDates);

/// <param name="Score">0 to 1.</param>
/// <param name="BestResumeTitle">The title on the resume that came closest, or empty.</param>
public sealed record TitleMatch(double Score, string BestResumeTitle);

/// <summary>
/// Reads the parts of a resume that are not skills but that a recruiter or an ATS weighs: how many years of work it shows, whether the titles fit the
/// job, and whether it shows leading or mentoring. Plain rules over the resume text, so every result can be explained.
/// </summary>
public static partial class ExperienceReader
{
    private const string Months = "jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec";

    private static readonly Regex NamedMonthRange = new(
        $@"(?:(?<m1>{Months})[a-z]*\.?\s*,?\s*)?(?<y1>(?:19|20)\d{{2}})\s*(?:-|–|—|to|until|through)\s*(?:(?<m2>{Months})[a-z]*\.?\s*,?\s*)?(?<y2>(?:19|20)\d{{2}}|present|current|now|today|ongoing)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NumericMonthRange = new(
        @"(?<m1>\d{1,2})/(?<y1>(?:19|20)\d{2})\s*(?:-|–|—|to|until|through)\s*(?:(?<m2>\d{1,2})/)?(?<y2>(?:19|20)\d{2}|present|current|now|today|ongoing)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NotWork = new(@"education|certif|award|publication|course|training|honou?r|volunteer", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SchoolLine = new(@"universit|college|institute|school|bachelor|master|b\.?tech|b\.?sc|m\.?sc|degree|gpa|diploma", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StatedYears = new(@"(?<![\d$./])(?<n>\d{1,2})\s*\+?\s*(?:years?|yrs?)\b(?!\s+(?:ago|old))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Years of work the resume shows: the date ranges of the jobs, with overlaps counted once (two jobs at the same time are not twice the experience),
    /// and education left out. When the resume has no dates, a sentence such as "8+ years of experience" is used instead, and the result says so.
    /// </summary>
    public static ResumeYears? YearsOf(IReadOnlyList<ResumeLine> lines, DateOnly today)
    {
        var months = new SortedSet<int>(); // each month that is inside some job, as year*12+month
        foreach (var line in lines.Where(l => !NotWork.IsMatch(l.Section) && !SchoolLine.IsMatch(l.Text)))
        {
            foreach (Match m in NamedMonthRange.Matches(line.Text))
                AddRange(months, MonthNumber(m.Groups["m1"].Value, 1), int.Parse(m.Groups["y1"].Value), m.Groups["m2"], m.Groups["y2"].Value, today, named: true);
            foreach (Match m in NumericMonthRange.Matches(line.Text))
                AddRange(months, int.TryParse(m.Groups["m1"].Value, out var month) ? Math.Clamp(month, 1, 12) : 1, int.Parse(m.Groups["y1"].Value), m.Groups["m2"], m.Groups["y2"].Value, today, named: false);
        }

        if (months.Count > 0)
            return new ResumeYears(Math.Round(months.Count / 12.0, 1), FromDates: true);

        var stated = lines.Where(l => !SchoolLine.IsMatch(l.Text))
            .SelectMany(l => StatedYears.Matches(l.Text).Select(m => double.Parse(m.Groups["n"].Value)))
            .Where(n => n is >= 1 and <= 40).DefaultIfEmpty(0).Max();
        return stated > 0 ? new ResumeYears(stated, FromDates: false) : null;
    }

    private static void AddRange(SortedSet<int> months, int startMonth, int startYear, Group endMonth, string endYear, DateOnly today, bool named)
    {
        var present = !int.TryParse(endYear, out var y2);
        var start = startYear * 12 + startMonth;
        int end;
        if (present)
            end = today.Year * 12 + today.Month;
        else
        {
            var month = endMonth.Success ? (named ? MonthNumber(endMonth.Value, 12) : Math.Clamp(int.Parse(endMonth.Value), 1, 12)) : 12;
            end = Math.Min(y2 * 12 + month, today.Year * 12 + today.Month); // never past today
        }

        if (end < start || end - start > 12 * 50)
            return; // not a real stretch of work
        for (var m = start; m <= end; m++)
            months.Add(m);
    }

    private static int MonthNumber(string name, int whenMissing) =>
        name.Length >= 3 && MonthTable.TryGetValue(name[..3].ToLowerInvariant(), out var month) ? month : whenMissing;

    private static readonly Dictionary<string, int> MonthTable = new()
    {
        ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["apr"] = 4, ["may"] = 5, ["jun"] = 6, ["jul"] = 7, ["aug"] = 8, ["sep"] = 9, ["oct"] = 10, ["nov"] = 11, ["dec"] = 12,
    };

    // ---------------------------------------------------------------- job title

    private static readonly HashSet<string> TitleStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "with", "and", "of", "the", "a", "an", "for", "in", "to", "at", "on", "remote", "hybrid", "onsite", "on-site", "contract", "contractor", "w2", "c2c", "c2h",
        "full-time", "fulltime", "part-time", "permanent", "temporary", "i", "ii", "iii", "iv", "level", "core", "software", "application", "applications", "web",
    };

    private static readonly Dictionary<string, int> Seniority = new(StringComparer.OrdinalIgnoreCase)
    {
        ["junior"] = 1, ["jr"] = 1, ["associate"] = 1, ["mid"] = 2, ["senior"] = 3, ["sr"] = 3, ["lead"] = 4, ["staff"] = 4, ["principal"] = 5, ["architect"] = 4,
    };

    private static readonly Regex TitleWord = new(@"developer|engineer|architect|analyst|consultant|programmer|administrator|specialist|scientist|devops|\bsre\b|manager|lead\b|designer|tester|\bqa\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TitleSplit = new(@"[,|·•]|\s[-–—]\s|\(|\)", RegexOptions.Compiled);

    /// <summary>The titles a resume shows: the headline under the name, and the title parts of each job heading.</summary>
    public static IReadOnlyList<string> TitlesOf(string resumeText)
    {
        var titles = new List<string>();
        var name = ResumeStructure.PersonName(resumeText);
        var seenJob = false;
        foreach (var line in ResumeLineReader.Read(resumeText))
        {
            if (line.Role == LineRole.JobHeader)
                seenJob = true;
            else if (seenJob || line.Role != LineRole.Prose || line.Text.Length > 90 || line.Text.Contains('@') || line.Text.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue; // the headline is only the first short lines before the first job

            foreach (var part in TitleSplit.Split(line.Text).Select(p => p.Trim()).Where(p => p.Length > 0 && TitleWord.IsMatch(p)))
                titles.Add(part);
        }

        return titles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>How well the titles on a resume fit a job title: the words of the job title (developer and engineer count as the same) and the seniority.</summary>
    public static TitleMatch TitleSimilarity(string jobTitle, IReadOnlyList<string> resumeTitles)
    {
        var wanted = Tokens(jobTitle, out var wantedSeniority);
        if (wanted.Count == 0)
            return new TitleMatch(0, "");

        var best = new TitleMatch(0, "");
        foreach (var title in resumeTitles)
        {
            var have = Tokens(title, out var haveSeniority);
            var coverage = (double)wanted.Count(w => have.Contains(w)) / wanted.Count;
            var level = wantedSeniority == 0 || haveSeniority >= wantedSeniority ? 1.0 : Math.Max(0, 1 - 0.25 * (wantedSeniority - haveSeniority));
            var score = Math.Round(0.85 * coverage + 0.15 * level, 3);
            if (score > best.Score)
                best = new TitleMatch(score, title);
        }

        return best;
    }

    private static HashSet<string> Tokens(string title, out int seniority)
    {
        seniority = 0;
        var text = Regex.Replace(title.ToLowerInvariant(), @"full[\s-]*stack", "fullstack");
        text = text.Replace("dotnet", ".net").Replace("c sharp", "c#").Replace("csharp", "c#");
        var tokens = new HashSet<string>();
        foreach (var raw in Regex.Split(text, @"[^a-z0-9#.+]+").Select(t => t.Trim('.')).Where(t => t.Length > 0))
        {
            if (Seniority.TryGetValue(raw, out var level))
            {
                seniority = Math.Max(seniority, level);
                if (raw is not ("lead" or "architect"))
                    continue; // lead and architect are also the job itself
            }

            if (TitleStopWords.Contains(raw))
                continue;
            tokens.Add(raw switch { "developer" or "programmer" or "swe" or "coder" => "engineer", "net" => ".net", _ => raw });
        }

        return tokens;
    }

    // ---------------------------------------------------------------- leadership

    private static readonly Regex LeadershipShown = new(
        @"\b(?:led|leading|mentored|mentoring|coached|supervised|spearheaded|oversaw|directed|headed|onboarded|championed|team\s+of\s+\d+|managed\s+(?:a\s+|the\s+)?(?:team|people|engineers|developers|staff|group|\d+)|lead\s+(?:developer|engineer|a\s+team)|guided\s+(?:junior|engineers|developers|the\s+team)|trained\s+(?:\d+\s+)?(?:engineers|developers|the\s+team|junior|new\s+hires|staff))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The lines of the resume that show leading, mentoring or managing people (a skills list does not count).</summary>
    public static IReadOnlyList<string> LeadershipShownIn(IReadOnlyList<ResumeLine> lines) =>
        lines.Where(l => !l.InSkillsList && LeadershipShown.IsMatch(l.Text)).Select(l => l.Text).Distinct().ToList();
}