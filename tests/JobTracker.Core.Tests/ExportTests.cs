using System.Security.Cryptography;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobTracker.Core.Export;
using JobTracker.Core.Resumes;

namespace JobTracker.Core.Tests;

public class ResumeDocxExportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jt-export-" + Guid.NewGuid().ToString("N"));

    public ResumeDocxExportTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, true);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private string WithOriginal(string fixture)
    {
        File.Copy(Fixture(fixture), Path.Combine(_folder, "resume.original.docx"));
        return ResumeFileReader.Read(Fixture(fixture)).Text;
    }

    private string Out(string name = "out.docx") => Path.Combine(_folder, name);

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private const string OldLatency = "- Cut SQL Server query latency by 63% by redesigning indexes and removing N+1 queries in the claims API.";
    private const string LedTeam = "- Led a team of four engineers delivering a claims platform on Azure Service Bus and Cosmos DB.";

    // ---------------------------------------------------------------- editing the person's own Word file

    [Fact]
    public void ReWordsAndAddsBulletsInACopyOfTheirOwnFileAndKeepsTheirFormatting()
    {
        var baseline = WithOriginal("resume-word.docx");
        var edited = baseline
            .Replace(OldLatency, "- Cut claims API query latency by 63% on SQL Server by redesigning indexes and removing N+1 queries.")
            .Replace(LedTeam, LedTeam + "\n- Trained five new hires on the claims API.");
        var originalHash = Hash(Path.Combine(_folder, "resume.original.docx"));

        var export = ResumeDocxExporter.Export(_folder, edited, Out());

        Assert.Equal(DocxMethod.EditedOriginal, export.Method);
        Assert.True(export.Verified, string.Join(" | ", export.Differences));
        Assert.Equal(originalHash, Hash(Path.Combine(_folder, "resume.original.docx"))); // the original file is never touched

        var read = ResumeFileReader.Read(export.Path).Text;
        Assert.Contains("- Cut claims API query latency by 63% on SQL Server", read);
        Assert.Contains(LedTeam + "\n- Trained five new hires on the claims API.\nSoftware Engineer, Fabrikam Logistics", read);
        Assert.DoesNotContain("Cut SQL Server query latency", read);

        // The look is the original's: the changed paragraph and the new one keep the paragraph and run formatting of the bullet they came from.
        using var original = WordprocessingDocument.Open(Path.Combine(_folder, "resume.original.docx"), false);
        using var patched = WordprocessingDocument.Open(export.Path, false);
        var before = original.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().ToList();
        var after = patched.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().ToList();
        Assert.Equal(before.Count + 1, after.Count);

        var originalLatency = before.Single(p => p.InnerText.StartsWith("Cut SQL Server"));
        var newLatency = after.Single(p => p.InnerText.StartsWith("Cut claims API"));
        Assert.Equal(originalLatency.ParagraphProperties?.OuterXml, newLatency.ParagraphProperties?.OuterXml);
        Assert.Equal(originalLatency.Descendants<Run>().First().RunProperties?.OuterXml, newLatency.Descendants<Run>().First().RunProperties?.OuterXml);

        var originalLed = before.Single(p => p.InnerText.StartsWith("Led a team"));
        var added = after.Single(p => p.InnerText.StartsWith("Trained five"));
        Assert.Equal(originalLed.ParagraphProperties?.OuterXml, added.ParagraphProperties?.OuterXml);
        Assert.StartsWith("Led a team", after[after.IndexOf(added) - 1].InnerText);
    }

    [Fact]
    public void AllTheOtherLinesAreLeftAlone()
    {
        var baseline = WithOriginal("resume-word.docx");
        var export = ResumeDocxExporter.Export(_folder, baseline.Replace(OldLatency, "- Cut claims query latency by 63%."), Out());
        Assert.Equal(DocxMethod.EditedOriginal, export.Method);

        using var original = WordprocessingDocument.Open(Path.Combine(_folder, "resume.original.docx"), false);
        using var patched = WordprocessingDocument.Open(export.Path, false);
        var before = original.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(p => p.OuterXml).ToList();
        var after = patched.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(p => p.OuterXml).ToList();
        Assert.Equal(before.Count, after.Count);
        Assert.Single(before.Zip(after), pair => pair.First != pair.Second);
    }

    [Fact]
    public void RemovesALineTheTextNoLongerHas()
    {
        var baseline = WithOriginal("resume-word.docx");
        var export = ResumeDocxExporter.Export(_folder, baseline.Replace(LedTeam + "\n", ""), Out());
        Assert.Equal(DocxMethod.EditedOriginal, export.Method);
        Assert.DoesNotContain("Led a team", ResumeFileReader.Read(export.Path).Text);
        Assert.True(export.Verified);
    }

    [Fact]
    public void NoChangesGivesTheirFileBack()
    {
        var baseline = WithOriginal("resume-word.docx");
        var export = ResumeDocxExporter.Export(_folder, baseline, Out());
        Assert.Equal(DocxMethod.EditedOriginal, export.Method);
        Assert.Equal(Hash(Path.Combine(_folder, "resume.original.docx")), Hash(export.Path));
    }

    [Fact]
    public void WorksOnAFileWhoseLayoutIsATable()
    {
        var baseline = WithOriginal("resume-word-table.docx");
        var edited = baseline.Replace("- Automated the monthly sales report that three teams used to build by hand.",
            "- Automated the monthly sales report that three teams used to build by hand, saving each of them a morning.");
        var export = ResumeDocxExporter.Export(_folder, edited, Out());

        Assert.Equal(DocxMethod.EditedOriginal, export.Method);
        Assert.True(export.Verified, string.Join(" | ", export.Differences));
        using var patched = WordprocessingDocument.Open(export.Path, false);
        Assert.NotEmpty(patched.MainDocumentPart!.Document!.Body!.Descendants<Table>()); // the table, and so the two columns, are still there
    }

    [Fact]
    public void ARewordedLineWithMixedFormattingFallsBackToACleanDocument()
    {
        // A bullet that starts with a bold lead-in would lose it if its text were replaced, so the person's layout is not used.
        var path = Path.Combine(_folder, "resume.original.docx");
        using (var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Alex Morgan"))),
                new Paragraph(new Run(new Text("Experience"))),
                new Paragraph(
                    new Run(new RunProperties(new Bold()), new Text("Platform: ") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }),
                    new Run(new Text("built a billing service")))));
            main.Document.Save();
        }

        var baseline = ResumeFileReader.Read(path).Text;
        var export = ResumeDocxExporter.Export(_folder, baseline.Replace("built a billing service", "built and ran a billing service"), Out());

        Assert.Equal(DocxMethod.Generated, export.Method);
        Assert.Contains("mixes formatting", export.Note);
        Assert.True(export.Verified, string.Join(" | ", export.Differences));
        Assert.Contains("built and ran a billing service", ResumeFileReader.Read(export.Path).Text);
    }

    [Fact]
    public void ALineThatCannotBeFoundInTheirFileFallsBack()
    {
        // The name and contact line of this file live in the page header, which is not part of the body that gets edited.
        var baseline = WithOriginal("resume-word-table.docx");
        var export = ResumeDocxExporter.Export(_folder, baseline.Replace("Austin, TX | jordan", "Dallas, TX | jordan"), Out());

        Assert.Equal(DocxMethod.Generated, export.Method);
        Assert.Contains("could not be found in your Word file", export.Note);
        Assert.True(export.Verified, string.Join(" | ", export.Differences));
        Assert.Contains("Dallas, TX", ResumeFileReader.Read(export.Path).Text);
    }
    // ---------------------------------------------------------------- a clean document made from the text

    private const string Sample = "Alex Rivera\nSenior Software Engineer | Seattle, WA | alex@example.com\n\n## Summary\nEngineer who builds claims platforms.\n\n## Experience\nContoso Insurance\nSenior Engineer, 2021 to present\n- Cut SQL Server query latency by 63%\n- Led four engineers\n  - Mentored two of them\n\nFabrikam Systems, Engineer, 2018 to 2021\n- Built a billing service\n\n## Skills\nC#, SQL Server, Kafka\n";

    [Fact]
    public void LaysOutAnyResumeAsACleanDocumentThatReadsBackTheSame()
    {
        var export = ResumeDocxExporter.Export(_folder, Sample, Out());

        Assert.Equal(DocxMethod.Generated, export.Method);
        Assert.True(export.Verified, string.Join(" | ", export.Differences));
        Assert.Contains("Your original file is not a Word document", export.Note);
        Assert.Empty(ResumeTextCompare.Differences(Sample, ResumeFileReader.Read(export.Path).Text));
    }

    [Fact]
    public void TheCleanDocumentUsesRealStylesRealBulletsAndNoTablesOrPictures()
    {
        var export = ResumeDocxExporter.Export(_folder, Sample, Out());
        using var doc = WordprocessingDocument.Open(export.Path, false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        Assert.Empty(body.Descendants<Table>());
        Assert.Empty(body.Descendants<Drawing>());
        Assert.Empty(body.Descendants<TextBoxContent>());
        Assert.Contains(body.Descendants<ParagraphStyleId>(), s => s.Val == "Title");
        Assert.Equal(3, body.Descendants<ParagraphStyleId>().Count(s => s.Val == "Heading1")); // Summary, Experience, Skills
        Assert.Equal(2, body.Descendants<ParagraphStyleId>().Count(s => s.Val == "Heading2")); // the two jobs
        Assert.Equal(4, body.Descendants<NumberingProperties>().Count()); // real list items, not typed dashes
        Assert.DoesNotContain(body.Descendants<Text>(), t => t.Text.TrimStart().StartsWith("-") || t.Text.TrimStart().StartsWith("•"));
        Assert.Contains(body.Descendants<NumberingLevelReference>(), n => n.Val?.Value == 1); // the nested bullet
        Assert.Equal(12240u, doc.MainDocumentPart.Document.Body!.Descendants<PageSize>().Single().Width!.Value);
    }

    [Fact]
    public void ARealWordFileIsNotUsedWhenTheOriginalWasAPdf()
    {
        File.WriteAllBytes(Path.Combine(_folder, "resume.original.pdf"), File.ReadAllBytes(Fixture("resume-word.pdf")));
        var export = ResumeDocxExporter.Export(_folder, ResumeFileReader.Read(Fixture("resume-word.pdf")).Text, Out());
        Assert.Equal(DocxMethod.Generated, export.Method);
        Assert.True(export.Verified, string.Join(" | ", export.Differences));
    }

    [Theory]
    [InlineData("resume-word.pdf")]
    [InlineData("resume-edge.pdf")]
    [InlineData("resume-word-table.pdf")]
    public void EveryFixtureResumeSurvivesBeingLaidOutAsAWordDocument(string fixture)
    {
        var text = ResumeFileReader.Read(Fixture(fixture)).Text;
        var export = ResumeDocxExporter.Export(_folder, text, Out());
        Assert.True(export.Verified, fixture + ": " + string.Join(" | ", export.Differences));
    }

    [Fact]
    public void AnEmptyOrOneLineResumeStillMakesAValidFile()
    {
        foreach (var text in new[] { "Alex Rivera", "- just a bullet", "## Experience" })
        {
            var export = ResumeDocxExporter.Export(_folder, text, Out());
            Assert.True(File.Exists(export.Path));
            Assert.True(export.Verified, text + ": " + string.Join(" | ", export.Differences));
        }
    }

    // ---------------------------------------------------------------- the preview model

    [Fact]
    public void ThePreviewShowsTheFileAsTitleHeadingsAndBullets()
    {
        var export = ResumeDocxExporter.Export(_folder, Sample, Out());
        var blocks = DocxPreviewModel.Read(export.Path);

        Assert.Equal(PreviewBlockKind.Title, blocks[0].Kind);
        Assert.Equal("Alex Rivera", blocks[0].Text);
        Assert.True(blocks[0].Runs[0].Bold);
        Assert.Equal("center", blocks[0].Alignment);
        Assert.Contains(blocks, b => b.Kind == PreviewBlockKind.Heading && b.Text == "Experience" && b.Runs[0].Caps);
        Assert.Contains(blocks, b => b.Kind == PreviewBlockKind.SubHeading && b.Text == "Contoso Insurance");
        Assert.Equal(4, blocks.Count(b => b.Kind == PreviewBlockKind.Bullet));
        Assert.Contains(blocks, b => b.Kind == PreviewBlockKind.Bullet && b.Level == 1 && b.Text == "Mentored two of them");
    }

    [Fact]
    public void ThePreviewOfAPatchedOriginalIncludesTheNewBullet()
    {
        var baseline = WithOriginal("resume-word.docx");
        var export = ResumeDocxExporter.Export(_folder, baseline.Replace(LedTeam, LedTeam + "\n- Trained five new hires on the claims API."), Out());
        var blocks = DocxPreviewModel.Read(export.Path);
        Assert.Contains(blocks, b => b.Kind == PreviewBlockKind.Bullet && b.Text == "Trained five new hires on the claims API.");
        Assert.Contains(blocks, b => b.Kind == PreviewBlockKind.Title && b.Text == "Alex Morgan");
    }
}

