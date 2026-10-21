using JobTracker.Core;

namespace JobTracker.Core.Tests;

public class RemoveApplicationTests
{
    private static NewApplication Sample(string company = "Acme Corp", string role = "Senior Backend Engineer", int day = 7) =>
        new(company, role, "We need Kafka experience.\n- Build APIs", "- Built APIs in C#", null, new DateOnly(2026, 10, day));

    private static void Delete(string folder) => Directory.Delete(folder, recursive: true);

    [Fact]
    public void RemovesTheApplicationAndItsFoldersContents_AndTheEmptyCompanyFolder()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample());
        File.WriteAllText(Path.Combine(app.FolderPath, "extra file the person added.txt"), "x");
        File.WriteAllText(Path.Combine(app.FolderPath, "resume.v2.md"), "v2");

        store.Remove(app, Delete);

        Assert.False(Directory.Exists(app.FolderPath));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "Acme Corp")));
        Assert.Empty(store.List());
        Assert.True(Directory.Exists(temp.Path));
    }

    [Fact]
    public void AnotherApplicationOfTheSameCompanyIsNotTouched()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var gone = store.Create(Sample(role: "Data Analyst", day: 7));
        var kept = store.Create(Sample(role: "Platform Engineer", day: 8));

        store.Remove(gone, Delete);

        Assert.Equal([kept.FolderPath], store.List().Select(a => a.FolderPath).ToArray());
        Assert.True(File.Exists(Path.Combine(kept.FolderPath, "jd.md")));
        Assert.True(Directory.Exists(Path.Combine(temp.Path, "Acme Corp")));
    }

    [Fact]
    public void OnlyTheFolderIsPassedOn_ExactlyOnce()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample());
        var calls = new List<string>();

        store.Remove(app, f => { calls.Add(f); Delete(f); });

        Assert.Equal([app.FolderPath], calls.ToArray());
    }

    [Fact]
    public void StarredBulletsFromItStayInTheLibrary()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var library = new BulletStore(temp.Path);
        var app = store.Create(Sample());
        library.Toggle(store.RelativeFolder(app), app.Meta.Company, app.Meta.Role, BulletSource.Resume, "Built APIs in C#");

        store.Remove(app, Delete);

        Assert.Single(library.List());
    }

    [Fact]
    public void ADisposerThatLeavesTheFolderBehindIsReported()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample());

        var ex = Assert.Throws<IOException>(() => store.Remove(app, _ => { }));

        Assert.Contains("could not be removed", ex.Message);
        Assert.True(File.Exists(Path.Combine(app.FolderPath, "jd.md")));
    }

    [Fact]
    public void ADisposerThatFailsLeavesEverythingInPlace()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample());

        Assert.Throws<IOException>(() => store.Remove(app, _ => throw new IOException("in use")));

        Assert.Single(store.List());
        Assert.True(File.Exists(Path.Combine(app.FolderPath, "meta.json")));
    }

    [Fact]
    public void ACompanyFolderWithOtherThingsInItIsKept()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample());
        File.WriteAllText(Path.Combine(temp.Path, "Acme Corp", "offer letter.pdf"), "x");

        store.Remove(app, Delete);

        Assert.True(File.Exists(Path.Combine(temp.Path, "Acme Corp", "offer letter.pdf")));
    }

    public static TheoryData<string> NotApplicationFolders() => new()
    {
        "",                                  // the data folder itself
        "Acme Corp",                         // a company folder
        "Acme Corp\\2026-10-07_Senior Backend Engineer\\sub",   // deeper than an application
        ".jobtracker\\resume-builder",       // the app's own folder
        "..\\outside",                       // outside the data folder
        "Acme Corp\\..\\..\\outside",        // outside, by walking up
    };

    [Theory]
    [MemberData(nameof(NotApplicationFolders))]
    public void NothingThatIsNotAnApplicationFolderIsEverPassedOn(string relative)
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample());
        var target = Path.GetFullPath(Path.Combine(temp.Path, relative));
        Directory.CreateDirectory(target);
        var disposed = false;

        // even with a meta.json in it, a folder is refused unless it sits exactly at Root\Company\Application
        File.WriteAllText(Path.Combine(target, "meta.json"), "{}");
        var fake = new JobApplication { FolderPath = target, Meta = new ApplicationMeta { Company = "x", Role = "y" } };

        Assert.Throws<IOException>(() => store.Remove(fake, _ => disposed = true));

        Assert.False(disposed);
        Assert.True(Directory.Exists(app.FolderPath));
        Assert.True(Directory.Exists(target));
        Directory.Delete(target, recursive: true);
    }

    [Fact]
    public void AFolderWithoutMetaJsonIsRefused()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var folder = Path.Combine(temp.Path, "Acme Corp", "2026-10-07_notes only");
        Directory.CreateDirectory(folder);
        var fake = new JobApplication { FolderPath = folder, Meta = new ApplicationMeta { Company = "Acme Corp", Role = "x" } };

        Assert.Throws<IOException>(() => store.Remove(fake, _ => throw new InvalidOperationException("must not be reached")));
        Assert.True(Directory.Exists(folder));
    }
}