namespace JobTracker.Core.Tests;

public class DataRootMoverTests
{
    private static JobApplication Add(ApplicationStore store, string company, string role, string date = "2026-10-07") =>
        store.Create(new NewApplication(company, role, "JD for " + role, "- Resume line for " + role, AppliedDate: DateOnly.Parse(date)));

    [Fact]
    public void Validate_RejectsSameNestedAndEmptyFolders()
    {
        using var temp = new TempDir();
        var old = Path.Combine(temp.Path, "jobs");

        Assert.NotNull(DataRootMover.Validate(old, ""));
        Assert.Contains("already using", DataRootMover.Validate(old, old + @"\"));
        Assert.Contains("already using", DataRootMover.Validate(old, old.ToUpperInvariant()));
        Assert.Contains("inside the current one", DataRootMover.Validate(old, Path.Combine(old, "sub")));
        Assert.Contains("inside the new one", DataRootMover.Validate(Path.Combine(temp.Path, "a", "jobs"), Path.Combine(temp.Path, "a")));
        Assert.Null(DataRootMover.Validate(old, Path.Combine(temp.Path, "elsewhere")));
        Assert.Null(DataRootMover.Validate(old, Path.Combine(temp.Path, "jobs2"))); // a sibling that merely starts with the same letters
    }

    [Fact]
    public void CopyAll_CopiesEveryApplicationWithItsFiles_AndLeavesTheOriginals()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var newRoot = Path.Combine(temp.Path, "new");
        var store = new ApplicationStore(oldRoot);
        var acme = Add(store, "Acme", "Backend");
        Add(store, "Globex", "Frontend");
        // The user dropped extra files into an application folder: they must travel too.
        File.WriteAllText(Path.Combine(acme.FolderPath, "offer-letter.txt"), "offer");
        Directory.CreateDirectory(Path.Combine(acme.FolderPath, "attachments"));
        File.WriteAllText(Path.Combine(acme.FolderPath, "attachments", "portfolio.txt"), "work");

        var result = DataRootMover.CopyAll(oldRoot, newRoot);

        Assert.Equal(2, result.Applications.Count);
        var copied = new ApplicationStore(newRoot).List();
        Assert.Equal(2, copied.Count);
        var acmeCopy = copied.Single(a => a.Meta.Company == "Acme");
        Assert.Equal("offer", File.ReadAllText(Path.Combine(acmeCopy.FolderPath, "offer-letter.txt")));
        Assert.Equal("work", File.ReadAllText(Path.Combine(acmeCopy.FolderPath, "attachments", "portfolio.txt")));
        Assert.Contains("JD for Backend", new ApplicationStore(newRoot).ReadJd(acmeCopy));

        Assert.Equal(2, store.List().Count); // the originals are still there until DeleteOriginals
        Assert.True(result.FilesCopied >= 10);
        Assert.True(result.BytesCopied > 0);
    }

    [Fact]
    public void CopyAll_ReportsProgress()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(Path.Combine(temp.Path, "old"));
        Add(store, "Acme", "A");
        Add(store, "Globex", "B");
        var reports = new List<MoveProgress>();

        DataRootMover.CopyAll(store.Root, Path.Combine(temp.Path, "new"), new SyncProgress(reports.Add));

        Assert.Equal(2, reports[^1].Done);
        Assert.Equal(2, reports[^1].Total);
        Assert.Contains(reports, r => r.Current.StartsWith("Acme"));
    }

    private sealed class SyncProgress(Action<MoveProgress> report) : IProgress<MoveProgress>
    {
        public void Report(MoveProgress value) => report(value);
    }

    [Fact]
    public void CopyAll_CarriesStarredBullets_ToTheNewFolder()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var newRoot = Path.Combine(temp.Path, "new");
        var store = new ApplicationStore(oldRoot);
        var acme = Add(store, "Acme", "Backend");
        new BulletStore(oldRoot).Toggle(store.RelativeFolder(acme), "Acme", "Backend", BulletSource.Resume, "- Built Kafka consumers");

        DataRootMover.CopyAll(oldRoot, newRoot);

