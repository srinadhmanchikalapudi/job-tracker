using JobTracker.Core.Tailoring;

namespace JobTracker.Core.Tests;

public class BulletVerifierTests
{
    private const string Resume = """
        ## Experience
        Contoso Insurance, Senior Engineer
        - Cut SQL Server query latency by 63% by redesigning indexes in the claims API
        - Led a team of four to move nightly batch jobs to Kafka consumers
        - Wrote the design-review template used by six squads
        """;

    private static BulletContext Ctx(string original, string[]? facts = null, string[]? claimed = null, string company = "") =>
        new(original, facts ?? [], Resume, claimed ?? [], company);

    private const string Latency = "- Cut SQL Server query latency by 63% by redesigning indexes in the claims API";

    [Fact]
    public void AcceptsAHonestRewordingThatUsesOnlyWhatIsThere() =>
        Assert.Empty(BulletVerifier.Verify("Cut claims API query latency by 63% on SQL Server by redesigning indexes", Ctx(Latency)));

    [Fact]
    public void RejectsAnInventedNumber()
    {
        var problems = BulletVerifier.Verify("Cut SQL Server query latency by 80% by redesigning indexes in the claims API", Ctx(Latency));
        Assert.Contains(problems, p => p.Contains("80%") && p.Contains("number"));
    }

    [Fact]
    public void AllowsANumberTheNotesProvide() =>
        Assert.Empty(BulletVerifier.Verify("Cut SQL Server query latency by 63% across 12 services by redesigning indexes in the claims API",
            Ctx(Latency, facts: ["It covered 12 services"])));

    [Fact]
    public void RejectsASkillThePersonHasNotClaimed()
    {
        var problems = BulletVerifier.Verify("Cut SQL Server query latency by 63% with Terraform-managed indexes in the claims API", Ctx(Latency));
        Assert.Contains(problems, p => p.Contains("Terraform"));
    }

    [Fact]
    public void AllowsASkillTheResumeSaysElsewhere() =>
        Assert.Empty(BulletVerifier.Verify("Cut SQL Server query latency by 63% in the claims API, feeding Kafka consumers", Ctx(Latency)));

    [Fact]
    public void AllowsAClaimedSkillOnlyWhenTheNoteIsGiven()
    {
        const string withDocker = "Cut SQL Server query latency by 63% in the claims API, with Docker-based test runs";
        Assert.NotEmpty(BulletVerifier.Verify(withDocker, Ctx(Latency)));
        Assert.Empty(BulletVerifier.Verify(withDocker, Ctx(Latency, facts: ["Ran the test suite in Docker containers"], claimed: ["Docker"])));
    }

    [Fact]
    public void RejectsABiggerRole()
    {
        const string original = "- Helped move nightly batch jobs to Kafka consumers";
        var problems = BulletVerifier.Verify("Led the move of nightly batch jobs to Kafka consumers", Ctx(original));
        Assert.Contains(problems, p => p.Contains("led"));
    }

    [Fact]
    public void AllowsTheSameVerbTheOriginalUsed() =>
        Assert.Empty(BulletVerifier.Verify("Led four engineers moving nightly batch jobs to Kafka consumers", Ctx("- Led a team of four to move nightly batch jobs to Kafka consumers")));

    [Fact]
    public void RejectsANameNobodyWrote()
    {
        var problems = BulletVerifier.Verify("Cut SQL Server query latency by 63% for Fabrikam by redesigning indexes in the claims API", Ctx(Latency));
        Assert.Contains(problems, p => p.Contains("Fabrikam"));
    }

    [Fact]
    public void AllowsANameTheResumeContains() =>
        Assert.Empty(BulletVerifier.Verify("Cut SQL Server query latency by 63% at Contoso by redesigning indexes in the claims API", Ctx(Latency)));

    [Fact]
    public void RejectsNamingTheCompanyBeingAppliedTo()
    {
        var problems = BulletVerifier.Verify("Cut SQL Server query latency by 63% for Northwind Labs by redesigning indexes", Ctx(Latency, company: "Northwind Labs"));
        Assert.Contains(problems, p => p.Contains("Northwind Labs"));
    }

