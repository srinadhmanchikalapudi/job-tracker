namespace JobTracker.Core.Tests;

public class ApplicationStoreTests
{
    private static NewApplication Sample(string company = "Acme Corp", string role = "Senior Backend Engineer") =>
        new(company, role, "We need Kafka experience.\n- Build APIs", "- Built APIs in C#\n- Tuned SQL queries",
            "https://acme.example/jobs/1", new DateOnly(2026, 10, 7));

    [Fact]
    public void Create_WritesExpectedFolderAndFiles()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);

        var app = store.Create(Sample());

        var expected = Path.Combine(temp.Path, "Acme Corp", "2026-10-07_Senior Backend Engineer");
        Assert.Equal(expected, app.FolderPath);
        foreach (var file in new[] { "jd.md", "resume.md", "notes.md", "meta.json" })
            Assert.True(File.Exists(Path.Combine(expected, file)), file);
    }

    [Fact]
    public void Create_SameCompanyDateRole_GetsCounterSuffix()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);

        var first = store.Create(Sample());
        var second = store.Create(Sample());

        Assert.NotEqual(first.FolderPath, second.FolderPath);
        Assert.EndsWith("(2)", second.FolderPath);
    }

    [Fact]
    public void List_RoundTripsMetaAndText()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        store.Create(Sample());

        var app = Assert.Single(store.List());

        Assert.Equal("Acme Corp", app.Meta.Company);
        Assert.Equal("Senior Backend Engineer", app.Meta.Role);
        Assert.Equal("https://acme.example/jobs/1", app.Meta.Url);
        Assert.Equal(new DateOnly(2026, 10, 7), app.Meta.AppliedDate);
        Assert.Equal(ApplicationStatus.Applied, app.Meta.Status);
        Assert.Contains("Kafka", store.ReadJd(app));
        Assert.Contains("Tuned SQL", store.ReadResume(app));
    }

    [Fact]
    public void List_SortsNewestFirst_AndIgnoresDataFolderAndBrokenMeta()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        store.Create(Sample("Old Co") with { AppliedDate = new DateOnly(2026, 1, 1) });
        store.Create(Sample("New Co") with { AppliedDate = new DateOnly(2026, 9, 1) });
        Directory.CreateDirectory(Path.Combine(temp.Path, ".jobtracker", "x"));
        var broken = Path.Combine(temp.Path, "Broken Co", "2026-01-01_Dev");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "meta.json"), "{ not json");

        var names = store.List().Select(a => a.Meta.Company).ToList();

        Assert.Equal(["New Co", "Old Co"], names);
    }

    [Fact]
    public void SaveMetaAndText_Persist()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample());

        app.Meta.Status = ApplicationStatus.Interviewing;
        app.Meta.Starred = true;
        store.SaveMeta(app);
        store.SaveNotes(app, "Phone screen Tuesday");
        store.SaveResume(app, "- New bullet");

        var reloaded = Assert.Single(store.List());
        Assert.Equal(ApplicationStatus.Interviewing, reloaded.Meta.Status);
        Assert.True(reloaded.Meta.Starred);
        Assert.Equal("Phone screen Tuesday", store.ReadNotes(reloaded));
        Assert.Equal("- New bullet", store.ReadResume(reloaded));
    }

    [Fact]
    public void Create_LeavesNoTempFilesBehind()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample());

        Assert.Empty(Directory.GetFiles(app.FolderPath, "*.tmp"));
    }
}