        var bullet = Assert.Single(new BulletStore(newRoot).List());
        Assert.Equal("Built Kafka consumers", bullet.Text);
        Assert.Equal(store.RelativeFolder(acme), bullet.AppFolder);
    }

    [Fact]
    public void CopyAll_WhenTheNameIsTaken_RenamesTheCopy_AndPointsTheBulletAtIt()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var newRoot = Path.Combine(temp.Path, "new");
        var oldStore = new ApplicationStore(oldRoot);
        var newStore = new ApplicationStore(newRoot);
        var incoming = Add(oldStore, "Acme", "Backend");
        var existing = Add(newStore, "Acme", "Backend"); // the same company, date and role already lives in the new folder
        var rel = oldStore.RelativeFolder(incoming);
        new BulletStore(oldRoot).Toggle(rel, "Acme", "Backend", BulletSource.Resume, "- Led a team of 4");
        new BulletStore(newRoot).Toggle(newStore.RelativeFolder(existing), "Acme", "Backend", BulletSource.Resume, "- Existing bullet");

        var result = DataRootMover.CopyAll(oldRoot, newRoot);

        var moved = Assert.Single(result.Applications);
        Assert.EndsWith("(2)", moved.NewRelative);
        Assert.Equal(2, newStore.List().Count); // nothing was overwritten
        var bullets = new BulletStore(newRoot).List();
        Assert.Equal(2, bullets.Count);
        Assert.Equal(moved.NewRelative, bullets.Single(b => b.Text == "Led a team of 4").AppFolder);
        Assert.Equal(newStore.RelativeFolder(existing), bullets.Single(b => b.Text == "Existing bullet").AppFolder);
    }

    [Fact]
    public void CopyAll_DoesNotDuplicateABulletThatIsAlreadyInTheNewFolder()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var newRoot = Path.Combine(temp.Path, "new");
        var oldStore = new ApplicationStore(oldRoot);
        var app = Add(oldStore, "Acme", "Backend");
        var rel = oldStore.RelativeFolder(app);
        new BulletStore(oldRoot).Toggle(rel, "Acme", "Backend", BulletSource.Resume, "- Same line");
        DataRootMover.CopyAll(oldRoot, newRoot);

        // Moving the same data again into a folder that already has it must not double the library.
        new BulletStore(newRoot).MergeFrom(new BulletStore(oldRoot).List(), new Dictionary<string, string> { [rel] = rel });

        Assert.Single(new BulletStore(newRoot).List());
    }

    [Fact]
    public void DeleteOriginals_RemovesMovedApplications_EmptyCompanyFolders_AndMovedBullets()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var store = new ApplicationStore(oldRoot);
        var acme = Add(store, "Acme", "Backend");
        new BulletStore(oldRoot).Toggle(store.RelativeFolder(acme), "Acme", "Backend", BulletSource.Resume, "- A line");
        var result = DataRootMover.CopyAll(oldRoot, Path.Combine(temp.Path, "new"));

        var warnings = DataRootMover.DeleteOriginals(oldRoot, result);

        Assert.Empty(warnings);
        Assert.Empty(store.List());
        Assert.False(Directory.Exists(Path.Combine(oldRoot, "Acme")));
        Assert.False(Directory.Exists(Path.Combine(oldRoot, ApplicationStore.AppDataFolder)));
    }

    [Fact]
    public void DeleteOriginals_KeepsThingsThatWereNotMoved()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var store = new ApplicationStore(oldRoot);
        var acme = Add(store, "Acme", "Backend");
        File.WriteAllText(Path.Combine(oldRoot, "Acme", "my-notes.txt"), "not an application"); // a stray file in the company folder
        var broken = Path.Combine(oldRoot, "Broken Co", "2026-01-01_Dev"); // an application folder with a damaged meta.json
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "meta.json"), "{ not json");
        new BulletStore(oldRoot).Toggle("Broken Co\\2026-01-01_Dev", "Broken Co", "Dev", BulletSource.Jd, "Kept bullet");
        var result = DataRootMover.CopyAll(oldRoot, Path.Combine(temp.Path, "new"));

        DataRootMover.DeleteOriginals(oldRoot, result);

        Assert.False(Directory.Exists(acme.FolderPath));
        Assert.True(File.Exists(Path.Combine(oldRoot, "Acme", "my-notes.txt"))); // the company folder is not empty, so it stays
        Assert.True(File.Exists(Path.Combine(broken, "meta.json")));
        Assert.Equal("Kept bullet", Assert.Single(new BulletStore(oldRoot).List()).Text);
        Assert.Empty(new BulletStore(Path.Combine(temp.Path, "new")).List()); // that bullet's application was not moved
    }

    [Fact]
    public void DeleteOriginals_RemovesReadOnlyFiles()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var store = new ApplicationStore(oldRoot);
        var acme = Add(store, "Acme", "Backend");
        File.SetAttributes(Path.Combine(acme.FolderPath, "resume.md"), FileAttributes.ReadOnly);
        var result = DataRootMover.CopyAll(oldRoot, Path.Combine(temp.Path, "new"));

        var warnings = DataRootMover.DeleteOriginals(oldRoot, result);

        Assert.Empty(warnings);
        Assert.False(Directory.Exists(acme.FolderPath));
    }

    [Fact]
    public void CopyAll_WhenAFileCannotBeCopied_RollsBackAndLeavesEverythingAsItWas()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var newRoot = Path.Combine(temp.Path, "new");
        var store = new ApplicationStore(oldRoot);
        Add(store, "Acme", "Backend");
        var globex = Add(store, "Globex", "Frontend");
        new BulletStore(oldRoot).Toggle(store.RelativeFolder(globex), "Globex", "Frontend", BulletSource.Resume, "- A line");

        // The destination already holds the user's own data, including a bullets file that must come through unchanged.
        var existing = new ApplicationStore(newRoot);
        Add(existing, "Initech", "Dev", "2026-01-01");
        new BulletStore(newRoot).Toggle(existing.RelativeFolder(existing.List()[0]), "Initech", "Dev", BulletSource.Jd, "Existing");
        var bulletsBefore = File.ReadAllText(Path.Combine(newRoot, ".jobtracker", "bullets.json"));

        // Lock a file in the second application so copying it fails after the first one was already copied.
        using var held = new FileStream(Path.Combine(globex.FolderPath, "resume.md"), FileMode.Open, FileAccess.Read, FileShare.None);

        var ex = Assert.Throws<DataRootException>(() => DataRootMover.CopyAll(oldRoot, newRoot));

        Assert.Contains("nothing was changed", ex.Message);
        var survivors = new ApplicationStore(newRoot).List();
        Assert.Equal("Initech", Assert.Single(survivors).Meta.Company); // no half-copied Acme or Globex left behind
        Assert.False(Directory.Exists(Path.Combine(newRoot, "Acme")));
        Assert.False(Directory.Exists(Path.Combine(newRoot, "Globex")));
        Assert.Equal(bulletsBefore, File.ReadAllText(Path.Combine(newRoot, ".jobtracker", "bullets.json")));
        Assert.Equal(2, store.List().Count); // the originals are untouched
    }

    [Fact]
    public void CopyAll_WhenTheNewFolderDidNotExist_AndItFails_RemovesIt()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var newRoot = Path.Combine(temp.Path, "brand-new");
        var store = new ApplicationStore(oldRoot);
        var acme = Add(store, "Acme", "Backend");
        using var held = new FileStream(Path.Combine(acme.FolderPath, "jd.md"), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Throws<DataRootException>(() => DataRootMover.CopyAll(oldRoot, newRoot));

        Assert.False(Directory.Exists(newRoot));
    }

    [Fact]
    public void CopyAll_Cancelled_RollsBack()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        var newRoot = Path.Combine(temp.Path, "new");
        Add(new ApplicationStore(oldRoot), "Acme", "Backend");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => DataRootMover.CopyAll(oldRoot, newRoot, ct: cts.Token));

        Assert.False(Directory.Exists(newRoot));
    }

    [Fact]
    public void CopyAll_WithNothingToMove_StillSucceeds()
    {
        using var temp = new TempDir();

        var result = DataRootMover.CopyAll(Path.Combine(temp.Path, "old"), Path.Combine(temp.Path, "new"));

        Assert.Empty(result.Applications);
    }

    [Fact]
    public void CopyAll_RefusesAnInvalidDestination()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");

        Assert.Throws<DataRootException>(() => DataRootMover.CopyAll(oldRoot, oldRoot));
        Assert.Throws<DataRootException>(() => DataRootMover.CopyAll(oldRoot, Path.Combine(oldRoot, "inside")));
    }
}

