namespace JobTracker.Core.Tailoring;

/// <summary>One entry of the experience section: its heading line(s) and the bullets under it.</summary>
/// <param name="Header">The company and title lines joined, for example "Contoso Insurance · Senior Engineer, 2021 to present".</param>
/// <param name="Section">The heading it sits under ("Experience", "Projects"), or empty.</param>
/// <param name="LastLine">1-based line of the last bullet, which is where a new bullet goes.</param>
public sealed record ResumeJob(string Header, string Section, IReadOnlyList<ResumeBullet> Bullets, int LastLine)
{
    public string Label => Bullets.Count == 1 ? $"{Header} (1 bullet)" : $"{Header} ({Bullets.Count} bullets)";
}

public enum LineRole
{
    Section,
    JobHeader,
    Bullet,
    Prose,
}

public sealed record RoleLine(StructuredLine Line, LineRole Role);

/// <summary>
/// Splits a resume into jobs: a short line or two (company, title, dates) followed by bullets. Plain rules on the structure the reader already
/// produces, so it works on a resume pasted as text, read from a PDF, or written in Markdown. Skills and education lists are not jobs.
/// </summary>
public static class ResumeStructure
{
    private const int MaxHeaderLine = 120;
    private const int MaxHeaderLines = 3;

    private static readonly HashSet<string> KnownSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "experience", "work experience", "professional experience", "employment", "work history", "projects", "skills", "technical skills", "education",
        "summary", "professional summary", "profile", "certifications", "achievements", "awards", "publications", "interests",
    };

    private static readonly HashSet<string> NotJobSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "skills", "technical skills", "education", "certifications", "awards", "publications", "interests",
    };

    public static IReadOnlyList<ResumeJob> Jobs(string resumeText)
    {
        var lines = BulletText.Structure(resumeText).Where(l => l.Kind != LineKind.Blank).ToList();
        var jobs = new List<ResumeJob>();

        var section = "";
        var header = new List<string>();
        List<ResumeBullet>? current = null;
        string currentHeader = "", currentSection = "";

        void Close()
        {
            if (current is { Count: > 0 } && !NotJobSections.Contains(currentSection.Trim()))
                jobs.Add(new ResumeJob(currentHeader, currentSection, current, current[^1].LineNumber));
            current = null;
        }

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            switch (line.Kind)
            {
                case LineKind.Heading when !KnownSections.Contains(line.Display.Trim()) && IsHeaderRun(lines, i):
                case LineKind.Text when IsHeaderRun(lines, i):
                    Close();
                    header.Add(line.Display);
                    break;

                case LineKind.Heading:
                    Close();
                    header.Clear();
                    section = line.Display.Trim();
                    break;

                case LineKind.Text:
                    if (current is null)
                        header.Clear(); // prose before any job (a summary) is not part of the next job's heading
                    break; // inside a job, a stray paragraph is left alone

                case LineKind.Bullet:
                    if (current is null)
                    {
                        current = [];
                        currentHeader = header.Count > 0 ? string.Join(" · ", header) : "Other bullets";
                        currentSection = section;
                        header.Clear();
                    }

                    current.Add(new ResumeBullet(line.LineNumber, line.Raw, line.Display));
                    break;
            }
        }

        Close();
        return jobs;
    }

    /// <summary>
    /// Says what each line of a resume is, using the same rules as <see cref="Jobs"/>: a section heading, a line of a job's heading (company, title,
    /// dates), a bullet, or ordinary text. Used to lay a resume out as a document.
    /// </summary>
    public static IReadOnlyList<RoleLine> Annotate(string resumeText)
    {
        var lines = BulletText.Structure(resumeText).Where(l => l.Kind != LineKind.Blank).ToList();
        var result = new List<RoleLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var role = line.Kind switch
            {
                LineKind.Bullet => LineRole.Bullet,
                LineKind.Heading when !KnownSections.Contains(line.Display.Trim()) && IsHeaderRun(lines, i) => LineRole.JobHeader,
                LineKind.Text when IsHeaderRun(lines, i) => LineRole.JobHeader,
                LineKind.Heading => LineRole.Section,
                _ => LineRole.Prose,
            };
            result.Add(new RoleLine(line, role));
        }

        return result;
    }

    public static bool IsKnownSection(string heading) => KnownSections.Contains(heading.Trim());

    /// <summary>The person's name: the first line of the resume when it looks like one (a few words, no digits, no @). Null otherwise.</summary>
    public static string? PersonName(string resumeText)
    {
        var first = BulletText.Structure(resumeText).FirstOrDefault(l => l.Kind != LineKind.Blank);
        if (first is null || first.Kind == LineKind.Bullet)
            return null;
        var text = first.Display.Trim();
        if (KnownSections.Contains(text) || text.Length > 60)
            return null;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length is >= 1 and <= 5 && !text.Any(char.IsDigit) && !text.Contains('@') && !text.Contains('|') && !text.Contains(':') ? text : null;
    }

    /// <summary>A run of one to three short non-section lines that ends right before a bullet: company, title, dates.</summary>
    private static bool IsHeaderRun(List<StructuredLine> lines, int index)
    {
        for (var i = index; i < lines.Count && i < index + MaxHeaderLines; i++)
        {
            var line = lines[i];
            if (line.Kind == LineKind.Bullet)
                return i > index;
            if (line.Kind == LineKind.Blank || line.Display.Length > MaxHeaderLine || KnownSections.Contains(line.Display.Trim()))
                return false;
        }

        return false;
    }
}
