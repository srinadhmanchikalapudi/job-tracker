namespace JobTracker.Core.Tests;

public class JdCleanerTests
{
    private const string Body = """
        Senior Backend Engineer
        Umbrella Health · Seattle, WA (Hybrid)

        About the job

        We build tools that help clinicians.

        Responsibilities

        - Design and build APIs
        - Mentor engineers
          - Pair on reviews

        Requirements

        - 5+ years of C#
        - Strong SQL
        """;

    [Fact]
    public void Clean_RemovesMenuHeadButtonsAndFooter_KeepsStructureExactly()
    {
        var page = "Skip to main content\nHome\nJobs\nMessaging\n" + Body +
                   "\n\nEasy Apply\nSave\nShow more\n\nSimilar jobs\nOther Engineer\nAnother Engineer\nPrivacy Policy\n";

        var result = JdCleaner.Clean(page);

        Assert.Equal(Body.Replace("\r\n", "\n"), result.Text);
        Assert.True(result.RemovedLines >= 8);
    }

    [Fact]
    public void Clean_PreservesBlankLinesBetweenSections()
    {
        var result = JdCleaner.Clean(Body);

        Assert.Contains("About the job\n\nWe build tools", result.Text);
        Assert.Contains("Responsibilities\n\n- Design", result.Text);
        Assert.Contains("  - Pair on reviews", result.Text); // nested indent untouched
    }

    [Fact]
    public void Clean_CollapsesBlankRuns_AndTrimsEnds()
    {
        var result = JdCleaner.Clean("\n\nSoftware Engineer\n\n\n\nWe build things.\n\n\n");

        Assert.Equal("Software Engineer\n\nWe build things.", result.Text);
    }

    [Fact]
    public void Clean_RemovesCookieBannersAndApplicantCounts()
    {
        var result = JdCleaner.Clean("Software Engineer\nWe use cookies to improve your experience\n120 applicants\nBuild services\n");

        Assert.Equal("Software Engineer\nBuild services", result.Text);
        Assert.Equal(2, result.RemovedLines);
    }

    [Fact]
    public void Clean_RemovesPostedAgoApplicantsLine_ButNotSentencesAboutApplicants()
    {
        var result = JdCleaner.Clean("Software Engineer\n2 weeks ago · 118 applicants\nWe typically receive over 500 applicants for each opening, so apply early.");

        Assert.DoesNotContain("118 applicants", result.Text);
        Assert.Contains("We typically receive over 500 applicants", result.Text);
    }

    [Fact]
    public void Clean_DoesNotTrimHead_WhenAboveTextIsRealContent()
    {
        const string text = "Company: Acme Corp\nLocation: Remote\nWe are hiring for the platform group.\nSenior Platform Engineer\nBuild things.";

        var result = JdCleaner.Clean(text);

        Assert.StartsWith("Company: Acme Corp", result.Text);
        Assert.Contains("We are hiring for the platform group.", result.Text);
    }

    [Fact]
    public void Clean_FooterMarkerTooEarly_IsKept()
    {
        // "Privacy" heading near the top of a short posting must not truncate it.
        var result = JdCleaner.Clean("Software Engineer\nPrivacy\nWe respect your data.\nBuild services");

        Assert.Contains("Build services", result.Text);
    }

    [Fact]
    public void Clean_ReturnsInputUnchanged_WhenNothingToRemove()
    {
        var result = JdCleaner.Clean(Body);

        Assert.Equal(Body.Replace("\r\n", "\n"), result.Text);
        Assert.Equal(0, result.RemovedLines);
    }

    [Fact]
    public void Clean_HandlesEmptyAndNull()
    {
        Assert.Equal("", JdCleaner.Clean("").Text);
        Assert.Equal("", JdCleaner.Clean(null).Text);
    }
}

public class StructureTests
{
    [Fact]
    public void Structure_ClassifiesHeadingsBulletsTextAndBlanks()
    {
        const string text = "Senior Engineer\n\nAbout the job\n\nWe build tools for clinicians every day.\n\n## Requirements\n\n- 5+ years of C#\n• Strong SQL\n\nBenefits:\n- Health insurance";

        var lines = BulletText.Structure(text);

        Assert.Equal(
            [LineKind.Heading, LineKind.Blank, LineKind.Heading, LineKind.Blank, LineKind.Text, LineKind.Blank,
             LineKind.Heading, LineKind.Blank, LineKind.Bullet, LineKind.Bullet, LineKind.Blank, LineKind.Heading, LineKind.Bullet],
            lines.Select(l => l.Kind));
    }

    [Fact]
    public void Structure_DisplayStripsMarkersAndHashes()
    {
        var lines = BulletText.Structure("## Requirements\n- 5+ years of C#\n**Perks**\n");

        Assert.Equal(["Requirements", "5+ years of C#", "Perks"], lines.Select(l => l.Display));
        Assert.Equal("- 5+ years of C#", lines[1].Raw);
    }

    [Fact]
    public void Structure_CollapsesBlankRuns_DropsLeadingAndTrailing_KeepsLineNumbers()
    {
        var lines = BulletText.Structure("\n\nFirst line of text here.\n\n\n\nSecond line of text here.\n\n");

        Assert.Equal([LineKind.Text, LineKind.Blank, LineKind.Text], lines.Select(l => l.Kind));
        Assert.Equal([3, 4, 7], lines.Select(l => l.LineNumber));
    }

    [Fact]
    public void Structure_SentencesAndNegativeNumbers_AreNotBulletsOrHeadings()
    {
        var lines = BulletText.Structure("-5% churn improvement.\nWe are a growing team of engineers.\nSeattle, WA");

        Assert.All(lines, l => Assert.Equal(LineKind.Text, l.Kind));
    }

    [Fact]
    public void Structure_OnlyTextAndBulletsAreActionable()
    {
        var lines = BulletText.Structure("Requirements:\n- C#\nPlain sentence line here.");

        Assert.Equal([false, true, true], lines.Select(l => l.IsActionable));
    }
}

public class OriginalJdTests
{
    [Fact]
    public void Create_WithOriginalJd_SavesBothFiles()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);

        var app = store.Create(new NewApplication("Acme", "Dev", "clean", "resume", OriginalJd: "clean\nEasy Apply"));

        Assert.Equal("clean", File.ReadAllText(app.JdPath));
        Assert.Equal("clean\nEasy Apply", File.ReadAllText(Path.Combine(app.FolderPath, ApplicationStore.OriginalJdFile)));
    }

    [Fact]
    public void Create_WhenNothingWasCleaned_DoesNotWriteOriginal()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);

        var app = store.Create(new NewApplication("Acme", "Dev", "same", "resume", OriginalJd: "same"));

        Assert.False(File.Exists(Path.Combine(app.FolderPath, ApplicationStore.OriginalJdFile)));
    }
}
