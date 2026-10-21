using JobTracker.Core.Export;
using JobTracker.Core.Resumes;

namespace JobTracker.Core.Tests;

public class CurrentResumeWordTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static NewApplication Sample(string resume, string? file = null) =>
        new("Acme Corp", "Senior Backend Engineer", "We need Kafka experience.\n- Build APIs", resume, null, new DateOnly(2026, 10, 7), ResumeFilePath: file);

    private const string Alex = "Alex Rivera\nSenior Engineer\n\n## Experience\nContoso, Engineer\n- Built C# services for claim intake\n- Tuned SQL queries\n";

    [Fact]
    public void MakesTheFileForTheResumeInUse_AndRecordsIt()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample(Alex));

        var export = CurrentResumeWord.Refresh(store, app);

        var path = Path.Combine(app.FolderPath, "Alex_Rivera_Resume.docx");
        Assert.NotNull(export);
        Assert.True(File.Exists(path));
        Assert.True(export!.Verified);
        Assert.Equal("Alex_Rivera_Resume.docx", app.Meta.ResumeWordFile);
        Assert.Equal("Alex_Rivera_Resume.docx", store.List().Single().Meta.ResumeWordFile); // it is on disk, not only in memory
        Assert.Equal(path, CurrentResumeWord.ExistingPath(app));
    }

    [Fact]
    public void ChoosingAnotherResumeReplacesTheFileContents()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample(Alex));
        CurrentResumeWord.Refresh(store, app);

        store.SaveResume(app, Alex + "- Added a bullet about on-call\n");
        CurrentResumeWord.Refresh(store, app);

        Assert.Single(Directory.GetFiles(app.FolderPath, "*_Resume.docx"));
        Assert.Contains("Added a bullet about on-call", ResumeFileReader.Read(Path.Combine(app.FolderPath, "Alex_Rivera_Resume.docx")).Text);
    }

    [Fact]
    public void WhenTheNameChangesTheOldFileIsRemovedAndTheNewOneMade()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample(Alex));
        CurrentResumeWord.Refresh(store, app);

        store.SaveResume(app, Alex.Replace("Alex Rivera", "Priya Raman"));
        CurrentResumeWord.Refresh(store, app);

        Assert.False(File.Exists(Path.Combine(app.FolderPath, "Alex_Rivera_Resume.docx")));
        Assert.True(File.Exists(Path.Combine(app.FolderPath, "Priya_Raman_Resume.docx")));
        Assert.Equal("Priya_Raman_Resume.docx", app.Meta.ResumeWordFile);
    }

    [Fact]
    public void AFileThePersonPutThereIsNeverRemoved_EvenWithASimilarName()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample(Alex));
        var theirs = Path.Combine(app.FolderPath, "Old_Draft_Resume.docx");
        File.WriteAllText(theirs, "mine");
        CurrentResumeWord.Refresh(store, app);

        store.SaveResume(app, Alex.Replace("Alex Rivera", "Priya Raman"));
        CurrentResumeWord.Refresh(store, app);

        Assert.True(File.Exists(theirs));
    }

    [Fact]
    public void AMetaFileCannotPointTheCleanUpOutsideTheFolder()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample(Alex));
        var outside = Path.Combine(temp.Path, "precious.docx");
        File.WriteAllText(outside, "do not delete");
        app.Meta.ResumeWordFile = @"..\..\precious.docx";

        CurrentResumeWord.Refresh(store, app);

        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void WithAWordOriginalAndNoChangesTheFileIsACopyOfItUnderTheNewName()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var text = ResumeFileReader.Read(Fixture("resume-word.docx")).Text;
        var app = store.Create(Sample(text, Fixture("resume-word.docx")));

        var export = CurrentResumeWord.Refresh(store, app);

        Assert.Equal(DocxMethod.EditedOriginal, export!.Method);
        var made = Path.Combine(app.FolderPath, app.Meta.ResumeWordFile!);
        Assert.Equal(File.ReadAllBytes(Fixture("resume-word.docx")), File.ReadAllBytes(made)); // copied, then renamed: not rebuilt
    }

    [Fact]
    public void NoResumeTextMeansNoFile()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample(""));

        Assert.Null(CurrentResumeWord.Refresh(store, app));
        Assert.Empty(Directory.GetFiles(app.FolderPath, "*.docx"));
        Assert.Null(app.Meta.ResumeWordFile);
    }

    [Fact]
    public void AFileOpenInWordIsReported_AndTheOldOneIsLeftAlone()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(temp.Path);
        var app = store.Create(Sample(Alex));
        CurrentResumeWord.Refresh(store, app);
        var path = Path.Combine(app.FolderPath, "Alex_Rivera_Resume.docx");
        var before = File.ReadAllBytes(path);
        store.SaveResume(app, Alex + "- Another bullet\n");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => CurrentResumeWord.Refresh(store, app));

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("Alex Rivera\n", 3, "Alex_Rivera_Resume_v3.docx")]
    [InlineData("", 12, "Resume_v12.docx")]
    public void APreviewOfAnotherVersionHasItsOwnName(string resume, int number, string expected) =>
        Assert.Equal(expected, DownloadNames.ForVersion(resume, number));
}