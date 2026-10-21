using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace JobTracker.Core.Resumes;

/// <summary>
/// Reads a Word (.docx) resume in document order: headings become "## Heading", list paragraphs (from a list style or from direct list
/// formatting) become "- bullet" lines, tables are read row by row, and the name and contact line in the page header are included. Text in text
/// boxes is picked up too, with a warning, since an ATS may skip it.
/// </summary>
internal static partial class DocxResumeReader
{
    [GeneratedRegex(@"^Heading\s*(\d)$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingStyle();

    public static ResumeExtraction Read(string path)
    {
        var warnings = new List<string>();
        try
        {
            using var stream = new MemoryStream(ResumeFileReader.ReadShared(path));
            using var document = WordprocessingDocument.Open(stream, isEditable: false);
            var main = document.MainDocumentPart ?? throw new ResumeReadException("That Word file has no document in it.");
            var body = main.Document?.Body ?? throw new ResumeReadException("That Word file has no text in it.");

            var context = new Context(main.StyleDefinitionsPart?.Styles);
            var lines = new List<string>();
            WalkBlocks(body, lines, context);

            // The name and contact line often live in the page header, not the body.
            var bodyStart = lines.Take(8).Select(l => l.Trim().ToLowerInvariant()).ToHashSet();
            var headerLines = main.HeaderParts
                .SelectMany(h => h.Header?.Descendants<Paragraph>() ?? Enumerable.Empty<Paragraph>())
                .Select(p => Clean(Text(p, context)))
                .Where(t => t.Length > 0 && !bodyStart.Contains(t.ToLowerInvariant()))
                .Distinct()
                .ToList();
            if (headerLines.Count > 0)
                lines.InsertRange(0, headerLines.Append(""));

            if (context.TextBoxesSeen)
                warnings.Add("This document has text in text boxes. They were read, but an applicant tracking system may skip them: check the result.");

            var text = ResumeText.Finish(lines);
            if (text.Length == 0)
                warnings.Add("No text was found in this Word document.");
            return new ResumeExtraction(text, "Word", warnings, NoTextLayer: false);
        }
        catch (Exception ex) when (ex is not ResumeReadException and not OperationCanceledException and not IOException and not UnauthorizedAccessException)
        {
            throw new ResumeReadException(
                "This Word file could not be read. It may be damaged or password protected. Save it again as a Word Document (.docx), or paste the text instead.", ex);
        }
    }

    private sealed class Context(Styles? styles)
    {
        public Styles? Styles { get; } = styles;
        public bool TextBoxesSeen { get; set; }
    }

    private static void WalkBlocks(OpenXmlElement container, List<string> lines, Context context)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Paragraph paragraph:
                    EmitParagraph(paragraph, lines, context);
                    break;
                case Table table:
                    EmitTable(table, lines, context);
                    break;
                case SdtBlock block when block.SdtContentBlock is { } content: // content controls, used by many resume templates
                    WalkBlocks(content, lines, context);
                    break;
                case CustomXmlBlock custom:
                    WalkBlocks(custom, lines, context);
                    break;
            }
        }
    }

    private static void EmitParagraph(Paragraph paragraph, List<string> lines, Context context)
    {
        var text = Clean(Text(paragraph, context, out var textBoxParagraphs));
        var properties = paragraph.ParagraphProperties;
        var styleId = properties?.ParagraphStyleId?.Val?.Value;

        if (text.Length == 0)
        {
            lines.Add("");
        }
        else if (HeadingLevel(styleId, context) is { } level)
        {
            lines.Add(new string('#', level) + " " + text);
        }
        else if (IsListItem(paragraph, styleId, context))
        {
            var indent = new string(' ', 2 * Math.Clamp(properties?.NumberingProperties?.NumberingLevelReference?.Val?.Value ?? 0, 0, 3));
            lines.Add(indent + "- " + ResumeText.NormaliseBulletGlyph(text).TrimStart('-', ' ').Trim());
        }
        else
        {
            foreach (var line in text.Split('\n'))
                lines.Add(ResumeText.NormaliseBulletGlyph(line.Trim()));
        }

        // Each text box appears twice in the file (a modern form and a fallback), so repeated text is dropped.
        foreach (var boxText in textBoxParagraphs.Select(p => Clean(Text(p, context))).Where(t => t.Length > 0).Distinct())
        {
            context.TextBoxesSeen = true;
            lines.Add(ResumeText.NormaliseBulletGlyph(boxText));
        }
    }

    private static void EmitTable(Table table, List<string> lines, Context context)
    {
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>().Select(cell =>
            {
                var cellLines = new List<string>();
                WalkBlocks(cell, cellLines, context);
                return cellLines.Where(l => l.Length > 0).ToList();
            }).Where(c => c.Count > 0).ToList();

            if (cells.Count == 0)
                continue;

            if (cells.Count > 1 && cells.All(c => c.Count == 1 && !c[0].StartsWith("- ", StringComparison.Ordinal)))
            {
                lines.Add(string.Join(" | ", cells.Select(c => c[0]))); // a one-line row such as "Role ... | dates"
            }
            else
            {
                foreach (var cell in cells)
                {
                    lines.AddRange(cell);
                    lines.Add("");
                }
            }
        }
    }

    private static int? HeadingLevel(string? styleId, Context context)
    {
        if (string.IsNullOrEmpty(styleId))
            return null;
        if (styleId.Equals("Title", StringComparison.OrdinalIgnoreCase))
            return 1;
        var name = StyleName(styleId, context) ?? styleId;
        if (HeadingStyle().Match(name) is { Success: true } m || HeadingStyle().Match(styleId) is { Success: true })
        {
            var digits = (HeadingStyle().Match(name).Success ? HeadingStyle().Match(name) : HeadingStyle().Match(styleId)).Groups[1].Value;
            return Math.Clamp(int.Parse(digits) + 1, 2, 4); // Title is "#", Heading 1 is "##"
        }

        return null;
    }

    private static string? StyleName(string styleId, Context context) =>
        context.Styles?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == styleId)?.StyleName?.Val?.Value;

    private static bool IsListItem(Paragraph paragraph, string? styleId, Context context)
    {
        var direct = paragraph.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
        if (direct is not null)
            return direct.Value != 0; // numId 0 switches the numbering off

        // The list can come from the paragraph's style ("List Bullet", "List Paragraph" with numbering), possibly through a parent style.
        var current = styleId;
        for (var depth = 0; depth < 6 && !string.IsNullOrEmpty(current); depth++)
        {
            var style = context.Styles?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == current);
            if (style is null)
                break;
            if (style.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value is { } numId)
                return numId != 0;
            current = style.BasedOn?.Val?.Value;
        }

        return false;
    }

    private static string Text(Paragraph paragraph, Context context) => Text(paragraph, context, out _);

    private static string Text(Paragraph paragraph, Context context, out List<Paragraph> textBoxParagraphs)
    {
        var builder = new StringBuilder();
        textBoxParagraphs = [];
        Collect(paragraph, builder, textBoxParagraphs);
        return builder.ToString();
    }

    private static void Collect(OpenXmlElement element, StringBuilder text, List<Paragraph> textBoxParagraphs)
    {
        foreach (var child in element.ChildElements)
        {
            switch (child)
            {
                case Text t:
                    text.Append(t.Text);
                    break;
                case TabChar:
                    text.Append(' ');
                    break;
                case Break br when br.Type is null || br.Type.Value == BreakValues.TextWrapping:
                    text.Append('\n');
                    break;
                case CarriageReturn:
                    text.Append('\n');
                    break;
                case NoBreakHyphen:
                    text.Append('-');
                    break;
                case TextBoxContent box:
                    textBoxParagraphs.AddRange(box.Descendants<Paragraph>());
                    break;
                case ParagraphProperties or RunProperties or DeletedRun or DeletedText or FieldCode:
                    break;
                default:
                    Collect(child, text, textBoxParagraphs);
                    break;
            }
        }
    }

    private static string Clean(string text) =>
        Regex.Replace(text.Replace(' ', ' ').Replace("­", ""), @"[ \t]+", " ").Trim();
}
