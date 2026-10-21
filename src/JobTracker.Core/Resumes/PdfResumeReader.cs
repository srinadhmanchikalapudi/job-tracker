using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;

namespace JobTracker.Core.Resumes;

/// <summary>
/// Reads the text layer of a PDF and rebuilds the lines a person sees: words are grouped into lines by baseline, a bullet mark starts a new
/// bullet, and the lines that wrap under it (the hanging indent) are joined back into it. A page laid out in two columns is read one column at a time.
/// A PDF with no text at all (pictures of text) is reported as such: there is no OCR.
/// </summary>
internal static partial class PdfResumeReader
{
    private const double HeaderFraction = 0.12; // the top of the page, read before the columns (name and contact line)

    [GeneratedRegex(@"[­]")]
    private static partial Regex SoftHyphen();

    public static ResumeExtraction Read(string path)
    {
        var warnings = new List<string>();
        try
        {
            using var document = PdfDocument.Open(ResumeFileReader.ReadShared(path), new ParsingOptions { UseLenientParsing = true });
            var lines = new List<string>();
            var pagesWithText = 0;
            var twoColumns = false;

            foreach (var page in document.GetPages())
            {
                var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
                if (words.Count == 0)
                    continue;

                pagesWithText++;
                var pageLines = ReadPage(page, words, out var columns);
                twoColumns |= columns;
                if (lines.Count > 0 && pageLines.Count > 0)
                    lines.Add("");
                lines.AddRange(pageLines);
            }

            if (pagesWithText == 0)
            {
                warnings.Add("This PDF has no text in it: it is only pictures of the pages, probably a scan. Job Tracker does not do OCR. " +
                             "Paste the text instead, or attach a PDF saved from Word or another program.");
                return new ResumeExtraction("", "PDF", warnings, NoTextLayer: true);
            }

            if (pagesWithText < document.NumberOfPages)
                warnings.Add($"{document.NumberOfPages - pagesWithText} of {document.NumberOfPages} pages have no text (pictures only), so they were skipped.");
            if (twoColumns)
                warnings.Add("This PDF has two columns. They were read one after the other: check that the order is right.");
            if (document.NumberOfPages > 6)
                warnings.Add($"This PDF has {document.NumberOfPages} pages, which is long for a resume. Check that it is the right file.");

            return new ResumeExtraction(ResumeText.Finish(lines), "PDF", warnings, NoTextLayer: false);
        }
        catch (PdfDocumentEncryptedException ex)
        {
            throw new ResumeReadException("This PDF is password protected. Save an unprotected copy, or paste the text instead.", ex);
        }
        catch (Exception ex) when (ex is not ResumeReadException and not OperationCanceledException)
        {
            throw new ResumeReadException(
                "This PDF could not be read; it may be damaged. Save it again from the program that made it, or paste the text instead.", ex);
        }
    }

    private sealed class PdfLine
    {
        public required List<Word> Words { get; init; }
        public double Left { get; init; }
        public double Right { get; init; }
        public double Baseline { get; init; }
        public double Size { get; init; }
        public bool IsBullet { get; set; }
        public double TextLeft { get; set; }
        public string Text { get; set; } = "";
    }

    private static List<string> ReadPage(Page page, List<Word> words, out bool twoColumns)
    {
        twoColumns = false;
        var headerBottom = page.Height * (1 - HeaderFraction);
        var gutter = FindGutter(page, words.Where(w => w.BoundingBox.Top < headerBottom).ToList());

        if (gutter is null)
            return Assemble(BuildLines(words));

        // Two columns. Lines that run across the gutter with ordinary word spacing are full-width (the name, a heading across the page) and are
        // read first; everything else is split at the gutter and read as the left column, then the right column.
        twoColumns = true;
        var allLines = BuildLines(words);
        var spanning = allLines.Where(l => Spans(l, gutter.Value)).ToList();
        var columnWords = allLines.Where(l => !Spans(l, gutter.Value)).SelectMany(l => l.Words).ToList();
        var result = new List<string>();
        foreach (var block in new[]
                 {
                     Assemble(spanning),
                     Assemble(BuildLines(columnWords.Where(w => Center(w) < gutter).ToList())),
                     Assemble(BuildLines(columnWords.Where(w => Center(w) >= gutter).ToList())),
                 })
        {
            if (block.Count == 0)
                continue;
            if (result.Count > 0)
                result.Add("");
            result.AddRange(block);
        }

        return result;
    }

