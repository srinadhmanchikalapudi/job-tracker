using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobTracker.Core.Tailoring;

namespace JobTracker.Core.Export;

/// <summary>
/// Lays a resume out as a clean Word document an applicant tracking system reads without trouble: one column, real heading styles, real bullets,
/// a standard font, no tables, text boxes or pictures. Used when the person's own Word file cannot be edited safely (a PDF or pasted resume, or a
/// layout that cannot be patched).
/// </summary>
public static class DocxResumeWriter
{
    private const int PageWidth = 12240;  // US Letter, in twentieths of a point
    private const int PageHeight = 15840;
    private const int Margin = 1080;      // 0.75 inch

    public static void Write(string path, string resumeText)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var lines = ResumeStructure.Annotate(resumeText);

        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        main.Document = new Document(new Body());
        var body = main.Document.Body!;
        AddStyles(main);
        AddNumbering(main);

        var index = 0;
        string? name = null;

        // The name, then the title / contact lines, sit above the first section or job.
        if (lines.Count > 0 && lines[0].Role != LineRole.Bullet && ResumeStructure.PersonName(resumeText) is { } person)
        {
            name = person;
            body.Append(StyledParagraph("Title", name));
            index = 1;
            while (index < lines.Count && lines[index].Role == LineRole.Prose)
                body.Append(ContactLine(lines[index++].Line.Display));
        }

        var firstOfJob = true;
        var previousRole = LineRole.Section;
        for (; index < lines.Count; index++)
        {
            var (line, role) = (lines[index].Line, lines[index].Role);
            switch (role)
            {
                case LineRole.Section:
                    body.Append(StyledParagraph("Heading1", line.Display));
                    firstOfJob = true;
                    break;

                case LineRole.JobHeader:
                    if (previousRole != LineRole.JobHeader)
                        firstOfJob = true;
                    body.Append(firstOfJob ? StyledParagraph("Heading2", line.Display) : JobDetail(line.Display));
                    firstOfJob = false;
                    break;

                case LineRole.Bullet:
                    body.Append(Bullet(line.Display, NestingLevel(line.Raw)));
                    break;

                default:
                    body.Append(Plain(line.Display));
                    break;
            }

            previousRole = role;
        }

        body.Append(new SectionProperties(
            new PageSize { Width = PageWidth, Height = PageHeight },
            new PageMargin { Top = Margin, Right = (uint)Margin, Bottom = Margin, Left = (uint)Margin, Header = 720, Footer = 720, Gutter = 0 }));

        document.PackageProperties.Title = name is null ? "Resume" : name + " resume";
        main.Document.Save();
    }

    private static int NestingLevel(string raw) => Math.Clamp((raw.Length - raw.TrimStart().Length) / 2, 0, 1);

    private static Paragraph StyledParagraph(string style, string text) =>
        new(new ParagraphProperties(new ParagraphStyleId { Val = style }), TextRun(text));

    private static Paragraph ContactLine(string text) =>
        new(new ParagraphProperties(new SpacingBetweenLines { After = "20" }, new Justification { Val = JustificationValues.Center }),
            TextRun(text, size: 20));

    private static Paragraph JobDetail(string text) =>
        new(new ParagraphProperties(new KeepNext(), new SpacingBetweenLines { After = "0" }), TextRun(text));

    private static Paragraph Plain(string text) => new(TextRun(text));

    private static Paragraph Bullet(string text, int level) =>
        new(new ParagraphProperties(
                new ParagraphStyleId { Val = "ListParagraph" },
                new NumberingProperties(new NumberingLevelReference { Val = level }, new NumberingId { Val = 1 })),
            TextRun(text));

    private static Run TextRun(string text, int? size = null)
    {
        var run = new Run();
        if (size is { } half)
            run.Append(new RunProperties(new FontSize { Val = half.ToString() }, new FontSizeComplexScript { Val = half.ToString() }));
        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        part.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri", EastAsia = "Calibri" },
                    new FontSize { Val = "21" }, new FontSizeComplexScript { Val = "21" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines { After = "60", Line = "259", LineRule = LineSpacingRuleValues.Auto }))),
            new Style(new StyleName { Val = "Normal" }, new PrimaryStyle()) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
            new Style(
                new StyleName { Val = "Title" }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" }, new PrimaryStyle(),
                new StyleParagraphProperties(new SpacingBetweenLines { After = "40" }, new Justification { Val = JustificationValues.Center }),
                new StyleRunProperties(new Bold(), new FontSize { Val = "40" }, new FontSizeComplexScript { Val = "40" }))
            { Type = StyleValues.Paragraph, StyleId = "Title" },
            new Style(
                new StyleName { Val = "heading 1" }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" }, new PrimaryStyle(),
                new StyleParagraphProperties(
                    new KeepNext(),
                    new ParagraphBorders(new BottomBorder { Val = BorderValues.Single, Size = 6, Space = 1, Color = "808080" }),
                    new SpacingBetweenLines { Before = "200", After = "80" },
                    new OutlineLevel { Val = 0 }),
                new StyleRunProperties(new Bold(), new Caps(), new FontSize { Val = "23" }, new FontSizeComplexScript { Val = "23" }))
            { Type = StyleValues.Paragraph, StyleId = "Heading1" },
            new Style(
                new StyleName { Val = "heading 2" }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" }, new PrimaryStyle(),
                new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "140", After = "0" }, new OutlineLevel { Val = 1 }),
                new StyleRunProperties(new Bold(), new FontSize { Val = "22" }, new FontSizeComplexScript { Val = "22" }))
            { Type = StyleValues.Paragraph, StyleId = "Heading2" },
            new Style(
                new StyleName { Val = "List Paragraph" }, new BasedOn { Val = "Normal" }, new PrimaryStyle(),
                new StyleParagraphProperties(new SpacingBetweenLines { After = "30" }, new Indentation { Left = "720" }))
            { Type = StyleValues.Paragraph, StyleId = "ListParagraph" });
        part.Styles.Save();
    }

    private static void AddNumbering(MainDocumentPart main)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        part.Numbering = new Numbering(
            new AbstractNum(
                new MultiLevelType { Val = MultiLevelValues.HybridMultilevel },
                Level(0, "•", 360, 360),
                Level(1, "–", 720, 360))
            { AbstractNumberId = 0 },
            new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 });
        part.Numbering.Save();
    }

    private static Level Level(int index, string mark, int left, int hanging) =>
        new(new StartNumberingValue { Val = 1 },
            new NumberingFormat { Val = NumberFormatValues.Bullet },
            new LevelText { Val = mark },
            new LevelJustification { Val = LevelJustificationValues.Left },
            new PreviousParagraphProperties(new Indentation { Left = left.ToString(), Hanging = hanging.ToString() }),
            new NumberingSymbolRunProperties(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" }))
        { LevelIndex = index };
}
