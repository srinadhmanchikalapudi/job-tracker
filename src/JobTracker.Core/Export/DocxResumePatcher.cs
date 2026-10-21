using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JobTracker.Core.Resumes;

namespace JobTracker.Core.Export;

/// <summary>
/// Edits a copy of the person's own Word resume so it says the new text, changing only the lines that changed: a reworded bullet keeps the
/// formatting of the bullet it replaces, and an added bullet is a copy of the bullet above it. Fonts, spacing, colours and layout stay as they were.
/// It only does this when every change can be placed with certainty; otherwise it says why not and the caller makes a clean document instead.
/// </summary>
public static partial class DocxResumePatcher
{
    [GeneratedRegex(@"^\s*[•●○◦▪■□▫◆◇►▶➤➢✓✔∙‣⁃\-–—*]\s+")]
    private static partial Regex TypedMarker();

    private enum OpKind { Replace, Delete, Insert }

    private sealed record Op(OpKind Kind, int BaseIndex, string Text, int AnchorBase);

    /// <summary>Writes the edited copy to <paramref name="outputPath"/> and returns true, or returns false with the reason.</summary>
    public static bool TryPatch(string originalPath, string baselineText, string finalText, string outputPath, out string reason)
    {
        reason = "";
        var baseRaw = NonBlank(baselineText);
        var finalRaw = NonBlank(finalText);
        var baseNorm = baseRaw.Select(ResumeTextCompare.Normalize).ToList();
        var finalNorm = finalRaw.Select(ResumeTextCompare.Normalize).ToList();

        try
        {
            File.WriteAllBytes(outputPath, ResumeFileReader.ReadShared(originalPath)); // a copy, even if Word has the original open
            using var document = WordprocessingDocument.Open(outputPath, isEditable: true);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
            {
                reason = "The original Word file has no body to edit.";
                return false;
            }

            var paragraphs = body.Descendants<Paragraph>().Where(p => ParagraphText(p).Length > 0).ToList();
            var paragraphNorm = paragraphs.Select(p => ResumeTextCompare.Normalize(ParagraphText(p))).ToList();
            var map = MapLines(baseNorm, paragraphNorm);

            var inserted = new Dictionary<int, OpenXmlElement>(); // the last paragraph added after each anchor, so several additions stay in order
            foreach (var op in Diff(baseNorm, finalNorm, finalRaw))
            {
                if (op.Kind == OpKind.Insert)
                {
                    if (op.AnchorBase < 0)
                    {
                        reason = "A new line would have to go before the first line.";
                        return false;
                    }

                    if (!Find(map, paragraphs, op.AnchorBase, baseRaw, out var anchor, out reason))
                        return false;
                    if (!baseRaw[op.AnchorBase].TrimStart().StartsWith("- ", StringComparison.Ordinal))
                    {
                        reason = "A new bullet would have to follow a line that is not a bullet.";
                        return false;
                    }

                    var clone = (Paragraph)anchor.CloneNode(true);
                    if (!SetText(clone, op.Text, out reason))
                        return false;
                    (inserted.TryGetValue(op.AnchorBase, out var last) ? last : anchor).InsertAfterSelf(clone);
                    inserted[op.AnchorBase] = clone;
                    continue;
                }

                if (!Find(map, paragraphs, op.BaseIndex, baseRaw, out var paragraph, out reason))
                    return false;

                if (op.Kind == OpKind.Replace)
                {
                    if (!SetText(paragraph, op.Text, out reason))
                        return false;
                }
                else
                {
                    if (paragraph.Parent is TableCell cell && cell.Elements<Paragraph>().Count() == 1)
                    {
                        reason = "A line to remove is the only text in a table cell.";
                        return false;
                    }

                    paragraph.Remove();
                }
            }

            document.MainDocumentPart!.Document!.Save();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OpenXmlPackageException or InvalidOperationException)
        {
            reason = "The original Word file could not be edited: " + ex.Message;
            return false;
        }
    }

    private static bool Find(int[] map, List<Paragraph> paragraphs, int line, List<string> baseRaw, out Paragraph paragraph, out string reason)
    {
        if (map[line] < 0)
        {
            paragraph = null!;
            reason = $"The line “{Shorten(ResumeTextCompare.Normalize(baseRaw[line]))}” could not be found in your Word file.";
            return false;
        }

        paragraph = paragraphs[map[line]];
        reason = "";
        return true;
    }
    // ------------------------------------------------------------------ lining the text up with the document

