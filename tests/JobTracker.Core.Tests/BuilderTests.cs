using JobTracker.Core.Builder;
using JobTracker.Core.Export;
using JobTracker.Core.Resumes;
using JobTracker.Core.Tailoring;

namespace JobTracker.Core.Tests;

internal static class Sample
{
    public static ResumeProfile Alex() => new()
    {
        Name = "Alex Rivera",
        Headline = "Senior Software Engineer",
        Email = "alex@example.com",
        Phone = "555-0100",
        Location = "Seattle, WA",
        Links = "linkedin.com/in/alexrivera-example",
        Summary = "Engineer who builds claims platforms and the teams that run them.",
        Jobs =
        [
            new ProfileJob
            {
                Company = "Contoso Insurance", Title = "Senior Engineer", Location = "Seattle, WA", Start = "Jan 2021", IsCurrent = true,
                Bullets = ["Cut SQL Server query latency by 63% by redesigning indexes", "Led four engineers moving nightly batch jobs to Kafka consumers"],
            },
            new ProfileJob
            {
                Company = "Fabrikam Systems", Title = "Engineer", Start = "2018", End = "2020",
                Bullets = ["Built a billing service that processes 40,000 invoices a day", "Moved CI to GitHub Actions, cutting release time from two hours to twelve minutes"],
            },
        ],
        Education = [new ProfileSchool { School = "State University", Degree = "B.S. Computer Science", Dates = "2014 – 2018", Details = "Dean's list" }],
        Skills = "Languages: C#, SQL, Python\nPlatforms: Azure, Kafka, Docker",
        Certifications = "Azure Developer Associate",
    };
}

public class ResumeProfileTextTests
{
    [Fact]
    public void LaysTheProfileOutAsResumeText()
    {
        var text = ResumeProfileText.Build(Sample.Alex());

        Assert.Equal(
            "# Alex Rivera\nSenior Software Engineer\nSeattle, WA | alex@example.com | 555-0100 | linkedin.com/in/alexrivera-example\n\n" +
            "## Summary\nEngineer who builds claims platforms and the teams that run them.\n\n" +
            "## Experience\nSenior Engineer, Contoso Insurance, Seattle, WA\nJan 2021 – Present\n" +
            "- Cut SQL Server query latency by 63% by redesigning indexes\n- Led four engineers moving nightly batch jobs to Kafka consumers\n\n" +
            "Engineer, Fabrikam Systems\n2018 – 2020\n- Built a billing service that processes 40,000 invoices a day\n- Moved CI to GitHub Actions, cutting release time from two hours to twelve minutes\n\n" +
            "## Skills\nLanguages: C#, SQL, Python\nPlatforms: Azure, Kafka, Docker\n\n" +
            "## Education\nB.S. Computer Science, State University\n2014 – 2018\nDean's list\n\n" +
            "## Certifications\n- Azure Developer Associate\n", text);
    }

    [Fact]
    public void LeavesOutWhatIsBlankAndNeverInventsAnything()
    {
        var text = ResumeProfileText.Build(new ResumeProfile { Name = "Sam Okafor", Jobs = [new ProfileJob(), new ProfileJob { Company = "Acme", Title = "Analyst" }] });
        Assert.Equal("# Sam Okafor\n\n## Experience\nAnalyst, Acme\n", text);
        Assert.Equal("", ResumeProfileText.Build(new ResumeProfile()));
    }

    [Fact]
    public void TidiesSpacesAndBulletMarksTheUserTyped()
    {
        var text = ResumeProfileText.Build(new ResumeProfile
        {
            Name = "  Sam   Okafor ",
            Jobs = [new ProfileJob { Company = "Acme", Title = "Analyst", Bullets = ["- Built   a thing  ", "", "• Ran another"] }],
        });
        Assert.Contains("# Sam Okafor", text);
        Assert.Contains("- Built a thing\n- Ran another\n", text);
    }

    [Fact]
    public void ReadsBackAsTheSameStructure()
    {
        var text = ResumeProfileText.Build(Sample.Alex());
        var jobs = ResumeStructure.Jobs(text);

        Assert.Equal(2, jobs.Count);
        Assert.Equal("Senior Engineer, Contoso Insurance, Seattle, WA · Jan 2021 – Present", jobs[0].Header);
        Assert.Equal(2, jobs[0].Bullets.Count);
        Assert.Equal("Engineer, Fabrikam Systems · 2018 – 2020", jobs[1].Header);
        Assert.Equal("Alex Rivera", ResumeStructure.PersonName(text));
    }

    [Fact]
    public void ADatelessCurrentJobSaysPresent() =>
        Assert.Contains("Present", ResumeProfileText.Build(new ResumeProfile { Jobs = [new ProfileJob { Company = "Acme", Title = "Analyst", IsCurrent = true }] }));
}