public class DataRootAdvisorTests
{
    [Fact]
    public void SuggestsASubfolder_OnlyForANonEmptyFolderThatIsNotAJobTrackerFolder()
    {
        using var temp = new TempDir();
        var empty = Path.Combine(temp.Path, "empty");
        Directory.CreateDirectory(empty);
        var cluttered = Path.Combine(temp.Path, "documents");
        Directory.CreateDirectory(cluttered);
        File.WriteAllText(Path.Combine(cluttered, "taxes.pdf"), "x");
        var ours = Path.Combine(temp.Path, "ours");
        new ApplicationStore(ours).Create(new NewApplication("Acme", "Dev", "jd", "resume"));
        var withMarker = Path.Combine(temp.Path, "marker");
        Directory.CreateDirectory(Path.Combine(withMarker, ".jobtracker"));
        File.WriteAllText(Path.Combine(withMarker, "other.txt"), "x");

        Assert.False(DataRootAdvisor.ShouldSuggestSubfolder(Path.Combine(temp.Path, "does-not-exist")));
        Assert.False(DataRootAdvisor.ShouldSuggestSubfolder(empty));
        Assert.True(DataRootAdvisor.ShouldSuggestSubfolder(cluttered));
        Assert.False(DataRootAdvisor.ShouldSuggestSubfolder(ours));
        Assert.False(DataRootAdvisor.ShouldSuggestSubfolder(withMarker));
        Assert.Equal(Path.Combine(cluttered, "JobApplications"), DataRootAdvisor.Suggested(cluttered));
    }
}