    private static List<string> NonBlank(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Where(l => ResumeTextCompare.Normalize(l).Length > 0).ToList();

    /// <summary>For each line of the original text, the paragraph that holds it (in document order), or -1 when there is no such paragraph.</summary>
    private static int[] MapLines(List<string> baseNorm, List<string> paragraphNorm)
    {
        var map = new int[baseNorm.Count];
        var next = 0;
        for (var i = 0; i < baseNorm.Count; i++)
        {
            map[i] = -1;
            for (var k = next; k < paragraphNorm.Count; k++)
            {
                if (paragraphNorm[k] == baseNorm[i])
                {
                    map[i] = k;
                    next = k + 1;
                    break;
                }
            }
        }

        return map;
    }

    /// <summary>Line-by-line difference between the original text and the new text, as replace / delete / insert steps.</summary>
    private static List<Op> Diff(List<string> a, List<string> b, List<string> finalRaw)
    {
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var ops = new List<Op>();
        var removed = new List<int>();
        var added = new List<int>();
        var lastBase = -1;

        void Flush()
        {
            var pairs = Math.Min(removed.Count, added.Count);
            for (var k = 0; k < pairs; k++)
            {
                ops.Add(new Op(OpKind.Replace, removed[k], BulletText.Clean(finalRaw[added[k]]), -1));
                lastBase = removed[k];
            }

            foreach (var r in removed.Skip(pairs))
                ops.Add(new Op(OpKind.Delete, r, "", -1));
            foreach (var n in added.Skip(pairs))
                ops.Add(new Op(OpKind.Insert, -1, BulletText.Clean(finalRaw[n]), lastBase));
            removed.Clear();
            added.Clear();
        }

        int x = 0, y = 0;
        while (x < a.Count && y < b.Count)
        {
            if (a[x] == b[y])
            {
                Flush();
                lastBase = x;
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                removed.Add(x++);
            }
            else
            {
                added.Add(y++);
            }
        }

        while (x < a.Count)
            removed.Add(x++);
        while (y < b.Count)
            added.Add(y++);
        Flush();
        return ops;
    }

    // ------------------------------------------------------------------ editing a paragraph

    internal static string ParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Descendants().Select(e => e switch
        {
            Text t => t.Text,
            TabChar => " ",
            Break => " ",
            _ => "",
        })).Trim();

    /// <summary>Puts new words in a paragraph, keeping the formatting of the words it had. Refuses paragraphs with mixed formatting, links or fields.</summary>
    private static bool SetText(Paragraph paragraph, string newText, out string reason)
    {
        reason = "";
        var runs = paragraph.ChildElements.OfType<Run>().ToList();
        var textRuns = runs.Where(r => r.Elements<Text>().Any()).ToList();

        var complex = paragraph.ChildElements.Any(c => c is not (ParagraphProperties or Run or ProofError or BookmarkStart or BookmarkEnd))
                      || runs.Any(r => r.ChildElements.Any(c => c is not (RunProperties or Text or TabChar or LastRenderedPageBreak)));
        if (complex)
        {
            reason = $"The line “{Shorten(ParagraphText(paragraph))}” has a link, field or picture in it, which cannot be edited safely.";
            return false;
        }

        if (textRuns.Count == 0)
        {
            reason = "A line to change has no text run to edit.";
            return false;
        }

        var formats = textRuns.Select(r => r.RunProperties?.OuterXml ?? "").Distinct().Count();
        if (formats > 1)
        {
            reason = $"The line “{Shorten(ParagraphText(paragraph))}” mixes formatting (for example a bold lead-in), which would be flattened.";
            return false;
        }

        // A bullet typed as a character at the start of the text stays.
        var marker = TypedMarker().Match(string.Concat(textRuns.SelectMany(r => r.Elements<Text>().Select(t => t.Text))));
        var first = textRuns[0];
        foreach (var run in textRuns.Skip(1))
            run.Remove();
        foreach (var child in first.ChildElements.Where(c => c is not RunProperties).ToList())
            child.Remove();
        first.Append(new Text((marker.Success ? marker.Value : "") + newText) { Space = SpaceProcessingModeValues.Preserve });
        return true;
    }

    private static string Shorten(string s) => s.Length > 60 ? s[..60] + "…" : s;

}
