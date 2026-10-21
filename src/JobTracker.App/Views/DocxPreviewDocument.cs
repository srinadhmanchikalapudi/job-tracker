using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using JobTracker.Core.Export;

namespace JobTracker.App.Views;

/// <summary>Draws the paragraphs read from a .docx as a page on screen: the same text, bold, italic, sizes, headings and bullets, in black on white.</summary>
public static class DocxPreviewDocument
{
    private const double PointsToPixels = 96.0 / 72.0;

    public static FlowDocument Build(IReadOnlyList<PreviewBlock> blocks)
    {
        var document = new FlowDocument
        {
            PageWidth = 816, // 8.5 inches
            PagePadding = new Thickness(72, 64, 72, 64),
            ColumnWidth = 10000,
            FontFamily = new FontFamily("Calibri, Segoe UI"),
            FontSize = 10.5 * PointsToPixels,
            Foreground = Brushes.Black,
            Background = Brushes.White,
            IsOptimalParagraphEnabled = false,
        };

        foreach (var block in blocks)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 3), LineHeight = double.NaN };
            paragraph.TextAlignment = block.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left };

            switch (block.Kind)
            {
                case PreviewBlockKind.Heading:
                    paragraph.Margin = new Thickness(0, 12, 0, 4);
                    paragraph.BorderBrush = Brushes.Gray;
                    paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
                    paragraph.Padding = new Thickness(0, 0, 0, 1);
                    break;
                case PreviewBlockKind.SubHeading:
                    paragraph.Margin = new Thickness(0, 7, 0, 0);
                    break;
                case PreviewBlockKind.Bullet:
                    paragraph.Margin = new Thickness(18 * (block.Level + 1), 0, 0, 2);
                    paragraph.TextIndent = -14;
                    paragraph.Inlines.Add(new Run((block.Level == 0 ? "•" : "–") + "   "));
                    break;
            }

            foreach (var part in block.Runs)
            {
                var run = new Run(part.Caps ? part.Text.ToUpperInvariant() : part.Text)
                {
                    FontWeight = part.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = part.Italic ? FontStyles.Italic : FontStyles.Normal,
                };
                if (part.SizePoints is { } size)
                    run.FontSize = size * PointsToPixels;
                paragraph.Inlines.Add(run);
            }

            document.Blocks.Add(paragraph);
        }

        return document;
    }
}