    [Fact]
    public void RejectsNoChange() =>
        Assert.Contains(BulletVerifier.Verify("Cut SQL Server query latency by 63% by redesigning indexes in the claims API.", Ctx(Latency)), p => p.Contains("same"));

    [Fact]
    public void RejectsFirstPersonAndRambling()
    {
        Assert.Contains(BulletVerifier.Verify("I cut SQL Server query latency by 63% by redesigning indexes", Ctx(Latency)), p => p.Contains("first person"));
        var rambling = "Cut SQL Server query latency by 63% " + string.Join(' ', Enumerable.Repeat("really quickly", 40));
        Assert.Contains(BulletVerifier.Verify(rambling, Ctx(Latency)), p => p.Contains("longer"));
    }

    [Fact]
    public void RejectsEmptyAndMultiLine()
    {
        Assert.NotEmpty(BulletVerifier.Verify("   ", Ctx(Latency)));
        Assert.Contains(BulletVerifier.Verify("Cut latency\nby 63%", Ctx(Latency)), p => p.Contains("one line"));
    }

    [Fact]
    public void ReportsEveryProblemNotJustTheFirst()
    {
        var problems = BulletVerifier.Verify("Led a rollout of Terraform that cut latency by 80%", Ctx(Latency));
        Assert.True(problems.Count >= 3, string.Join(" | ", problems));
    }
}

public class WordDiffTests
{
    [Fact]
    public void ShowsOnlyTheWordsThatChanged()
    {
        var parts = WordDiff.Compare("Built a billing service in .NET", "Built a billing service in C# and .NET");
        Assert.Equal("Built a billing service in", parts[0].Text);
        Assert.Equal(DiffKind.Same, parts[0].Kind);
        Assert.Contains(parts, p => p.Kind == DiffKind.Added && p.Text == "C# and");
        Assert.DoesNotContain(parts, p => p.Kind == DiffKind.Removed);
    }

    [Fact]
    public void ShowsRemovedAndAddedWordsApart()
    {
        var parts = WordDiff.Compare("Helped cut latency by 63%", "Cut latency by 63%");
        Assert.Contains(parts, p => p.Kind == DiffKind.Removed && p.Text == "Helped cut");
        Assert.Contains(parts, p => p.Kind == DiffKind.Added && p.Text == "Cut");
    }

    [Fact]
    public void IdenticalTextIsOneSamePart() =>
        Assert.Equal([new DiffPart("a b c", DiffKind.Same)], WordDiff.Compare("a b c", "a  b c"));
}

public class TailorSessionTests
{
    private const string Resume = "Alex Rivera\n\n## Experience\n- Built a billing service\r\n  - Moved CI to GitHub Actions\n\nSkills: C#, SQL\n";

    [Fact]
    public void ListsTheBullets()
    {
        var s = new TailorSession(Resume);
        Assert.Equal(["Built a billing service", "Moved CI to GitHub Actions"], s.Bullets.Select(b => b.Text).ToArray());
    }

    [Fact]
    public void ReplacesOnlyTheChosenLineAndKeepsEverythingElse()
    {
        var s = new TailorSession(Resume);
        var line = s.Bullets[0].LineNumber;
        s.Apply(line, "Built a .NET billing service");

        Assert.Equal(Resume.Replace("Built a billing service", "Built a .NET billing service"), s.Text);
        Assert.True(s.HasChanges);
        Assert.Equal("Built a billing service", s.Changes.Single().Before);
        Assert.Equal("Built a .NET billing service", s.Changes.Single().After);
    }

    [Fact]
    public void KeepsTheIndentAndMarkerOfANestedBullet()
    {
        var s = new TailorSession(Resume);
        s.Apply(s.Bullets[1].LineNumber, "Moved CI/CD to GitHub Actions");
        Assert.Contains("\n  - Moved CI/CD to GitHub Actions\n", s.Text);
    }

