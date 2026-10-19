using System.Net;
using System.Text;
using JobTracker.Core.Updates;

namespace JobTracker.Core.Tests;

internal sealed class FakeFolders(string documents, string desktop) : IKnownFolders
{
    public string Documents { get; } = documents;
    public string Desktop { get; } = desktop;
}

public class OneDriveLocatorTests
{
    private static OneDriveLocator Locator(string root, bool running = true, string name = "OneDrive") =>
        new(() => [new OneDriveFolder(root, name)], () => running);

    [Fact]
    public void ApathInsideTheOneDriveFolder_IsRecognised_OtherPathsAreNot()
    {
        var locator = Locator(@"C:\Users\Pat\OneDrive");

        Assert.True(locator.IsInOneDrive(@"C:\Users\Pat\OneDrive\Documents\JobApplications"));
        Assert.True(locator.IsInOneDrive(@"C:\Users\Pat\OneDrive")); // the folder itself
        Assert.True(locator.IsInOneDrive(@"c:\users\pat\onedrive\Desktop")); // case does not matter
        Assert.True(locator.IsInOneDrive(@"C:\Users\Pat\OneDrive\"));
        Assert.False(locator.IsInOneDrive(@"C:\Users\Pat\Documents\JobApplications"));
        Assert.False(locator.IsInOneDrive(@"C:\Users\Pat\OneDrive2\Documents")); // a sibling that merely starts with the same letters
        Assert.False(locator.IsInOneDrive(@"D:\Jobs"));
        Assert.False(locator.IsInOneDrive(""));
    }

    [Fact]
    public void TheMostSpecificOneDriveFolderWins()
    {
        var locator = new OneDriveLocator(() =>
            [new OneDriveFolder(@"C:\Users\Pat\OneDrive", "OneDrive"), new OneDriveFolder(@"C:\Users\Pat\OneDrive\Work", "OneDrive (work or school)")], () => true);

        Assert.Equal("OneDrive (work or school)", locator.Containing(@"C:\Users\Pat\OneDrive\Work\Docs")!.Name);
        Assert.Equal("OneDrive", locator.Containing(@"C:\Users\Pat\OneDrive\Other")!.Name);
    }

    [Fact]
    public void WithNoOneDrive_NothingIsInOneDrive()
    {
        var locator = new OneDriveLocator(() => [], () => false);

        Assert.False(locator.IsInOneDrive(@"C:\Users\Pat\Documents"));
    }

    [Fact]
    public void RealDetection_DoesNotThrow()
    {
        // Whatever this PC has (OneDrive or not), asking must be safe.
        var locator = new OneDriveLocator();

        _ = locator.Folders();
        _ = locator.IsRunning();
    }
}

public class RecommendedFolderTests
{
    private static OneDriveLocator OneDriveAt(string root) => new(() => [new OneDriveFolder(root, "OneDrive")], () => true);
    private static readonly OneDriveLocator NoOneDrive = new(() => [], () => false);

    [Fact]
    public void WhenDocumentsIsInOneDrive_AndNothingExistsYet_DocumentsIsRecommended()
    {
        using var temp = new TempDir();
        var oneDrive = Path.Combine(temp.Path, "OneDrive");
        var known = new FakeFolders(Path.Combine(oneDrive, "Documents"), Path.Combine(oneDrive, "Desktop"));

        Assert.Equal(Path.Combine(oneDrive, "Documents", "JobApplications"), DataLocations.Recommended(known, OneDriveAt(oneDrive)));
    }

    [Fact]
    public void WhenDocumentsIsNotInOneDrive_TheDesktopRemainsTheDefault()
    {
        using var temp = new TempDir();
        var known = new FakeFolders(Path.Combine(temp.Path, "Documents"), Path.Combine(temp.Path, "Desktop"));

        Assert.Equal(Path.Combine(temp.Path, "Desktop", "JobApplications"), DataLocations.Recommended(known, NoOneDrive));
        Assert.Equal(Path.Combine(temp.Path, "Desktop", "JobApplications"), DataLocations.Recommended(known, OneDriveAt(Path.Combine(temp.Path, "Elsewhere"))));
    }

    [Fact]
    public void ApplicationsThatAlreadyExist_AreNeverLeftBehind()
    {
        using var temp = new TempDir();
        var oneDrive = Path.Combine(temp.Path, "OneDrive");
        var known = new FakeFolders(Path.Combine(oneDrive, "Documents"), Path.Combine(oneDrive, "Desktop"));
        // Someone who used version 1.x has applications on the Desktop; Documents is in OneDrive but empty.
        var desktopRoot = Path.Combine(known.Desktop, "JobApplications");
        new ApplicationStore(desktopRoot).Create(new NewApplication("Acme", "Dev", "jd", "resume"));

        Assert.Equal(desktopRoot, DataLocations.Recommended(known, OneDriveAt(oneDrive)));
    }

    [Fact]
    public void AfterAReset_TheRestoredApplicationsInDocumentsAreFoundAgain()
    {
        using var temp = new TempDir();
        var known = new FakeFolders(Path.Combine(temp.Path, "Documents"), Path.Combine(temp.Path, "Desktop"));
        var documentsRoot = Path.Combine(known.Documents, "JobApplications");
        new ApplicationStore(documentsRoot).Create(new NewApplication("Acme", "Dev", "jd", "resume")); // OneDrive brought it back

        // Even with no OneDrive information at all (not signed in yet), the folder that holds applications is chosen.
        Assert.Equal(documentsRoot, DataLocations.Recommended(known, NoOneDrive));
    }

    [Fact]
    public void WhenBothHoldApplications_DocumentsWins()
    {
        using var temp = new TempDir();
        var known = new FakeFolders(Path.Combine(temp.Path, "Documents"), Path.Combine(temp.Path, "Desktop"));
        new ApplicationStore(Path.Combine(known.Documents, "JobApplications")).Create(new NewApplication("A", "Dev", "jd", "resume"));
        new ApplicationStore(Path.Combine(known.Desktop, "JobApplications")).Create(new NewApplication("B", "Dev", "jd", "resume"));

        Assert.Equal(Path.Combine(known.Documents, "JobApplications"), DataLocations.Recommended(known, NoOneDrive));
    }

    [Fact]
    public void TheDefaultIsWhereAReinstallLooks()
    {
        var known = new FakeFolders(@"C:\Users\Pat\OneDrive\Documents", @"C:\Users\Pat\OneDrive\Desktop");

        Assert.True(DataLocations.IsWhereTheDefaultLooks(@"C:\Users\Pat\OneDrive\Documents\JobApplications", known));
        Assert.True(DataLocations.IsWhereTheDefaultLooks(@"c:\users\pat\onedrive\desktop\jobapplications\", known));
        Assert.False(DataLocations.IsWhereTheDefaultLooks(@"C:\Users\Pat\OneDrive\MyJobs", known));
    }
}

public class BackupAdvisorTests
{
    private static readonly FakeFolders Known = new(@"C:\Users\Pat\OneDrive\Documents", @"C:\Users\Pat\OneDrive\Desktop");

    private static OneDriveLocator Running(string root) => new(() => [new OneDriveFolder(root, "OneDrive")], () => true);

    [Fact]
    public void InsideOneDrive_AndRunning_IsBackedUp_AndSaysItIsFoundAgainAutomatically()
    {
        var status = BackupAdvisor.Evaluate(@"C:\Users\Pat\OneDrive\Documents\JobApplications", Known, Running(@"C:\Users\Pat\OneDrive"));

        Assert.Equal(BackupKind.InOneDrive, status.Kind);
        Assert.Contains("Backed up", status.Headline);
        Assert.Contains("finds them again by itself", status.Detail);
        Assert.Null(status.SuggestedFolder);
    }

    [Fact]
    public void InsideOneDrive_ButInACustomFolder_TellsThePersonToChooseItAgain()
    {
        var status = BackupAdvisor.Evaluate(@"C:\Users\Pat\OneDrive\Careers\Jobs", Known, Running(@"C:\Users\Pat\OneDrive"));

        Assert.Equal(BackupKind.InOneDrive, status.Kind);
        Assert.Contains("choose this folder again", status.Detail);
    }

    [Fact]
    public void InsideOneDrive_ButOneDriveIsNotRunning_Warns()
    {
        var oneDrive = new OneDriveLocator(() => [new OneDriveFolder(@"C:\Users\Pat\OneDrive", "OneDrive")], () => false);

        var status = BackupAdvisor.Evaluate(@"C:\Users\Pat\OneDrive\Documents\JobApplications", Known, oneDrive);

        Assert.Equal(BackupKind.OneDriveNotRunning, status.Kind);
        Assert.Contains("not running", status.Headline);
    }

    [Fact]
    public void OutsideOneDrive_SuggestsMovingToDocuments_WhenDocumentsIsSynced()
    {
        var status = BackupAdvisor.Evaluate(@"D:\Jobs", Known, Running(@"C:\Users\Pat\OneDrive"));

        Assert.Equal(BackupKind.OnThisPcOnly, status.Kind);
        Assert.Equal(@"C:\Users\Pat\OneDrive\Documents\JobApplications", status.SuggestedFolder);
        Assert.Contains("lost", status.Detail);
    }

    [Fact]
    public void OutsideOneDrive_SuggestsTheOneDriveFolder_WhenDocumentsIsNotSynced()
    {
        var known = new FakeFolders(@"C:\Users\Pat\Documents", @"C:\Users\Pat\Desktop");

        var status = BackupAdvisor.Evaluate(@"C:\Users\Pat\Documents\JobApplications", known, Running(@"C:\Users\Pat\OneDrive"));

        Assert.Equal(BackupKind.OnThisPcOnly, status.Kind);
        Assert.Equal(@"C:\Users\Pat\OneDrive\JobApplications", status.SuggestedFolder);
    }

    [Fact]
    public void WithoutOneDrive_NoMoveIsSuggested()
    {
        var status = BackupAdvisor.Evaluate(@"C:\Users\Pat\Desktop\JobApplications", Known, new OneDriveLocator(() => [], () => false));

        Assert.Equal(BackupKind.NoOneDrive, status.Kind);
        Assert.Null(status.SuggestedFolder);
    }
}

public class ReleaseNotesTests
{
    [Fact]
    public void ParseHistory_ReadsVersionsAndTheirLines_NewestFirst()
    {
        const string md = """
            # What's new

            Some intro that is ignored.

            ## 1.0.0
            - First thing
            - Second thing

            ## v1.10.0 - 2026-12-01
            - Newer thing

            ## 1.2.0
            - Middle thing
              not a bullet
            """;

        var sections = ReleaseNotes.ParseHistory(md);

        Assert.Equal(["1.10.0", "1.2.0", "1.0.0"], sections.Select(s => UpdateVersions.Display(s.Version)));
        Assert.Equal(["Newer thing"], sections[0].Items);
        Assert.Equal(["Middle thing"], sections[1].Items);
        Assert.Equal(["First thing", "Second thing"], sections[2].Items);
        Assert.Equal("Version 1.10.0", sections[0].Title);
    }

    [Fact]
    public void MarkdownEmphasis_IsShownAsPlainText_InBothSources()
    {
        var history = ReleaseNotes.ParseHistory("## 1.0.0\n- Open **What's new** and run `setup` now");
        var github = ReleaseNotes.FromGitHubBody("## What's new in 1.0.0\n\n- app: Open **What's new** and run `setup` now", new Version(1, 0, 0));

        Assert.Equal("Open What's new and run setup now", history[0].Items[0]);
        Assert.Equal("Open What's new and run setup now", github!.Items[0]);
    }

    [Fact]
    public void TheNotesThatShipInTheApp_AreReadableAndComplete()
    {
        var history = ReleaseNotes.History();

        Assert.NotEmpty(history);
        Assert.All(history, s => Assert.NotEmpty(s.Items));
        Assert.Equal(history.OrderByDescending(s => s.Version).Select(s => s.Version), history.Select(s => s.Version)); // newest first
        Assert.Equal(history.Count, history.Select(s => s.Version).Distinct().Count()); // no version twice
        foreach (var version in new[] { "1.0.0", "1.1.0", "1.2.0", "1.3.0" })
            Assert.Contains(history, s => UpdateVersions.Display(s.Version) == version);
        Assert.All(history.SelectMany(s => s.Items), item => Assert.DoesNotContain("Co-Authored-By", item)); // written for people, not copied from commits
        Assert.All(history.SelectMany(s => s.Items), item => Assert.DoesNotContain("**", item)); // shown as plain text
    }

    [Fact]
    public void FromGitHubBody_KeepsTheChanges_DropsInstallText_AndTidiesCommitPrefixes()
    {
        const string body = """
            ## What's new in 1.2.0

            - docs: describe the Full-time and Contract tabs, with new screenshots
            - app: Full-time and Contract tabs above the applications
            - core: remember whether an application is full-time or contract

            Changes since v1.1.0.

            **Install:** download the `Setup` file below and run it (no administrator rights needed).

            Windows may show a SmartScreen warning because the program is not code-signed.
            - not a change line
            """;

        var section = ReleaseNotes.FromGitHubBody(body, new Version(1, 2, 0));

        Assert.NotNull(section);
        Assert.Equal(
            ["Describe the Full-time and Contract tabs, with new screenshots", "Full-time and Contract tabs above the applications", "Remember whether an application is full-time or contract"],
            section.Items);
        Assert.Equal("Version 1.2.0", section.Title);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("## What's new in 1.0.0\n\nNothing to list.\n\n**Install:** go.")]
    public void FromGitHubBody_WithNoChangeLines_IsNull(string? body) =>
        Assert.Null(ReleaseNotes.FromGitHubBody(body, new Version(1, 0, 0)));
}

public class WhatsNewPolicyTests
{
    private static readonly Version Current = new(1, 3, 0);

    [Fact]
    public void ABrandNewInstall_SaysNothing_JustRemembersTheVersion() =>
        Assert.Equal(WhatsNewAction.RecordOnly, WhatsNewPolicy.Decide(null, isFirstRun: true, Current));

    [Fact]
    public void AnExistingInstallWithNoRecord_IsShownTheNotesOnce() =>
        Assert.Equal(WhatsNewAction.Show, WhatsNewPolicy.Decide(null, isFirstRun: false, Current));

    [Fact]
    public void ANewerVersionThanTheOneSeen_IsAnnounced() =>
        Assert.Equal(WhatsNewAction.Show, WhatsNewPolicy.Decide("1.2.0", isFirstRun: false, Current));

    [Theory]
    [InlineData("1.3.0")]
    [InlineData("1.4.0")] // a downgrade must not show anything
    public void TheSameOrNewerVersionSeen_ShowsNothing(string seen) =>
        Assert.Equal(WhatsNewAction.Nothing, WhatsNewPolicy.Decide(seen, isFirstRun: false, Current));

    private static readonly IReadOnlyList<ReleaseSection> History =
    [
        new(new Version(1, 4, 0), ["four"]), new(new Version(1, 3, 1), ["three-one"]), new(new Version(1, 3, 0), ["three"]), new(new Version(1, 2, 0), ["two"]),
    ];

    private static string[] Versions(IReadOnlyList<ReleaseSection> sections) => sections.Select(s => UpdateVersions.Display(s.Version)).ToArray();

    [Fact]
    public void WithNoRecord_TheWholeReleaseLineOfTheRunningVersionIsShown() =>
        Assert.Equal(["1.3.1", "1.3.0"], Versions(WhatsNewPolicy.SectionsToShow(History, null, new Version(1, 3, 1))));

    [Fact]
    public void WithARecord_EverythingNewerUpToTheRunningVersionIsShown()
    {
        Assert.Equal(["1.3.1", "1.3.0"], Versions(WhatsNewPolicy.SectionsToShow(History, new Version(1, 2, 0), new Version(1, 3, 1))));
        Assert.Equal(["1.3.1"], Versions(WhatsNewPolicy.SectionsToShow(History, new Version(1, 3, 0), new Version(1, 3, 1))));
        Assert.Empty(WhatsNewPolicy.SectionsToShow(History, new Version(1, 3, 1), new Version(1, 3, 1)));
    }

    [Fact]
    public void VersionsNewerThanTheRunningOne_AreNeverShown() =>
        Assert.DoesNotContain("1.4.0", Versions(WhatsNewPolicy.SectionsToShow(History, new Version(1, 2, 0), new Version(1, 3, 0))));

    [Fact]
    public void AGarbledRecord_IsTreatedAsNoRecord() =>
        Assert.Equal(WhatsNewAction.Show, WhatsNewPolicy.Decide("banana", isFirstRun: false, Current));

    [Fact]
    public void TheVersionSeenIsSavedAndLoaded_AndAFreshFileIsAFirstRun()
    {
        using var temp = new TempDir();
        var file = Path.Combine(temp.Path, "settings.json");
        Assert.True(AppSettings.Load(file, Path.Combine(temp.Path, "none.json")).IsFirstRun);

        new AppSettings { LastSeenVersion = "1.3.0" }.Save(file);
        var loaded = AppSettings.Load(file);

        Assert.Equal("1.3.0", loaded.LastSeenVersion);
        Assert.False(loaded.IsFirstRun);
    }
}

public class UpdateNotesTests
{
    [Fact]
    public async Task AnAvailableUpdate_CarriesTheNotesOfItsRelease()
    {
        const string release = """
            { "tag_name": "v1.3.0", "html_url": "https://github.com/o/r/releases/tag/v1.3.0",
              "body": "## What's new in 1.3.0\n\n- Something new\n\n**Install:** go.",
              "assets": [ { "name": "JobTracker-Setup-1.3.0.exe", "browser_download_url": "https://github.com/o/r/releases/download/v1.3.0/s" } ] }
            """;
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(release, Encoding.UTF8, "application/json") });

        var result = await new GitHubUpdateChecker(new HttpClient(handler), "o", "r").CheckAsync(new Version(1, 2, 0));

        Assert.Equal(UpdateStatus.Available, result.Status);
        var section = ReleaseNotes.FromGitHubBody(result.Update!.Notes, result.Update.Version);
        Assert.Equal(["Something new"], section!.Items);
    }
}
