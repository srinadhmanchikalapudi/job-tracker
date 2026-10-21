namespace JobTracker.Core.Builder;

/// <summary>One job in a resume that is being built.</summary>
public sealed class ProfileJob
{
    public string Company { get; set; } = "";
    public string Title { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>As the person writes it: "Jan 2021", "2021".</summary>
    public string Start { get; set; } = "";

    public string End { get; set; } = "";

    /// <summary>A job the person still has. The end date then reads "Present".</summary>
    public bool IsCurrent { get; set; }

    public List<string> Bullets { get; set; } = [];

    public string EndText => IsCurrent ? "Present" : End.Trim();

    public bool IsBlank =>
        Company.Trim().Length == 0 && Title.Trim().Length == 0 && Location.Trim().Length == 0 && Start.Trim().Length == 0 && End.Trim().Length == 0
        && Bullets.All(b => b.Trim().Length == 0);
}

public sealed class ProfileSchool
{
    public string School { get; set; } = "";
    public string Degree { get; set; } = "";
    public string Dates { get; set; } = "";
    public string Details { get; set; } = "";

    public bool IsBlank => School.Trim().Length == 0 && Degree.Trim().Length == 0 && Dates.Trim().Length == 0 && Details.Trim().Length == 0;
}

/// <summary>
/// A resume written from scratch, kept as separate answers (contact details, jobs, schools, skills) rather than as one block of text, so each part can be
/// edited on its own. <see cref="ResumeProfileText"/> lays it out as the text Job Tracker uses for every resume.
/// </summary>
public sealed class ResumeProfile
{
    public string Name { get; set; } = "";

    /// <summary>The line under the name, for example "Senior Software Engineer".</summary>
    public string Headline { get; set; } = "";

    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>LinkedIn, portfolio or GitHub, as the person wants it printed.</summary>
    public string Links { get; set; } = "";

    public string Summary { get; set; } = "";
    public List<ProfileJob> Jobs { get; set; } = [];
    public List<ProfileSchool> Education { get; set; } = [];

    /// <summary>One group per line, for example "Languages: C#, Python".</summary>
    public string Skills { get; set; } = "";

    /// <summary>One per line.</summary>
    public string Certifications { get; set; } = "";

    public bool IsEmpty =>
        Name.Trim().Length == 0 && Headline.Trim().Length == 0 && Email.Trim().Length == 0 && Phone.Trim().Length == 0 && Location.Trim().Length == 0
        && Links.Trim().Length == 0 && Summary.Trim().Length == 0 && Skills.Trim().Length == 0 && Certifications.Trim().Length == 0
        && Jobs.All(j => j.IsBlank) && Education.All(e => e.IsBlank);
}