public class DownloadNameTests
{
    [Theory]
    [InlineData("Alex Rivera\nSenior Engineer\n", "Alex_Rivera_Resume.docx")]
    [InlineData("Priya Raman\nSenior Software Engineer\n", "Priya_Raman_Resume.docx")]
    [InlineData("Priya K. Raman\n", "Priya_Raman_Resume.docx")]
    [InlineData("Dr. Alex Q. Rivera Jr.\n", "Alex_Rivera_Resume.docx")]
    [InlineData("José Núñez-García, PMP\n", "Jose_Nunez-Garcia_Resume.docx")]
    [InlineData("Conan O'Brien\n", "Conan_OBrien_Resume.docx")]
    [InlineData("Madonna\n", "Madonna_Resume.docx")]
    [InlineData("## Experience\n- built things\n", "Resume.docx")]
    [InlineData("alex@example.com | 555-0100\n", "Resume.docx")]
    [InlineData("", "Resume.docx")]
    public void SuggestsFirstLastResumeWithUnderscores(string resume, string expected) =>
        Assert.Equal(expected, DownloadNames.Suggest(resume));

    [Theory]
    [InlineData("Alex Rivera\n")]
    [InlineData("Mary Ann Van Der Berg\n")]
    [InlineData("<Alex>|? Ri:vera\n")]
    public void TheNameHasNoSpacesPunctuationOrIllegalCharacters(string resume)
    {
        var name = DownloadNames.Suggest(resume);
        Assert.DoesNotContain(' ', name);
        Assert.DoesNotContain(name.ToCharArray(), c => Path.GetInvalidFileNameChars().Contains(c));
        Assert.Matches("^[A-Za-z0-9_-]+\\.docx$", name);
    }
    [Theory]
    [InlineData("Alex Rivera\nrest", "Alex Rivera")]
    [InlineData("# Alex Rivera\nrest", "Alex Rivera")]
    [InlineData("Dr. Alex Q. Rivera Jr.\nrest", "Dr. Alex Q. Rivera Jr.")]
    [InlineData("- a bullet first", null)]
    [InlineData("Experience\n- a bullet", null)]
    [InlineData("Seattle, WA | alex@example.com", null)]
    [InlineData("Resume of a person with a very long name that is actually a sentence", null)]
    public void FindsAPersonsNameOnlyWhenTheFirstLineLooksLikeOne(string resume, string? expected) =>
        Assert.Equal(expected, JobTracker.Core.Tailoring.ResumeStructure.PersonName(resume));
}