public class InstallDefaultsTests
{
    [Fact]
    public void FirstRun_UsesTheFolderChosenInTheInstaller()
    {
        using var temp = new TempDir();
        var defaults = Path.Combine(temp.Path, "install-defaults.json");
        File.WriteAllText(defaults, """{ "DataRoot": "D:\\My Jobs" }""");

        var settings = AppSettings.Load(Path.Combine(temp.Path, "no-settings-yet.json"), defaults);

        Assert.Equal(@"D:\My Jobs", settings.DataRoot);
    }

    [Fact]
    public void ExistingSettings_WinOverTheInstallerChoice()
    {
        using var temp = new TempDir();
        var defaults = Path.Combine(temp.Path, "install-defaults.json");
        File.WriteAllText(defaults, """{ "DataRoot": "D:\\From Installer" }""");
        var settingsFile = Path.Combine(temp.Path, "settings.json");
        new AppSettings { DataRoot = @"E:\Chosen In The App" }.Save(settingsFile);

        Assert.Equal(@"E:\Chosen In The App", AppSettings.Load(settingsFile, defaults).DataRoot);
    }

    [Fact]
    public void NoInstallerChoice_FallsBackToTheDesktopFolder()
    {
        using var temp = new TempDir();

        var settings = AppSettings.Load(Path.Combine(temp.Path, "none.json"), Path.Combine(temp.Path, "none-either.json"));

        Assert.Equal(AppSettings.DefaultDataRoot(), settings.DataRoot);
    }

    [Fact]
    public void EnvironmentVariablesInTheInstallerChoice_AreExpanded()
    {
        using var temp = new TempDir();
        var defaults = Path.Combine(temp.Path, "install-defaults.json");
        File.WriteAllText(defaults, "{ \"DataRoot\": \"%USERPROFILE%\\\\Documents\\\\Jobs\" }");

        var settings = AppSettings.Load(Path.Combine(temp.Path, "none.json"), defaults);

        Assert.Equal(Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE")!, "Documents", "Jobs"), settings.DataRoot);
    }

    [Fact]
    public void AnInstallerFileWithOtherThingsInIt_IsStillRead()
    {
        using var temp = new TempDir();
        var defaults = Path.Combine(temp.Path, "install-defaults.json");
        File.WriteAllText(defaults, "\uFEFF{ \"DataRoot\": \"C:\\\\Users\\\\Someone\\\\Jobs\", \"Unknown\": 1 }"); // with a byte order mark, as the installer writes it

        var settings = AppSettings.Load(Path.Combine(temp.Path, "none.json"), defaults);

        Assert.Equal(@"C:\Users\Someone\Jobs", settings.DataRoot);
    }
}
