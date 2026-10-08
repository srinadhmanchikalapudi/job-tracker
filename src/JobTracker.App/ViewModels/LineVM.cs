using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JobTracker.Core;

namespace JobTracker.App.ViewModels;

/// <summary>One line of a JD or resume: a heading, bullet, paragraph or blank spacer.</summary>
public sealed partial class LineVM(DocumentVM document, StructuredLine line, bool isMatch, bool isStarred) : ObservableObject
{
    /// <summary>The line as stored in the file. Used for starring and copying.</summary>
    public string Text { get; } = line.Raw;

    /// <summary>What the viewer shows (no leading dash or "#").</summary>
    public string Display { get; } = line.Display;

    public LineKind Kind { get; } = line.Kind;
    public bool IsActionable { get; } = line.IsActionable;
    public bool IsMatch { get; } = isMatch;

    /// <summary>Nested bullets keep their indentation from the file.</summary>
    public System.Windows.Thickness IndentMargin { get; } = new(
        line.Kind == LineKind.Bullet ? Math.Min(line.Raw.Length - line.Raw.TrimStart().Length, 8) * 5 : 0, 0, 0, 0);

    [ObservableProperty] private bool _isStarred = isStarred;

    // The document refreshes IsStarred for every line after toggling, so repeated lines stay in sync.
    [RelayCommand]
    private void ToggleStar() => document.ToggleStar(Text);

    [RelayCommand]
    private void Copy() => document.Copy(Text);
}