public class ResumeAdviceTests
{
    [Theory]
    [InlineData("I built a billing service for four teams", "“I”")]
    [InlineData("Responsible for the nightly batch jobs and their monitoring", "responsible for")]
    [InlineData("Worked on the claims API for the enrollment peak", "worked on")]
    public void SteersAwayFromWeakOpenings(string bullet, string mentions) =>
        Assert.Contains(mentions, ResumeAdvice.BulletTip(bullet), StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void FlagsALongBullet() =>
        Assert.Contains("words", ResumeAdvice.BulletTip(string.Join(' ', Enumerable.Repeat("word", 40))));

    [Fact]
    public void AsksForANumberOnAPlainBullet() =>
        Assert.Contains("number", ResumeAdvice.BulletTip("Built a billing service for the claims team"));

    [Theory]
    [InlineData("Cut SQL Server query latency by 63% by redesigning indexes")]
    [InlineData("Led four engineers")]
    [InlineData("Led four engineers moving nightly batch jobs to Kafka consumers")]
    [InlineData("")]
    public void SaysNothingWhenABulletReadsWell(string bullet) => Assert.Null(ResumeAdvice.BulletTip(bullet));

    [Fact]
    public void ACompleteResumeHasNothingToFix() =>
        Assert.Empty(ResumeAdvice.Problems(Sample.Alex()));

    [Fact]
    public void NamesWhatIsMissingInAnEmptyResume()
    {
        var problems = ResumeAdvice.Problems(new ResumeProfile());
        Assert.Contains(problems, p => p.Contains("name"));
        Assert.Contains(problems, p => p.Contains("email") || p.Contains("phone"));
        Assert.Contains(problems, p => p.Contains("job"));
        Assert.Contains(problems, p => p.Contains("skills"));
    }

    [Fact]
    public void SaysWhichJobNeedsWhat()
    {
        var profile = Sample.Alex();
        profile.Jobs[1].Bullets.Clear();
        profile.Jobs[1].End = "";
        profile.Jobs[0].Bullets.Add(profile.Jobs[0].Bullets[0]);

        var problems = ResumeAdvice.Problems(profile);
        Assert.Contains(problems, p => p.StartsWith("Fabrikam Systems") && p.Contains("bullets"));
        Assert.Contains(problems, p => p.StartsWith("Fabrikam Systems") && p.Contains("ended"));
        Assert.Contains(problems, p => p.StartsWith("Contoso Insurance") && p.Contains("same thing"));
    }

    [Fact]
    public void NoticesAnEndDateBeforeTheStart()
    {
        var profile = Sample.Alex();
        profile.Jobs[1].Start = "2021";
        profile.Jobs[1].End = "2019";
        Assert.Contains(ResumeAdvice.Problems(profile), p => p.Contains("before the start"));
    }
}

public class BulletComposerTests
{
    [Theory]
    [InlineData("built", "a billing service", "C# and SQL Server", "cutting invoice errors by 40%", "Built a billing service using C# and SQL Server, cutting invoice errors by 40%")]
    [InlineData("Led", "four engineers", "", "", "Led four engineers")]
    [InlineData("Cut", "report time.", "using Excel", "From 6 hours to 20 minutes", "Cut report time using Excel, from 6 hours to 20 minutes")]
    [InlineData("Reduced", "latency", "", "63% across the claims API", "Reduced latency, 63% across the claims API")]
    [InlineData("Migrated", "jobs to Kafka", "", "ETL jobs now run hourly", "Migrated jobs to Kafka, ETL jobs now run hourly")]
    public void PutsTheWordsTogetherWithoutAddingAny(string verb, string what, string tools, string result, string expected) =>
        Assert.Equal(expected, BulletComposer.Compose(verb, what, tools, result));

    [Theory]
    [InlineData("", "a thing")]
    [InlineData("Built", "")]
    [InlineData("  ", "  ")]
    public void NeedsAVerbAndAWhat(string verb, string what) => Assert.Equal("", BulletComposer.Compose(verb, what, "C#", "faster"));
}

public class ProfileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jt-builder-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Fact]
    public void SavesAndLoadsTheResumeUnderTheDataFolder()
    {
        var store = new ProfileStore(_root);
        Assert.False(store.Exists);
        Assert.True(store.Load().IsEmpty);

        store.Save(Sample.Alex());

        Assert.True(store.Exists);
        Assert.True(File.Exists(Path.Combine(_root, ".jobtracker", "resume-builder", "profile.json")));
        var back = new ProfileStore(_root).Load();
        Assert.Equal(ResumeProfileText.Build(Sample.Alex()), ResumeProfileText.Build(back));
        Assert.Equal(2, back.Jobs.Count);
        Assert.True(back.Jobs[0].IsCurrent);
    }

    [Fact]
    public void ACorruptFileIsAnEmptyResumeNotACrash()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".jobtracker", "resume-builder"));
        File.WriteAllText(Path.Combine(_root, ".jobtracker", "resume-builder", "profile.json"), "{ not json");
        Assert.True(new ProfileStore(_root).Load().IsEmpty);
    }

    [Fact]
    public void MakesAWordDocumentThatReadsBackTheSame()
    {
        var store = new ProfileStore(_root);
        var export = store.MakeWord(Sample.Alex());

        Assert.Equal(store.WordPath, export.Path);
        Assert.Equal(DocxMethod.Generated, export.Method);
        Assert.True(export.Verified, string.Join(" | ", export.Differences));
        Assert.Empty(ResumeTextCompare.Differences(ResumeProfileText.Build(Sample.Alex()), ResumeFileReader.Read(export.Path).Text));

        var blocks = DocxPreviewModel.Read(export.Path);
        Assert.Equal(PreviewBlockKind.Title, blocks[0].Kind);
        Assert.Equal("Alex Rivera", blocks[0].Text);
        Assert.Equal(5, blocks.Count(b => b.Kind == PreviewBlockKind.Bullet)); // two bullets for each job, and the certification
    }

    [Fact]
    public void StartOverDeletesTheResumeAndItsWordFile()
    {
        var store = new ProfileStore(_root);
        store.Save(Sample.Alex());
        store.MakeWord(Sample.Alex());
        store.Delete();
        Assert.False(store.Exists);
        Assert.False(File.Exists(store.WordPath));
    }

    [Fact]
    public void ADownloadNameIsMadeFromTheName() =>
        Assert.Equal("Alex Rivera - Resume.docx", DownloadNames.Suggest(ResumeProfileText.Build(Sample.Alex()), ""));
}

public class MoverCarriesTheBuiltResumeTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "jt-move-builder-" + Guid.NewGuid().ToString("N"));
    private string Old => Path.Combine(_base, "old");
    private string New => Path.Combine(_base, "new");

    public void Dispose()
    {
        foreach (var f in Directory.Exists(_base) ? Directory.EnumerateFileSystemEntries(_base, "*", SearchOption.AllDirectories) : [])
            File.SetAttributes(f, File.GetAttributes(f) & ~FileAttributes.ReadOnly);
        if (Directory.Exists(_base))
            Directory.Delete(_base, true);
    }

    [Fact]
    public void CopiesItAlongWithTheApplicationsAndRemovesTheOriginalAfterwards()
    {
        new ApplicationStore(Old).Create(new NewApplication("Acme", "Engineer", "jd", "resume", null, new DateOnly(2026, 10, 1)));
        var store = new ProfileStore(Old);
        store.Save(Sample.Alex());
        store.MakeWord(Sample.Alex());

        var result = DataRootMover.CopyAll(Old, New);
        Assert.True(result.BuilderCopied);
        Assert.True(new ProfileStore(New).Exists);
        Assert.True(File.Exists(new ProfileStore(New).WordPath));
        Assert.True(new ProfileStore(Old).Exists); // the originals stay until the caller has switched over

        var warnings = DataRootMover.DeleteOriginals(Old, result);
        Assert.Empty(warnings);
        Assert.False(new ProfileStore(Old).Exists);
        Assert.Equal("Alex Rivera", new ProfileStore(New).Load().Name);
    }

    [Fact]
    public void MovesAResumeEvenWhenThereAreNoApplications()
    {
        new ProfileStore(Old).Save(Sample.Alex());
        var result = DataRootMover.CopyAll(Old, New);
        Assert.True(result.BuilderCopied);
        Assert.Empty(result.Applications);
        DataRootMover.DeleteOriginals(Old, result);
        Assert.True(new ProfileStore(New).Exists);
        Assert.False(new ProfileStore(Old).Exists);
    }

    [Fact]
    public void NeverOverwritesAResumeAlreadyInTheNewFolder()
    {
        new ProfileStore(Old).Save(Sample.Alex());
        new ProfileStore(New).Save(new ResumeProfile { Name = "Someone Else" });

        var result = DataRootMover.CopyAll(Old, New);
        Assert.False(result.BuilderCopied);
        Assert.True(result.BuilderSkipped);
        DataRootMover.DeleteOriginals(Old, result);

        Assert.Equal("Someone Else", new ProfileStore(New).Load().Name);
        Assert.Equal("Alex Rivera", new ProfileStore(Old).Load().Name); // nothing lost
    }

    [Fact]
    public void ARollbackLeavesNoResumeBehindInTheNewFolder()
    {
        // An application whose copy cannot be completed makes the whole move roll back, including the resume.
        new ProfileStore(Old).Save(Sample.Alex());
        Directory.CreateDirectory(New);
        File.WriteAllText(Path.Combine(New, "keep.txt"), "mine");

        var cts = new CancellationTokenSource();
        cts.Cancel();
        new ApplicationStore(Old).Create(new NewApplication("Acme", "Engineer", "jd", "resume", null, new DateOnly(2026, 10, 1)));
        Assert.Throws<OperationCanceledException>(() => DataRootMover.CopyAll(Old, New, ct: cts.Token));

        Assert.False(new ProfileStore(New).Exists);
        Assert.True(File.Exists(Path.Combine(New, "keep.txt")));
        Assert.True(new ProfileStore(Old).Exists);
    }
}
