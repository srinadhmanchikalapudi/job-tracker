using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using JobTracker.Core.Tailoring;

namespace JobTracker.App.Views;

/// <summary>Shows a word-by-word before and after: new words highlighted, removed words struck through.</summary>
public sealed class DiffTextBlock : TextBlock
{
    public static readonly DependencyProperty PartsProperty = DependencyProperty.Register(
        nameof(Parts), typeof(IReadOnlyList<DiffPart>), typeof(DiffTextBlock), new PropertyMetadata(null, (d, _) => ((DiffTextBlock)d).Rebuild()));

    private static readonly Brush AddedBackground = Frozen(new SolidColorBrush(Color.FromArgb(0x4D, 0x16, 0xA3, 0x4A)));
    private static readonly Brush RemovedForeground = Frozen(new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)));

    public DiffTextBlock() => TextWrapping = TextWrapping.Wrap;

    public IReadOnlyList<DiffPart>? Parts
    {
        get => (IReadOnlyList<DiffPart>?)GetValue(PartsProperty);
        set => SetValue(PartsProperty, value);
    }

    private void Rebuild()
    {
        Inlines.Clear();
        foreach (var part in Parts ?? [])
        {
            var run = new Run(part.Text);
            switch (part.Kind)
            {
                case DiffKind.Added:
                    run.Background = AddedBackground;
                    run.FontWeight = FontWeights.SemiBold;
                    break;
                case DiffKind.Removed:
                    run.TextDecorations = System.Windows.TextDecorations.Strikethrough;
                    run.Foreground = RemovedForeground;
                    break;
            }

            Inlines.Add(run);
            Inlines.Add(new Run(" "));
        }
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
