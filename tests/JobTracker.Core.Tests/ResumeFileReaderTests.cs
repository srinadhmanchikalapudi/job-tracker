using System.Text;
using JobTracker.Core.Resumes;

namespace JobTracker.Core.Tests;

/// <summary>
/// The fixtures in Fixtures\ are genuine files made by real programs from invented resumes: Microsoft Word (DOCX and its own PDF export, one plain
/// and one built on a two-column table with the name in the page header), Edge's Chromium engine (a PDF whose bullets are drawn as shapes), and a
/// PDF that is only a picture. Nothing here is synthesised by the code under test.
/// </summary>
public class ResumeFileReaderTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string[] Lines(ResumeExtraction r) => r.Text.Split('\n');

    private static string[] Bullets(ResumeExtraction r) =>
        Lines(r).Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..]).ToArray();

    // The same text without markdown heading marks or blank lines, so a Word file and its PDF can be compared line for line.
    private static string[] Content(ResumeExtraction r) =>
        Lines(r).Select(l => l.TrimStart('#', ' ')).Where(l => l.Length > 0).ToArray();

    private static readonly string[] AlexBullets =
    [
        "Built Kafka consumers that process 40,000 events per second with zero data loss across three regions and two failovers during the quarterly enrollment peak.",
        "Cut SQL Server query latency by 63% by redesigning indexes and removing N+1 queries in the claims API.",
        "Led a team of four engineers delivering a claims platform on Azure Service Bus and Cosmos DB.",
        "Maintained a C# and .NET Core order service used by 120 warehouses.",
        "Introduced contract tests that caught 30 breaking API changes before release.",
    ];

    // ---------------------------------------------------------------- a plain Word resume, as DOCX and as the PDF Word exported

    [Theory]
    [InlineData("resume-word.docx")]
    [InlineData("resume-word.pdf")]
    public void PlainWordResume_KeepsEveryBulletOnOneLine(string file)
    {
        var result = ResumeFileReader.Read(Fixture(file));

        Assert.Equal(AlexBullets, Bullets(result)); // one bullet per line, wrapped lines joined, and nothing else turned into a bullet
        Assert.Empty(result.Warnings);
        Assert.False(result.NoTextLayer);
    }

    [Theory]
    [InlineData("resume-word.docx")]
    [InlineData("resume-word.pdf")]
    public void PlainWordResume_KeepsTheStructureAReaderNeeds(string file)
    {
        var lines = Content(ResumeFileReader.Read(Fixture(file)));

        Assert.Equal("Alex Morgan", lines[0]);
        Assert.Equal("Seattle, WA | alex.morgan@example.com | 555-0100 | linkedin.com/in/alexmorgan-example", lines[1]);
        foreach (var heading in new[] { "Summary", "Experience", "Skills", "Education" })
            Assert.Contains(heading, lines);
        Assert.Equal(["Summary", "Experience", "Skills", "Education"],
            lines.Where(l => l is "Summary" or "Experience" or "Skills" or "Education")); // in order
        Assert.Contains("Senior Software Engineer, Contoso Health (Seattle, WA) 2021 to present", lines);
        Assert.Contains("Software Engineer, Fabrikam Logistics (Remote) 2017 to 2021", lines);
        Assert.Contains("C#, .NET 10, ASP.NET Core, EF Core, SQL Server, Redis, Azure Service Bus, Kafka, Docker", lines);
        Assert.Contains("B.S. Computer Science, State University, 2017", lines);
    }

    [Theory]
    [InlineData("resume-word.docx")]
    [InlineData("resume-word.pdf")]
    public void AWrappedSummaryParagraph_ComesBackAsOneLine(string file)
    {
        var lines = Content(ResumeFileReader.Read(Fixture(file)));

        Assert.Contains("Backend engineer with eight years of experience building payment and claims systems on .NET and Azure, comfortable owning services from design to on-call and mentoring engineers who are new to distributed systems.", lines);
    }

    [Fact]
    public void TheWordFileAndItsPdfExport_GiveTheSameLines() =>
        Assert.Equal(Content(ResumeFileReader.Read(Fixture("resume-word.docx"))), Content(ResumeFileReader.Read(Fixture("resume-word.pdf"))));

    [Fact]
    public void Docx_MarksTheTitleAndHeadings()
    {
        var lines = Lines(ResumeFileReader.Read(Fixture("resume-word.docx")));

        Assert.Equal("# Alex Morgan", lines[0]);
        Assert.Contains("## Experience", lines);
    }

    [Theory]
    [InlineData("resume-word.docx")]
    [InlineData("resume-word.pdf")]
    [InlineData("resume-edge.pdf")]
    [InlineData("resume-word-table.docx")]
    [InlineData("resume-word-table.pdf")]
    public void NoBulletGlyphsOrSymbolFontCharactersLeakIntoTheText(string file)
    {
        var text = ResumeFileReader.Read(Fixture(file)).Text;

        Assert.DoesNotContain(text, ch => ch is '•' or '●' or '▪' || ch is >= '\uE000' and <= '\uF8FF');
        Assert.DoesNotContain('\0', text);
        Assert.DoesNotContain("  ", text); // runs of spaces are collapsed
    }

    // ---------------------------------------------------------------- a two-column layout built on a table, with the name in the page header

    [Fact]
    public void Docx_ReadsTheTableAndThePageHeader()
    {
        var result = ResumeFileReader.Read(Fixture("resume-word-table.docx"));
        var lines = Lines(result);

        Assert.Equal("Jordan Rivers | Austin, TX | jordan.rivers@example.com | 555-0111", lines[0]); // from the page header, put first
        Assert.Equal(
        [
            "Built Airflow pipelines that load 2 TB of order data every night into PostgreSQL with automated backfills.",
            "Reduced warehouse build time from six hours to 55 minutes by partitioning the largest tables.",
            "Automated the monthly sales report that three teams used to build by hand.",
        ], Bullets(result));
        Assert.Contains("Data Engineer, Northwind Traders (Austin, TX) 2020 to present", lines); // a role is not a bullet
        Assert.Contains("Analyst, Tailspin Toys 2017 to 2020", lines);
        foreach (var skill in new[] { "Python", "PostgreSQL", "Airflow", "Terraform", "AWS" })
            Assert.Contains(skill, lines);
    }

    [Fact]
    public void Pdf_WithTwoColumns_IsReadOneColumnAtATime_AndSaysSo()
    {
        var result = ResumeFileReader.Read(Fixture("resume-word-table.pdf"));
        var lines = Content(result);

        Assert.Contains(result.Warnings, w => w.Contains("two columns"));
        Assert.Equal("Jordan Rivers | Austin, TX | jordan.rivers@example.com | 555-0111", lines[0]);
        // The whole left column (skills) comes before the right column (experience), and the right column is in its own order.
        var order = new[] { "SKILLS", "Python", "AWS", "EXPERIENCE", "Data Engineer, Northwind Traders (Austin, TX) 2020 to present", "Analyst, Tailspin Toys 2017 to 2020" }
            .Select(l => Array.IndexOf(lines, l)).ToArray();
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.OrderBy(i => i), order);
        Assert.Equal(
        [
            "Built Airflow pipelines that load 2 TB of order data every night into PostgreSQL with automated backfills.",
            "Reduced warehouse build time from six hours to 55 minutes by partitioning the largest tables.",
            "Automated the monthly sales report that three teams used to build by hand.",
        ], Bullets(result));
    }

    [Fact]
    public void ASingleColumnPdf_IsNotMistakenForTwoColumns()
    {
        foreach (var file in new[] { "resume-word.pdf", "resume-edge.pdf" })
            Assert.DoesNotContain(ResumeFileReader.Read(Fixture(file)).Warnings, w => w.Contains("two columns"));
    }

    // ---------------------------------------------------------------- a PDF made by Chromium: the bullet dots are shapes, not characters

    [Fact]
    public void ChromiumPdf_BulletsAreRecoveredFromTheirIndent_AndWrappedLinesAreJoined()
    {
        var result = ResumeFileReader.Read(Fixture("resume-edge.pdf"));
        var lines = Content(result);

        Assert.Equal(
        [
            "Moved 85 services from hand-built servers to Kubernetes with Helm charts and a shared pipeline, cutting deploys from 40 minutes to six.",
            "Wrote the on-call runbooks that brought the median incident time down by 35%.",
            "Mentored three junior engineers through their first production releases.",
            "Automated server patching with Ansible across 200 machines.",
            "Replaced a manual backup process with nightly verified restores.",
        ], Bullets(result));
        Assert.Contains("Sam Okafor", lines);
        Assert.Contains("Denver, CO | sam.okafor@example.com | 555-0122", lines);
        Assert.Contains("Platform Engineer, Adventure Works (Denver, CO) 2019 to present", lines); // roles stay unbulleted
        Assert.Contains("Systems Administrator, Wide World Importers 2015 to 2019", lines);
        Assert.Contains("Platform engineer who keeps build systems fast and boring, with a habit of writing the runbook before the incident rather than after it.", lines);
        Assert.Contains("Kubernetes, Helm, Terraform, Ansible, Linux, GitHub Actions, Prometheus", lines);
        Assert.Empty(result.Warnings);
    }

    // ---------------------------------------------------------------- no text layer

    [Fact]
    public void APdfThatIsOnlyAPicture_IsReportedNotGuessedAt()
    {
        var result = ResumeFileReader.Read(Fixture("scanned-image.pdf"));

        Assert.True(result.NoTextLayer);
        Assert.Equal("", result.Text);
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("no text", warning);
        Assert.Contains("OCR", warning);
    }

    // ---------------------------------------------------------------- the file is open in another program

    [Theory]
    [InlineData("resume-word.docx")]
    [InlineData("resume-word.pdf")]
    public void AFileHeldOpenForWritingByAnotherProgram_CanStillBeRead(string file)
    {
        using var temp = new TempDir();
        var copy = Path.Combine(temp.Path, file);
        File.Copy(Fixture(file), copy);
        using var held = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite, FileShare.Read); // as Word holds an open document

        var result = ResumeFileReader.Read(copy);

        Assert.NotEmpty(result.Text);
    }

    // ---------------------------------------------------------------- text and Markdown files

    [Fact]
    public void TextFiles_KeepTheirLines_AndGlyphBulletsBecomeDashes()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "resume.txt");
        File.WriteAllText(path, "Alex Morgan\r\n\r\n\r\nExperience\r\n• Built a thing\r\n● Fixed another thing\r\n- Already a dash\r\n  - nested\r\nPage 1 of 2\r\n", new UTF8Encoding(true));

        var result = ResumeFileReader.Read(path);

        Assert.Equal("Alex Morgan\n\nExperience\n- Built a thing\n- Fixed another thing\n- Already a dash\n  - nested", result.Text);
        Assert.Equal("Text", result.FileKind);
    }

    [Fact]
    public void MarkdownFiles_AreKeptAsTheyAre()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "resume.md");
        File.WriteAllText(path, "# Alex Morgan\n\n## Experience\n- Built a thing\n");

        var result = ResumeFileReader.Read(path);

        Assert.Equal("# Alex Morgan\n\n## Experience\n- Built a thing", result.Text);
        Assert.Equal("Markdown", result.FileKind);
    }

    [Fact]
    public void NotepadsUnicodeFile_UTF16WithABom_IsDecoded()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "resume.txt");
        File.WriteAllText(path, "Alex Morgan\n• Built café software", new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

        Assert.Equal("Alex Morgan\n- Built café software", ResumeFileReader.Read(path).Text);
    }

    [Fact]
    public void AnOldWindowsCodePageTextFile_IsDecodedNotMangled()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "resume.txt");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllBytes(path, Encoding.GetEncoding(1252).GetBytes("José Müller\nBuilt café software for São Paulo"));

        Assert.Equal("José Müller\nBuilt café software for São Paulo", ResumeFileReader.Read(path).Text);
    }

    // ---------------------------------------------------------------- things that go wrong

    [Fact]
    public void AnOldDocFile_ExplainsWhatToDoInstead()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "old.doc");
        File.WriteAllBytes(path, [1, 2, 3]);

        var ex = Assert.Throws<ResumeReadException>(() => ResumeFileReader.Read(path));

        Assert.Contains("Save As", ex.Message);
        Assert.Contains(".docx", ex.Message);
    }

    [Theory]
    [InlineData("resume.rtf")]
    [InlineData("resume.odt")]
    [InlineData("resume.pages")]
    public void OtherFileTypes_AreRefusedWithAClearMessage(string name)
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, name);
        File.WriteAllBytes(path, [1, 2, 3]);

        Assert.Contains("PDF", Assert.Throws<ResumeReadException>(() => ResumeFileReader.Read(path)).Message);
    }

    [Fact]
    public void AMissingEmptyOrHugeFile_IsRefusedWithAClearMessage()
    {
        using var temp = new TempDir();
        Assert.Contains("could not be found", Assert.Throws<ResumeReadException>(() => ResumeFileReader.Read(Path.Combine(temp.Path, "nope.pdf"))).Message);

        var empty = Path.Combine(temp.Path, "empty.docx");
        File.WriteAllBytes(empty, []);
        Assert.Contains("empty", Assert.Throws<ResumeReadException>(() => ResumeFileReader.Read(empty)).Message);

        var huge = Path.Combine(temp.Path, "huge.pdf");
        using (var stream = File.Create(huge)) stream.SetLength(ResumeFileReader.MaxBytes + 1);
        Assert.Contains("too big", Assert.Throws<ResumeReadException>(() => ResumeFileReader.Read(huge)).Message);
    }

    [Theory]
    [InlineData("damaged.pdf", "PDF")]
    [InlineData("damaged.docx", "Word")]
    public void ADamagedFile_GivesAFriendlyMessage_NotACrash(string name, string kind)
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, name);
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("this is not really a " + kind + " file at all, just some text pretending to be one"));

        var ex = Assert.Throws<ResumeReadException>(() => ResumeFileReader.Read(path));

        Assert.Contains("paste the text", ex.Message);
    }

    [Fact]
    public void ATruncatedRealPdf_GivesAFriendlyMessage()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "cut.pdf");
        var bytes = File.ReadAllBytes(Fixture("resume-word.pdf"));
        File.WriteAllBytes(path, bytes[..(bytes.Length / 3)]);

        // Either the readable part comes back or a friendly error does: never an unhandled exception.
        try
        {
            _ = ResumeFileReader.Read(path);
        }
        catch (ResumeReadException ex)
        {
            Assert.Contains("PDF", ex.Message);
        }
    }

    [Fact]
    public void SupportedTypes_AreRecognisedWhateverTheCase()
    {
        Assert.True(ResumeFileReader.IsSupported(@"C:\x\Resume.PDF"));
        Assert.True(ResumeFileReader.IsSupported("a.DocX"));
        Assert.True(ResumeFileReader.IsSupported("a.md"));
        Assert.True(ResumeFileReader.IsSupported("a.txt"));
        Assert.False(ResumeFileReader.IsSupported("a.doc"));
        Assert.False(ResumeFileReader.IsSupported("a"));
    }
}