    /// <summary>
    /// True when the line runs straight across the gutter: a word sits on it, or the words either side are an ordinary word space apart.
    /// The first lines of two columns that merely share a baseline have a wide empty gap there instead.
    /// </summary>
    private static bool Spans(PdfLine line, double gutter)
    {
        for (var i = 0; i < line.Words.Count; i++)
        {
            var word = line.Words[i].BoundingBox;
            if (word.Left < gutter && word.Right > gutter)
                return true;
            if (i + 1 < line.Words.Count && Center(line.Words[i]) < gutter && Center(line.Words[i + 1]) >= gutter
                && line.Words[i + 1].BoundingBox.Left - word.Right < 8)
                return true;
        }

        return false;
    }

    private static double Center(Word w) => (w.BoundingBox.Left + w.BoundingBox.Right) / 2;

    /// <summary>
    /// The x position of an empty vertical corridor that splits the page into two columns of text, or null for a normal single column.
    /// Only a corridor that is empty for the whole height of the body counts, so a right-aligned date on one line never does.
    /// </summary>
    private static double? FindGutter(Page page, List<Word> body)
    {
        if (body.Count < 25)
            return null;

        var spans = body.Select(w => (Left: w.BoundingBox.Left, Right: w.BoundingBox.Right)).OrderBy(s => s.Left).ToList();
        double reach = spans[0].Right, bestGap = 0, bestCenter = 0;
        foreach (var span in spans.Skip(1))
        {
            if (span.Left > reach)
            {
                var gap = span.Left - reach;
                var center = (span.Left + reach) / 2;
                if (gap > bestGap && center > page.Width * 0.15 && center < page.Width * 0.85)
                {
                    bestGap = gap;
                    bestCenter = center;
                }
            }

            reach = Math.Max(reach, span.Right);
        }

        if (bestGap < Math.Max(12, page.Width * 0.02))
            return null;

        // Both sides must hold real text, but a sidebar can be small (a handful of skills).
        var left = body.Count(w => Center(w) < bestCenter);
        var right = body.Count - left;
        return Math.Min(left, right) >= Math.Max(3, body.Count * 0.04) ? bestCenter : null;
    }

    private static List<PdfLine> BuildLines(List<Word> words)
    {
        var lines = new List<PdfLine>();
        var ordered = words.OrderByDescending(Baseline).ThenBy(w => w.BoundingBox.Left).ToList();
        var current = new List<Word>();
        double currentBaseline = 0;

        void Flush()
        {
            if (current.Count > 0)
                lines.Add(MakeLine(current));
            current = [];
        }

        foreach (var word in ordered)
        {
            var size = Size(word);
            if (current.Count > 0 && Math.Abs(Baseline(word) - currentBaseline) > Math.Max(2, size * 0.4))
                Flush();
            if (current.Count == 0)
                currentBaseline = Baseline(word);
            current.Add(word);
        }

        Flush();
        return lines;
    }

    private static double Baseline(Word w) => w.Letters.Count > 0 ? w.Letters[0].StartBaseLine.Y : w.BoundingBox.Bottom;

    private static double Size(Word w) => w.Letters.Count > 0 && w.Letters.Max(l => l.PointSize) is var s and > 0 ? s : Math.Max(1, w.BoundingBox.Height);

    private static PdfLine MakeLine(List<Word> unsorted)
    {
        var words = unsorted.OrderBy(w => w.BoundingBox.Left).ToList();
        var line = new PdfLine
        {
            Words = words,
            Left = words[0].BoundingBox.Left,
            Right = words.Max(w => w.BoundingBox.Right),
            Baseline = words.Average(Baseline),
            Size = words.Max(Size),
        };

        var first = words[0].Text;
        var texts = words.Select(w => SoftHyphen().Replace(w.Text, "")).ToList();
        line.TextLeft = line.Left;

        if (ResumeText.IsBulletToken(first) && words.Count > 1 && words[1].BoundingBox.Left - words[0].BoundingBox.Right > 1)
        {
            // A bullet mark standing alone, then the text.
            line.IsBullet = true;
            line.TextLeft = words[1].BoundingBox.Left;
            texts.RemoveAt(0);
        }
        else if (first.Length > 1 && ResumeText.IsBulletChar(first[0]) && !char.IsLetterOrDigit(first[0]))
        {
            // The bullet mark glued to the first word ("•Managed ...").
            line.IsBullet = true;
            line.TextLeft = words[0].Letters.Count > 1 ? words[0].Letters[1].StartBaseLine.X : line.Left;
            texts[0] = texts[0][1..];
        }

        line.Text = string.Join(' ', texts.Where(t => t.Length > 0));
        return line;
    }

