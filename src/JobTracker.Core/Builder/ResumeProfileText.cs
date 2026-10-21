using System.Text.RegularExpressions;

namespace JobTracker.Core.Builder;

/// <summary>
/// Lays a <see cref="ResumeProfile"/> out as resume text in the same form every other resume in Job Tracker has: a "# Name" line, a headline and contact
/// line, "##" section headings, a job as one or two short heading lines followed by "- " bullets. Nothing is added: every word is the person's.
/// </summary>
public static partial class ResumeProfileText
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private static string Clean(string? text) => Spaces().Replace((text ?? "").Trim(), " ");

    private static string Join(string separator, params string[] parts) => string.Join(separator, parts.Select(Clean).Where(p => p.Length > 0));

    public static string Build(ResumeProfile profile)
    {
        var lines = new List<string>();

        if (Clean(profile.Name).Length > 0)
            lines.Add("# " + Clean(profile.Name));
        if (Clean(profile.Headline).Length > 0)
            lines.Add(Clean(profile.Headline));
        var contact = Join(" | ", profile.Location, profile.Email, profile.Phone, profile.Links);
        if (contact.Length > 0)
            lines.Add(contact);

        if (Clean(profile.Summary).Length > 0)
            Section(lines, "Summary", [Clean(profile.Summary)]);

        var jobs = profile.Jobs.Where(j => !j.IsBlank).ToList();
        if (jobs.Count > 0)
        {
            Section(lines, "Experience", []);
            for (var i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                if (i > 0)
                    lines.Add("");
                var heading = Join(", ", job.Title, job.Company, job.Location);
                if (heading.Length > 0)
                    lines.Add(heading);
                var dates = Join(" – ", job.Start, job.EndText);
                if (dates.Length > 0)
                    lines.Add(dates);
                foreach (var bullet in job.Bullets.Select(Clean).Where(b => b.Length > 0))
                    lines.Add("- " + BulletText.Clean(bullet));
            }
        }

        var skills = Paragraphs(profile.Skills);
        if (skills.Count > 0)
            Section(lines, "Skills", skills);

        var schools = profile.Education.Where(s => !s.IsBlank).ToList();
        if (schools.Count > 0)
        {
            Section(lines, "Education", []);
            for (var i = 0; i < schools.Count; i++)
            {
                if (i > 0)
                    lines.Add("");
                var school = schools[i];
                var heading = Join(", ", school.Degree, school.School);
                if (heading.Length > 0)
                    lines.Add(heading);
                if (Clean(school.Dates).Length > 0)
                    lines.Add(Clean(school.Dates));
                if (Clean(school.Details).Length > 0)
                    lines.Add(Clean(school.Details));
            }
        }

        var certifications = Paragraphs(profile.Certifications);
        if (certifications.Count > 0)
            Section(lines, "Certifications", certifications.Select(c => "- " + BulletText.Clean(c)).ToList());

        return string.Join("\n", lines) + (lines.Count > 0 ? "\n" : "");
    }

    private static void Section(List<string> lines, string heading, List<string> body)
    {
        if (lines.Count > 0)
            lines.Add("");
        lines.Add("## " + heading);
        lines.AddRange(body);
    }

    private static List<string> Paragraphs(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Select(Clean).Where(l => l.Length > 0).ToList();
}
