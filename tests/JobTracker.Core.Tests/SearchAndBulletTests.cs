namespace JobTracker.Core.Tests;

public class SearchAndBulletTests
{
    private static (ApplicationStore Store, IReadOnlyList<JobApplication> Apps) Seed(string root)
    {
        var store = new ApplicationStore(root);
        store.Create(new NewApplication("Acme", "Backend", "Must know Kafka\nPostgres a plus", "- Built Kafka consumers\n- Led a team of 4",
            AppliedDate: new DateOnly(2026, 10, 1)));
        store.Create(new NewApplication("Globex", "Frontend", "React and TypeScript", "- Shipped React dashboards\n- Tuned Kafka lag alerts",
            AppliedDate: new DateOnly(2026, 10, 2)));
        return (store, store.List());
    }

    [Fact]
    public void Search_JdScope_OnlyReturnsJdLines()
    {
        using var temp = new TempDir();
        var (store, apps) = Seed(temp.Path);

        var hits = new KeywordSearchProvider(store).Search(apps, "kafka", SearchScope.Jd);

        var hit = Assert.Single(hits);
        Assert.Equal("Acme", hit.Application.Meta.Company);
        Assert.Equal(BulletSource.Jd, hit.Source);
        Assert.Equal("Must know Kafka", hit.Text);
        Assert.Equal(1, hit.LineNumber);
    }

    [Fact]
    public void Search_ResumeScope_FindsAcrossApplications()
    {
        using var temp = new TempDir();
        var (store, apps) = Seed(temp.Path);

        var hits = new KeywordSearchProvider(store).Search(apps, "KAFKA", SearchScope.Resume);

        Assert.Equal(2, hits.Count);
        Assert.All(hits, h => Assert.Equal(BulletSource.Resume, h.Source));
    }

    [Fact]
    public void Search_BothScope_ReturnsJdAndResumeHits()
    {
        using var temp = new TempDir();
        var (store, apps) = Seed(temp.Path);

        var hits = new KeywordSearchProvider(store).Search(apps, "kafka", SearchScope.Both);

        Assert.Equal(3, hits.Count);
    }

    [Fact]
    public void Search_MultipleTerms_RequiresAllInApplication()
    {
        using var temp = new TempDir();
        var (store, apps) = Seed(temp.Path);

        var hits = new KeywordSearchProvider(store).Search(apps, "kafka react", SearchScope.Both);

        Assert.All(hits, h => Assert.Equal("Globex", h.Application.Meta.Company));
        Assert.NotEmpty(hits);
    }

    [Fact]
    public void Search_BlankQuery_ReturnsNothing()
    {
        using var temp = new TempDir();
        var (store, apps) = Seed(temp.Path);

        Assert.Empty(new KeywordSearchProvider(store).Search(apps, "   ", SearchScope.Both));
    }

    [Theory]
    [InlineData("- Built APIs", "Built APIs")]
    [InlineData("  • Built APIs  ", "Built APIs")]
    [InlineData("* - Built APIs", "Built APIs")]
    [InlineData("Built APIs", "Built APIs")]
    public void BulletText_Clean_StripsMarkers(string input, string expected) =>
        Assert.Equal(expected, BulletText.Clean(input));

    [Fact]
    public void BulletText_Lines_SkipsBlanksButKeepsLineNumbers()
    {
        var lines = BulletText.Lines("first\r\n\r\n- third\n   \nfifth").ToList();

        Assert.Equal([(1, "first"), (3, "- third"), (5, "fifth")], lines);
    }

    [Fact]
    public void BulletStore_Toggle_StarsThenUnstars_AndPersists()
    {
        using var temp = new TempDir();
        var bullets = new BulletStore(temp.Path);
        const string folder = @"Acme\2026-10-01_Backend";

        Assert.False(bullets.IsStarred(folder, BulletSource.Resume, "- Built Kafka consumers"));

        Assert.True(bullets.Toggle(folder, "Acme", "Backend", BulletSource.Resume, "- Built Kafka consumers"));
        Assert.True(new BulletStore(temp.Path).IsStarred(folder, BulletSource.Resume, "•  built  kafka CONSUMERS"));
        var saved = Assert.Single(bullets.List());
        Assert.Equal("Built Kafka consumers", saved.Text);
        Assert.Equal("Acme", saved.Company);

        Assert.False(bullets.Toggle(folder, "Acme", "Backend", BulletSource.Resume, "- Built Kafka consumers"));
        Assert.Empty(bullets.List());
    }

    [Fact]
    public void BulletStore_SameTextInDifferentApplication_IsSeparateEntry()
    {
        using var temp = new TempDir();
        var bullets = new BulletStore(temp.Path);

        bullets.Toggle(@"Acme\a", "Acme", "Dev", BulletSource.Resume, "Led a team");
        bullets.Toggle(@"Globex\b", "Globex", "Dev", BulletSource.Resume, "Led a team");

        Assert.Equal(2, bullets.List().Count);
    }

    [Fact]
    public void BulletStore_Search_FiltersByKeywordAndSource()
    {
        using var temp = new TempDir();
        var bullets = new BulletStore(temp.Path);
        bullets.Toggle(@"Acme\a", "Acme", "Dev", BulletSource.Resume, "Built Kafka consumers");
        bullets.Toggle(@"Acme\a", "Acme", "Dev", BulletSource.Jd, "Kafka required");
        bullets.Toggle(@"Acme\a", "Acme", "Dev", BulletSource.Resume, "Led a team of 4");

        Assert.Equal(2, bullets.Search("kafka").Count);
        Assert.Single(bullets.Search("kafka", BulletSource.Jd));
        Assert.Equal(3, bullets.Search("acme").Count); // matches company too
        Assert.Empty(bullets.Search("golang"));
    }

    [Fact]
    public void BulletStore_Remove_DeletesById()
    {
        using var temp = new TempDir();
        var bullets = new BulletStore(temp.Path);
        bullets.Toggle(@"Acme\a", "Acme", "Dev", BulletSource.Resume, "Led a team");
        var id = bullets.List().Single().Id;

        bullets.Remove(id);

        Assert.Empty(bullets.List());
    }

    [Fact]
    public void AppSettings_RoundTrips()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "s", "settings.json");

        new AppSettings { DataRoot = @"D:\Jobs" }.Save(path);

        Assert.Equal(@"D:\Jobs", AppSettings.Load(path).DataRoot);
        Assert.Equal(AppSettings.DefaultDataRoot(), AppSettings.Load(Path.Combine(temp.Path, "missing.json")).DataRoot);
    }
}
