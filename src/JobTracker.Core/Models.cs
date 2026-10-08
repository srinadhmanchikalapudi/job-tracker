namespace JobTracker.Core;

public enum ApplicationStatus
{
    Applied,
    Interviewing,
    Offer,
    Rejected,
    Withdrawn,
}

public enum SearchScope
{
    Both,
    Jd,
    Resume,
}

public enum BulletSource
{
    Jd,
    Resume,
}

/// <summary>What is persisted in meta.json.</summary>
public sealed class ApplicationMeta
{
    public string Company { get; set; } = "";
    public string Role { get; set; } = "";
    public string? Url { get; set; }
    public DateOnly AppliedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Applied;
    public bool Starred { get; set; }
}

/// <summary>An application folder on disk plus its metadata. Text files are read on demand.</summary>
public sealed class JobApplication
{
    public required string FolderPath { get; init; }
    public required ApplicationMeta Meta { get; init; }

    public string JdPath => Path.Combine(FolderPath, ApplicationStore.JdFile);
    public string ResumePath => Path.Combine(FolderPath, ApplicationStore.ResumeFile);
    public string NotesPath => Path.Combine(FolderPath, ApplicationStore.NotesFile);
}

/// <param name="OriginalJd">The JD exactly as pasted, when <paramref name="JdText"/> was cleaned. Saved as jd.original.txt.</param>
public sealed record NewApplication(
    string Company,
    string Role,
    string JdText,
    string ResumeText,
    string? Url = null,
    DateOnly? AppliedDate = null,
    string? OriginalJd = null);

public sealed record SearchHit(JobApplication Application, BulletSource Source, int LineNumber, string Text);

public sealed class StarredBullet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public BulletSource Source { get; set; }

    /// <summary>Application folder relative to the data root, so the library survives moving the root.</summary>
    public string AppFolder { get; set; } = "";
    public string Company { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTimeOffset StarredAt { get; set; } = DateTimeOffset.Now;
}
