using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobTracker.Core.Resumes;

namespace JobTracker.Core.Export;

public enum PreviewBlockKind
{
    Title,
    Heading,
    SubHeading,
    Paragraph,
    Bullet,
}

public sealed record PreviewRun(string Text, bool Bold, bool Italic, bool Caps, double? SizePoints);

/// <param name="Level">Nesting of a bullet: 0 for a first-level bullet, 1 for one inside it.</param>
/// <param name="Alignment">"left", "center" or "right".</param>
public sealed record PreviewBlock(PreviewBlockKind Kind, int Level, string Alignment, IReadOnlyList<PreviewRun> Runs)
{
    public string Text => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>
/// Reads a finished .docx into a simple list of paragraphs (text, bold, italic, size, bullets, headings) so the app can show it on screen without
/// Word. It reads the real file, not the text it came from, so what the person sees is what was written.
/// </summary>
public static class DocxPreviewModel
{
    public static IReadOnlyList<PreviewBlock> Read(string path)
    {
        using var stream = new MemoryStream(ResumeFileReader.ReadShared(path));
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        var main = document.MainDocumentPart ?? throw new ResumeReadException("That Word file has no document in it.");
        var styles = main.StyleDefinitionsPart?.Styles;
        var defaultSize = HalfPoints(styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle?.FontSize?.Val?.Value) ?? 10.5;

        var blocks = new List<PreviewBlock>();
        foreach (var paragraph in main.Document!.Body!.Descendants<Paragraph>())
        {
            var runs = paragraph.Descendants<Run>()
                .Select(run => new PreviewRun(
                    string.Concat(run.ChildElements.Select(c => c switch { Text t => t.Text, TabChar => "    ", _ => "" })),
                    Flag(run.RunProperties?.Bold, paragraph, styles, s => s.StyleRunProperties?.Bold),
                    Flag(run.RunProperties?.Italic, paragraph, styles, s => s.StyleRunProperties?.Italic),
                    Flag(run.RunProperties?.Caps, paragraph, styles, s => s.StyleRunProperties?.Caps),
                    HalfPoints(run.RunProperties?.FontSize?.Val?.Value) ?? StyleSize(paragraph, styles) ?? defaultSize))
                .Where(r => r.Text.Length > 0)
                .ToList();
            if (runs.Count == 0)
                continue;

            var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            var name = StyleName(styleId, styles) ?? styleId ?? "";
            var kind = name.Equals("Title", StringComparison.OrdinalIgnoreCase) ? PreviewBlockKind.Title
                : name.StartsWith("heading 1", StringComparison.OrdinalIgnoreCase) ? PreviewBlockKind.Heading
                : name.StartsWith("heading", StringComparison.OrdinalIgnoreCase) ? PreviewBlockKind.SubHeading
                : IsList(paragraph, styles) ? PreviewBlockKind.Bullet
                : PreviewBlockKind.Paragraph;

            var level = kind == PreviewBlockKind.Bullet ? paragraph.ParagraphProperties?.NumberingProperties?.NumberingLevelReference?.Val?.Value ?? 0 : 0;
            blocks.Add(new PreviewBlock(kind, Math.Clamp(level, 0, 3), Alignment(paragraph, styles), runs));
        }

        return blocks;
    }

    private static bool Flag(OnOffType? direct, Paragraph paragraph, Styles? styles, Func<Style, OnOffType?> fromStyle)
    {
        if (direct is not null)
            return direct.Val is null || direct.Val.Value;
        foreach (var style in StyleChain(paragraph, styles))
            if (fromStyle(style) is { } flag)
                return flag.Val is null || flag.Val.Value;
        return false;
    }

    private static double? StyleSize(Paragraph paragraph, Styles? styles) =>
        StyleChain(paragraph, styles).Select(s => HalfPoints(s.StyleRunProperties?.FontSize?.Val?.Value)).FirstOrDefault(v => v is not null);

    private static double? HalfPoints(string? value) => double.TryParse(value, out var half) ? half / 2 : null;

    private static string Alignment(Paragraph paragraph, Styles? styles)
    {
        var value = paragraph.ParagraphProperties?.Justification?.Val?.Value
                    ?? StyleChain(paragraph, styles).Select(s => s.StyleParagraphProperties?.Justification?.Val?.Value).FirstOrDefault(v => v is not null);
        return value == JustificationValues.Center ? "center" : value == JustificationValues.Right ? "right" : "left";
    }

    private static IEnumerable<Style> StyleChain(Paragraph paragraph, Styles? styles)
    {
        var id = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        for (var depth = 0; depth < 6 && !string.IsNullOrEmpty(id); depth++)
        {
            var style = styles?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == id);
            if (style is null)
                yield break;
            yield return style;
            id = style.BasedOn?.Val?.Value;
        }
    }

    private static string? StyleName(string? styleId, Styles? styles) =>
        styleId is null ? null : styles?.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == styleId)?.StyleName?.Val?.Value;

    private static bool IsList(Paragraph paragraph, Styles? styles)
    {
        var direct = paragraph.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
        if (direct is not null)
            return direct.Value != 0;
        return StyleChain(paragraph, styles).Any(s => s.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value is { } id && id != 0);
    }
}
