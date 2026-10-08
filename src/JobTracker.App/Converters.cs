using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace JobTracker.App;

/// <summary>First letter of a company name, for the avatar tile.</summary>
public sealed class InitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string)?.Trim();
        return string.IsNullOrEmpty(text) ? "?" : char.ToUpperInvariant(text[0]).ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A stable gradient per company name, so each company keeps its colour everywhere.</summary>
public sealed class CompanyBrushConverter : IValueConverter
{
    private static readonly (string From, string To)[] Palette =
    [
        ("#6366F1", "#2563EB"), ("#0EA5E9", "#0284C7"), ("#14B8A6", "#0D9488"), ("#10B981", "#059669"),
        ("#F59E0B", "#D97706"), ("#F43F5E", "#E11D48"), ("#A855F7", "#7C3AED"), ("#EC4899", "#DB2777"),
    ];

    private static readonly Brush[] Brushes = Palette.Select(p => (Brush)Freeze(new LinearGradientBrush(
        (Color)ColorConverter.ConvertFromString(p.From), (Color)ColorConverter.ConvertFromString(p.To), 45))).ToArray();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = (value as string)?.Trim().ToLowerInvariant() ?? "";
        var hash = 0;
        foreach (var ch in name)
            hash = (hash * 31 + ch) & 0x7FFFFFFF;
        return Brushes[hash % Brushes.Length];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static T Freeze<T>(T freezable) where T : System.Windows.Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