    [Fact]
    public void UndoAndChangingBackRestoreTheOriginal()
    {
        var s = new TailorSession(Resume);
        var line = s.Bullets[0].LineNumber;
        s.Apply(line, "Something else");
        s.Undo(line);
        Assert.Equal(Resume, s.Text);
        Assert.False(s.HasChanges);

        s.Apply(line, "Something else");
        s.Apply(line, "Built a billing service");
        Assert.False(s.HasChanges);
        Assert.Equal(Resume, s.Text);
    }

    [Fact]
    public void ChangingALineTwiceKeepsTheFirstOriginal()
    {
        var s = new TailorSession(Resume);
        var line = s.Bullets[0].LineNumber;
        s.Apply(line, "First");
        s.Apply(line, "Second");
        Assert.Equal("Built a billing service", s.Changes.Single().Before);
        Assert.Equal("Second", s.Changes.Single().After);
        Assert.Equal(Resume, s.OriginalText);
    }

    [Fact]
    public void AResumeWithoutBulletMarksOffersItsSentences()
    {
        var s = new TailorSession("Experience\nBuilt and ran the claims billing service for four years\nSkills\nC#\n");
        Assert.Equal(["Built and ran the claims billing service for four years"], s.Bullets.Select(b => b.Text).ToArray());
    }
}

public class ResumeVersionTests
{
    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "jt-versions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "resume.md"), "original words");
        return folder;
    }

    [Fact]
    public void TheFirstSavedVersionIsV2AndTheyCountUp()
    {
        var folder = NewFolder();
        try
        {
            Assert.Empty(ResumeVersions.List(folder));
            Assert.Equal("resume.v2.md", ResumeVersions.SaveNew(folder, "a").FileName);
            Assert.Equal("resume.v3.md", ResumeVersions.SaveNew(folder, "b").FileName);
            Assert.Equal([2, 3], ResumeVersions.List(folder).Select(v => v.Number).ToArray());
            Assert.Equal("original words", File.ReadAllText(Path.Combine(folder, "resume.md")));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void UsingAVersionKeepsTheOldResumeAsAnotherVersion()
    {
        var folder = NewFolder();
        try
        {
            var v2 = ResumeVersions.SaveNew(folder, "tailored words");
            var backup = ResumeVersions.UseAsResume(folder, v2);

            Assert.Equal("tailored words", File.ReadAllText(Path.Combine(folder, "resume.md")));
            Assert.NotNull(backup);
            Assert.Equal("original words", ResumeVersions.Read(backup!));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void UsingTheVersionThatIsAlreadyTheResumeChangesNothing()
    {
        var folder = NewFolder();
        try
        {
            var v2 = ResumeVersions.SaveNew(folder, "original words");
            Assert.Null(ResumeVersions.UseAsResume(folder, v2));
            Assert.Single(ResumeVersions.List(folder));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void DoesNotDuplicateABackupThatAlreadyExists()
    {
        var folder = NewFolder();
        try
        {
            var kept = ResumeVersions.SaveNew(folder, "original words"); // already saved
            var other = ResumeVersions.SaveNew(folder, "tailored words");
            Assert.Null(ResumeVersions.UseAsResume(folder, other));
            Assert.Equal(2, ResumeVersions.List(folder).Count);
            Assert.NotNull(kept);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void IgnoresOtherFilesThatLookSimilar()
    {
        var folder = NewFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, "resume.vX.md"), "x");
            File.WriteAllText(Path.Combine(folder, "resume.v2.md.bak"), "x");
            Assert.Empty(ResumeVersions.List(folder));
        }
        finally { Directory.Delete(folder, true); }
    }
}

public class WordDiffPunctuationTests
{
    [Fact]
    public void ATrailingCommaDoesNotMakeAWordLookChanged()
    {
        var parts = WordDiff.Compare("Moved batch jobs to scheduled workers", "Moved batch jobs to scheduled workers, then to Kafka consumers");
        Assert.DoesNotContain(parts, p => p.Kind == DiffKind.Removed);
        Assert.Contains(parts, p => p.Kind == DiffKind.Added && p.Text == "then to Kafka consumers");
        Assert.Equal("Moved batch jobs to scheduled workers,", parts[0].Text);
    }
}
