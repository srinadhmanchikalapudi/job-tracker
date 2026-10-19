namespace JobTracker.Core.Tests;

public class JobTypeDetectionTests
{
    private static JobType? Detect(string page) => JdExtractor.Extract(page).JobType;

    [Theory]
    [InlineData("Backend Engineer\nJob Type: Contract\nWe build things.")]
    [InlineData("Backend Engineer\nEmployment Type: Contract to hire")]
    [InlineData("Backend Engineer\nJob type - C2C")]
    [InlineData("Backend Engineer\nJob Type\nContract")] // the label alone on a line, as Indeed shows it
    [InlineData("Backend Engineer\nUmbrella Health · Seattle, WA (Hybrid) · Contract · Mid-Senior level")]
    [InlineData("Backend Engineer\nThis is a 6 month contract with a chance to extend.")]
    [InlineData("Backend Engineer\nCorp-to-corp or W2 contract, no 1099 please.")]
    public void ContractPostings_AreRecognised(string page) => Assert.Equal(JobType.Contract, Detect(page));

    [Theory]
    [InlineData("Backend Engineer\nJob Type: Full-time\nBenefits included.")]
    [InlineData("Backend Engineer\nEmployment type: Permanent")]
    [InlineData("Backend Engineer\nUmbrella Health · Seattle, WA (Hybrid) · Full-time · Mid-Senior level")]
    [InlineData("Backend Engineer\nRemote · Full-time")]
    public void FullTimePostings_AreRecognised(string page) => Assert.Equal(JobType.FullTime, Detect(page));

    [Theory]
    [InlineData("Backend Engineer\nWe work with external contractors on some projects and are a growing team.")] // a passing mention is not evidence
    [InlineData("Backend Engineer\nBuild services in C#.")]
    [InlineData("Backend Engineer\nJob Type: Part-time")]
    [InlineData("Backend Engineer\nJob Type: Full-time, Contract")] // names both: do not guess
    [InlineData("Backend Engineer\nSeattle · Full-time · Contract")]
    [InlineData("")]
    public void WhenUnclear_NothingIsGuessed(string page) => Assert.Null(Detect(page));

    [Fact]
    public void AJobTypeLabel_LowerInThePage_IsStillFound()
    {
        var filler = string.Join('\n', Enumerable.Range(1, 120).Select(i => $"Responsibility number {i} for the role."));

        Assert.Equal(JobType.Contract, Detect($"Backend Engineer\n{filler}\nJob Type: Contract"));
    }

    [Fact]
    public void ContractWordsLowInTheBody_WithoutALabel_AreNotEvidence()
    {
        var filler = string.Join('\n', Enumerable.Range(1, 120).Select(i => $"Responsibility number {i} for the role."));

        Assert.Null(Detect($"Backend Engineer\n{filler}\nOur sister company hires on a 6 month contract basis."));
    }

    [Fact]
    public void ExtractedJob_StillCarriesCompanyRoleAndLink()
    {
        var result = JdExtractor.Extract("Job Title: QA Engineer\nCompany: Acme\nJob Type: Contract\nhttps://acme.example/jobs/1");

        Assert.Equal("Acme", result.Company);
        Assert.Equal("QA Engineer", result.Role);
        Assert.Equal("https://acme.example/jobs/1", result.Url);
        Assert.Equal(JobType.Contract, result.JobType);
    }
}

public class JobTypeStorageTests
{
    [Fact]
    public void NewApplications_DefaultToFullTime()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);

        var app = store.Create(new NewApplication("Acme", "Dev", "jd", "resume"));

        Assert.Equal(JobType.FullTime, app.Meta.JobType);
    }

    [Fact]
    public void TheTypeIsSavedWithTheApplication_AndCanBeChanged()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(new NewApplication("Acme", "Dev", "jd", "resume", JobType: JobType.Contract));
        Assert.Equal(JobType.Contract, Assert.Single(store.List()).Meta.JobType);

        app.Meta.JobType = JobType.FullTime;
        store.SaveMeta(app);

        Assert.Equal(JobType.FullTime, Assert.Single(store.List()).Meta.JobType);
    }

    [Fact]
    public void TheTypeIsWrittenAsAReadableWord()
    {
        using var temp = new TempDir();
        var app = new ApplicationStore(temp.Path).Create(new NewApplication("Acme", "Dev", "jd", "resume", JobType: JobType.Contract));

        Assert.Contains("\"JobType\": \"Contract\"", File.ReadAllText(Path.Combine(app.FolderPath, "meta.json")));
    }

    [Fact]
    public void ApplicationsSavedBeforeTheTypeExisted_AreFullTime()
    {
        using var temp = new TempDir();
        var folder = Path.Combine(temp.Path, "Acme", "2026-09-01_Dev");
        Directory.CreateDirectory(folder);
        // meta.json exactly as version 1.0 and 1.1 wrote it: no JobType at all.
        File.WriteAllText(Path.Combine(folder, "meta.json"),
            "{ \"Company\": \"Acme\", \"Role\": \"Dev\", \"AppliedDate\": \"2026-09-01\", \"Status\": \"Interviewing\", \"Starred\": true }");

        var app = Assert.Single(new ApplicationStore(temp.Path).List());

        Assert.Equal(JobType.FullTime, app.Meta.JobType);
        Assert.Equal(ApplicationStatus.Interviewing, app.Meta.Status);
        Assert.True(app.Meta.Starred);
    }

    [Fact]
    public void MovingTheFolder_KeepsEachApplicationsType()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var store = new ApplicationStore(oldRoot);
        store.Create(new NewApplication("Acme", "Dev", "jd", "resume", JobType: JobType.Contract));
        store.Create(new NewApplication("Globex", "Dev", "jd", "resume", JobType: JobType.FullTime));

        DataRootMover.CopyAll(oldRoot, Path.Combine(temp.Path, "new"));

        var moved = new ApplicationStore(Path.Combine(temp.Path, "new")).List().ToDictionary(a => a.Meta.Company, a => a.Meta.JobType);
        Assert.Equal(JobType.Contract, moved["Acme"]);
        Assert.Equal(JobType.FullTime, moved["Globex"]);
    }

    [Fact]
    public void Labels_AreReadable()
    {
        Assert.Equal("Full-time", JobType.FullTime.Label());
        Assert.Equal("Contract", JobType.Contract.Label());
    }
}
