using System.Globalization;
using System.Text.RegularExpressions;

namespace JobTracker.Core;

public sealed record ExtractedJob(string? Company, string? Role, string? Url);

/// <summary>
/// Best-effort guess of company, role and link from a pasted job page. Purely local pattern matching:
/// it returns null for anything it is unsure about, and the user always reviews the result.
/// </summary>
public static class JdExtractor
{
    private const int TitleScanLines = 25;
    private const int LabelScanLines = 80;
    private const int AboutScanLines = 300;

    private static readonly Regex UrlPattern = new(@"https?://[^\s<>""'\)\]]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SourceUrlPattern = new(@"^SourceURL:(?<u>\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex RoleLabel = new(@"^(?:job\s*title|position(?:\s*title)?|title|role)\s*[:\-–]\s*(?<v>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CompanyLabel = new(@"^(?:company(?:\s*name)?|employer|hiring\s*company|organization)\s*[:\-–]\s*(?<v>.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TitleWord = new(
        @"\b(engineer|developer|architect|manager|analyst|scientist|designer|lead|director|consultant|specialist|administrator|programmer|intern|devops|sre|tester|qa|owner|head|vp|president|coordinator|technician|researcher|associate|principal|staff)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NotATitle = new(
        @"^(we\b|we're|you\b|join\b|about\b|apply\b|easy apply|share\b|save\b|sign\b|log\s?in|search\b|skip\b|looking\b|are you|as an?\b|the\b|what\b|responsibilit|qualification|requirement|similar\b|people also|report\b|show\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AtCompany = new(@"^(?<r>.+?)\s+at\s+(?<c>[A-Z0-9][^|]{1,50})$", RegexOptions.Compiled);
    private static readonly Regex About = new(@"^About\s+(?<v>[A-Z][\w&.,'’\- ]{1,50}?)\s*[:?]?$", RegexOptions.Compiled);

    private static readonly Regex LocationLike = new(
        @",\s*[A-Z]{2}\b|\b(remote|hybrid|on-?site|united states|full-?time|part-?time|contract|applicants?|ago|posted|apply|save|share|promoted|reposted)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] AboutIgnore = ["the ", "this ", "us", "you", "our "];

    private static readonly string[] Aggregators =
    [
        "linkedin.com", "indeed.com", "glassdoor.com", "ziprecruiter.com", "monster.com", "dice.com", "google.com",
        "wellfound.com", "angel.co", "simplyhired.com", "builtin.com", "otta.com", "welcometothejungle.com",
        "ycombinator.com", "careerbuilder.com", "usajobs.gov", "jooble.org",
    ];

    public static ExtractedJob Extract(string? text, string? sourceUrl = null)
    {
        text ??= "";
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        var url = WebUrl(sourceUrl) ?? FirstUrlIn(text);
        Uri? uri = url is not null && Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed : null;

        var (role, titleCompany, roleIndex) = FindRole(lines);
        var company = Labeled(lines, CompanyLabel)
                      ?? titleCompany
                      ?? AboutCompany(lines)
                      ?? (uri is null ? null : CompanyFromUrl(uri))
                      ?? CompanyAfterTitle(lines, roleIndex);

        return new ExtractedJob(Clean(company), Clean(role), url);
    }

    /// <summary>Chrome and Edge put "SourceURL:https://..." in the HTML clipboard header when you copy part of a page.</summary>
    public static string? SourceUrlFromHtmlClipboard(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        var match = SourceUrlPattern.Match(html);
        return match.Success ? WebUrl(match.Groups["u"].Value) : null;
    }

    private static (string? Role, string? Company, int Index) FindRole(List<string> lines)
    {
        for (var i = 0; i < Math.Min(lines.Count, LabelScanLines); i++)
        {
            var m = RoleLabel.Match(lines[i]);
            if (m.Success && m.Groups["v"].Value.Length <= 100)
                return (m.Groups["v"].Value, null, i);
        }

        for (var i = 0; i < Math.Min(lines.Count, TitleScanLines); i++)
        {
            var line = lines[i];
            if (!LooksLikeTitle(line))
                continue;

            line = line.Split(" | ")[0].Trim();
            var at = AtCompany.Match(line);
            return at.Success ? (at.Groups["r"].Value, at.Groups["c"].Value, i) : (line, null, i);
        }

        return (null, null, -1);
    }

    /// <summary>True for a short line that reads like a job title (used to find where the real posting starts).</summary>
    public static bool IsTitleLine(string line) => LooksLikeTitle(line);

    private static bool LooksLikeTitle(string line) =>
        line.Length is >= 3 and <= 90 &&
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 12 &&
        !line.EndsWith('.') &&
        !line.Contains(':') &&
        !line.Contains("http", StringComparison.OrdinalIgnoreCase) &&
        !NotATitle.IsMatch(line) &&
        TitleWord.IsMatch(line);

    private static string? Labeled(List<string> lines, Regex label)
    {
        foreach (var line in lines.Take(LabelScanLines))
        {
            var m = label.Match(line);
            if (m.Success && m.Groups["v"].Value.Length <= 80)
                return m.Groups["v"].Value;
        }

        return null;
    }

    private static string? AboutCompany(List<string> lines)
    {
        foreach (var line in lines.Take(AboutScanLines))
        {
            var m = About.Match(line);
            if (!m.Success)
                continue;

            var name = m.Groups["v"].Value.Trim();
            if (name.Split(' ').Length > 5 || AboutIgnore.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;
            return name;
        }

        return null;
    }

    /// <summary>LinkedIn-style pages put the company on the line right after the title.</summary>
    private static string? CompanyAfterTitle(List<string> lines, int roleIndex)
    {
        if (roleIndex < 0 || roleIndex + 1 >= lines.Count)
            return null;

        var candidate = lines[roleIndex + 1].Split([" · ", " • ", " | "], StringSplitOptions.None)[0].Trim();
        if (candidate.Length is < 2 or > 50 ||
            candidate.Split(' ').Length > 5 ||
            candidate.Contains(':') ||
            NotATitle.IsMatch(candidate) ||
            LocationLike.IsMatch(candidate) ||
            TitleWord.IsMatch(candidate))
            return null;

        return candidate;
    }

    private static string? CompanyFromUrl(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www."))
            host = host[4..];
        if (Aggregators.Any(a => host == a || host.EndsWith("." + a)))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var labels = host.Split('.');
        string? slug = null;

        if (host.EndsWith("greenhouse.io"))
            slug = segments.FirstOrDefault(s => s != "embed");
        else if (host is "jobs.lever.co" or "jobs.eu.lever.co" or "jobs.ashbyhq.com" or "apply.workable.com" or "jobs.smartrecruiters.com")
            slug = segments.FirstOrDefault();
        else if (host.EndsWith(".myworkdayjobs.com") || host.EndsWith(".bamboohr.com"))
            slug = labels[0];
        else if (labels.Length >= 3 && labels[0] is "careers" or "career" or "jobs")
            slug = labels[^2];
        else if (labels.Length >= 2 && segments.Length > 0 &&
                 (segments[0].Contains("career", StringComparison.OrdinalIgnoreCase) || segments[0].Equals("jobs", StringComparison.OrdinalIgnoreCase)))
            slug = labels[^2];

        return slug is null ? null : Prettify(slug);
    }

    private static string Prettify(string slug)
    {
        var spaced = Regex.Replace(slug.Replace('-', ' ').Replace('_', ' ').Replace('.', ' '), @"\s+", " ").Trim();
        return spaced == spaced.ToLowerInvariant()
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced)
            : spaced;
    }

    private static string? WebUrl(string? value)
    {
        var trimmed = value?.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? trimmed : null;
    }

    private static string? FirstUrlIn(string text)
    {
        var m = UrlPattern.Match(text);
        return m.Success ? WebUrl(m.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}', '\'', '"')) : null;
    }

    private static string? Clean(string? value)
    {
        if (value is null)
            return null;
        var cleaned = Regex.Replace(value, @"\s+", " ").Trim().TrimEnd(',', '.', '-', '|', ':', '·', '•', ' ');
        return cleaned.Length == 0 ? null : cleaned;
    }
}