/// <summary>
/// The fixtures are committed to a public repository, and Word records the signed-in person as the author of a DOCX. A real name must never
/// travel with them, so regenerating the files without scrubbing them (tools\make-resume-fixtures.ps1 does) fails here.
/// </summary>
public class FixtureHygieneTests
{
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void DocxFixtures_RecordNoRealAuthor()
    {
        var docx = Directory.GetFiles(FixtureDirectory, "*.docx");
        Assert.NotEmpty(docx);
        foreach (var path in docx)
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            using var reader = new StreamReader(zip.GetEntry("docProps/core.xml")!.Open());
            var core = reader.ReadToEnd();
            Assert.Contains("<dc:creator>Resume Fixture</dc:creator>", core);
            Assert.Contains("<cp:lastModifiedBy>Resume Fixture</cp:lastModifiedBy>", core);
        }
    }

    [Fact]
    public void PdfFixtures_RecordNoAuthor_AndNothingNamesThisMachinesUser()
    {
        var pdfs = Directory.GetFiles(FixtureDirectory, "*.pdf");
        Assert.NotEmpty(pdfs);
        foreach (var path in pdfs)
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(path);
            Assert.True(string.IsNullOrEmpty(document.Information.Author), $"{Path.GetFileName(path)} names an author");
            var everything = string.Join(" ", document.Information.Title, document.Information.Subject, document.Information.Keywords,
                document.Information.Creator, document.Information.Producer);
            Assert.DoesNotContain(Environment.UserName, everything, StringComparison.OrdinalIgnoreCase);
        }
    }
}