    private static void InferBulletsFromIndent(List<PdfLine> lines, double columnLeft, double columnRight)
    {
        var width = columnRight - columnLeft;
        var centre = (columnLeft + columnRight) / 2;
        PdfLine? previous = null;
        foreach (var line in lines)
        {
            var indented = line.Left - columnLeft >= 9;
            var centred = Math.Abs((line.Left + line.Right) / 2 - centre) <= 6; // a centred line is not indented, it is centred
            if (indented && !centred)
            {
                // The line above ran to the right edge and starts at the same indent: this is its wrapped second line, not a new bullet.
                var wrapsFromPrevious = previous is { } above
                    && Math.Abs(above.Left - line.Left) <= 3
                    && above.Right >= columnLeft + width * 0.86
                    && above.Baseline - line.Baseline <= 1.9 * above.Size;
                if (!wrapsFromPrevious)
                {
                    line.IsBullet = true;
                    line.TextLeft = line.Left;
                }
            }

            previous = line;
        }
    }

    private sealed class Block
    {
        public required List<string> Parts { get; init; }
        public required bool IsBullet { get; init; }
        public required double FirstLeft { get; init; }
        public required double TextLeft { get; init; }
        public required double FirstBaseline { get; init; }
        public required PdfLine Last { get; set; }
    }

    private static List<string> Assemble(List<PdfLine> lines)
    {
        var output = new List<string>();
        if (lines.Count == 0)
            return output;

        var columnLeft = lines.Min(l => l.Left);
        var columnRight = lines.Max(l => l.Right);
        var columnWidth = Math.Max(1, columnRight - columnLeft);

        // Some programs (Chromium, for one) draw the bullet dot as a shape, so the text has no bullet character at all. The list is still there
        // as an indented block, so when a page has no bullet marks, indentation says where the bullets are.
        if (!lines.Any(l => l.IsBullet))
            InferBulletsFromIndent(lines, columnLeft, columnRight);

        Block? block = null;
        PdfLine? previousBlockLast = null;

        void Flush()
        {
            if (block is null)
                return;

            // A gap bigger than a normal line break before a paragraph (not a bullet) becomes a blank line.
            if (output.Count > 0 && previousBlockLast is not null && !block.IsBullet)
            {
                var gap = previousBlockLast.Baseline - block.FirstBaseline;
                if (gap > 1.5 * previousBlockLast.Size)
                    output.Add("");
            }

            var text = Join(block.Parts);
            output.Add(block.IsBullet ? "- " + text : text);
            previousBlockLast = block.Last;
            block = null;
        }

        foreach (var line in lines)
        {
            if (line.Text.Length == 0)
                continue;

            if (block is not null && !line.IsBullet && IsContinuation(block, line, columnLeft, columnWidth))
            {
                block.Parts.Add(line.Text);
                block.Last = line;
                continue;
            }

            Flush();
            block = new Block
            {
                Parts = [line.Text],
                IsBullet = line.IsBullet,
                FirstLeft = line.Left,
                TextLeft = line.TextLeft,
                FirstBaseline = line.Baseline,
                Last = line,
            };
        }

        Flush();
        return output;
    }

    private static bool IsContinuation(Block block, PdfLine line, double columnLeft, double columnWidth)
    {
        var previous = block.Last;
        var gap = previous.Baseline - line.Baseline;
        if (gap > 1.9 * previous.Size || gap < 0)
            return false; // a paragraph break (or the next column starting again at the top)
        if (Math.Abs(line.Size - previous.Size) > 1.0)
            return false; // a heading or a different kind of line

        if (block.IsBullet)
            return Math.Abs(line.Left - block.TextLeft) <= 6 || (line.Left > block.FirstLeft + 1 && line.Left >= block.TextLeft - 6);

        // A paragraph wraps when the line above it ran to the right edge and this one starts at the same left edge.
        var ranToTheEdge = previous.Right >= columnLeft + columnWidth * 0.86;
        return ranToTheEdge && Math.Abs(line.Left - block.FirstLeft) <= 6;
    }

    /// <summary>Joins wrapped lines with a space, but not after a hyphen at the end of a line ("cross-" + "functional").</summary>
    private static string Join(List<string> parts)
    {
        var text = parts[0];
        foreach (var part in parts.Skip(1))
            text = text.EndsWith('-') && part.Length > 0 && char.IsLetter(part[0]) ? text + part : text + " " + part;
        return text;
    }
}