public class OriginalResumeStorageTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void AnAttachedResume_IsKeptNextToResumeMd_ByteForByte()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(Path.Combine(temp.Path, "data"));

        var app = store.Create(new NewApplication("Acme", "Dev", "jd", "- the reviewed text", ResumeFilePath: Fixture("resume-word.pdf")));

        var kept = Path.Combine(app.FolderPath, "resume.original.pdf");
        Assert.True(File.Exists(kept));
        Assert.Equal(File.ReadAllBytes(Fixture("resume-word.pdf")), File.ReadAllBytes(kept));
        Assert.Equal("- the reviewed text", File.ReadAllText(Path.Combine(app.FolderPath, "resume.md"))); // the reviewed text is what is saved
        Assert.Equal(kept, store.FindOriginalResume(app));
        Assert.Empty(Directory.GetFiles(app.FolderPath, "*.tmp"));
    }

    [Fact]
    public void ADocxIsKeptAsDocx_AndAnUpperCaseExtensionIsTidied()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(Path.Combine(temp.Path, "data"));
        var shouting = Path.Combine(temp.Path, "MY RESUME (final).DOCX");
        File.Copy(Fixture("resume-word.docx"), shouting);

        var app = store.Create(new NewApplication("Acme", "Dev", "jd", "resume", ResumeFilePath: shouting));

        Assert.True(File.Exists(Path.Combine(app.FolderPath, "resume.original.docx")));
        Assert.Single(Directory.GetFiles(app.FolderPath, "resume.original.*"));
    }

    [Fact]
    public void WithoutAnAttachment_NothingExtraIsWritten_AndOldApplicationsAreUnchanged()
    {
        using var temp = new TempDir();
        var store = new ApplicationStore(Path.Combine(temp.Path, "data"));

        var app = store.Create(new NewApplication("Acme", "Dev", "jd", "resume"));

        Assert.Null(store.FindOriginalResume(app));
        Assert.Equal(["jd.md", "meta.json", "notes.md", "resume.md"], Directory.GetFiles(app.FolderPath).Select(Path.GetFileName).OrderBy(n => n));
        Assert.Contains("\"Company\": \"Acme\"", File.ReadAllText(Path.Combine(app.FolderPath, "meta.json"))); // meta.json is unchanged in shape
    }

    [Fact]
    public void AnAttachmentThatCannotBeKept_LeavesNoHalfMadeApplication()
    {
        using var temp = new TempDir();
        var data = Path.Combine(temp.Path, "data");
        var store = new ApplicationStore(data);

        Assert.ThrowsAny<IOException>(() => store.Create(new NewApplication("Acme", "Dev", "jd", "resume", ResumeFilePath: Path.Combine(temp.Path, "gone.pdf"))));

        Assert.Empty(store.List());
        Assert.False(Directory.Exists(Path.Combine(data, "Acme"))); // not even the empty company folder
    }

    [Fact]
    public void AnUnsupportedAttachment_IsRefused_AndLeavesNothingBehind()
    {
        using var temp = new TempDir();
        var data = Path.Combine(temp.Path, "data");
        var odd = Path.Combine(temp.Path, "program.exe");
        File.WriteAllBytes(odd, [1, 2, 3]);

        Assert.ThrowsAny<IOException>(() => new ApplicationStore(data).Create(new NewApplication("Acme", "Dev", "jd", "resume", ResumeFilePath: odd)));

        Assert.False(Directory.Exists(Path.Combine(data, "Acme")));
    }

    [Fact]
    public void MovingTheDataFolder_CarriesTheOriginalResumeAlong()
    {
        using var temp = new TempDir();
        var oldRoot = Path.Combine(temp.Path, "old");
        new ApplicationStore(oldRoot).Create(new NewApplication("Acme", "Dev", "jd", "resume", ResumeFilePath: Fixture("resume-word.docx")));

        DataRootMover.CopyAll(oldRoot, Path.Combine(temp.Path, "new"));

        var moved = Assert.Single(new ApplicationStore(Path.Combine(temp.Path, "new")).List());
        Assert.True(File.Exists(Path.Combine(moved.FolderPath, "resume.original.docx")));
    }
}
