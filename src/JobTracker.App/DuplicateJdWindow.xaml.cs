using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using JobTracker.Core;

namespace JobTracker.App;

public enum DuplicateChoice
{
    /// <summary>Close this window and stay in New application, with everything still typed in.</summary>
    GoBack,

    /// <summary>It is a different role: save it as well.</summary>
    SaveAnyway,

    /// <summary>Do not save; show the application that is already there.</summary>
    OpenSaved,
}

/// <summary>
/// Shown when the job description being saved is close to one already saved for the same company. Shows both, with the lines that are only in one of
/// them highlighted, and lets the person decide. It never saves or deletes anything itself.
/// </summary>
public partial class DuplicateJdWindow : Window
{
    private static readonly Brush OnlyHereBackground = Frozen(new SolidColorBrush(Color.FromArgb(0x4D, 0xF5, 0x9E, 0x0B)));

    private readonly string _newRole;
    private readonly string _newJd;
    private readonly IReadOnlyList<SimilarJd> _matches;

    public DuplicateJdWindow(string company, string newRole, string newJd, IReadOnlyList<SimilarJd> matches)
    {
        InitializeComponent();
        _newRole = newRole;
        _newJd = newJd;
        _matches = matches;

        var closest = matches[0];
        SubHeading.Text = matches.Count == 1
            ? $"{company} already has a saved application whose job description is {closest.Percent}% the same. If it is the same job posted again, you may not need a second copy."
            : $"{company} already has {matches.Count} saved applications with job descriptions that are {matches[^1].Percent}% to {closest.Percent}% the same. If it is the same job posted again, you may not need another copy.";

        MatchBox.ItemsSource = matches.Select(Describe).ToList();
        PickLabel.Visibility = MatchBox.Visibility = matches.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        MatchBox.SelectedIndex = 0;
    }

    public DuplicateChoice Choice { get; private set; } = DuplicateChoice.GoBack;

    /// <summary>The saved application being compared with (the one chosen in the list), for "Open the saved one".</summary>
    public JobApplication Selected => _matches[Math.Max(0, MatchBox.SelectedIndex)].Application;

    private static string Describe(SimilarJd s) => $"{s.Application.Meta.Role} · {s.Application.Meta.AppliedDate:yyyy-MM-dd} · {s.Percent}% the same";

    private void MatchBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var match = _matches[Math.Max(0, MatchBox.SelectedIndex)];
        var meta = match.Application.Meta;
        SavedTitle.Text = $"{meta.Role}, applied {meta.AppliedDate:yyyy-MM-dd} ({meta.Status}) · {match.Percent}% the same";
        NewTitle.Text = string.IsNullOrWhiteSpace(_newRole) ? "Not saved yet" : $"{_newRole}, not saved yet" +
            (string.Equals(_newRole.Trim(), meta.Role.Trim(), StringComparison.OrdinalIgnoreCase) ? " · same role title" : "");

        Fill(SavedText, JdSimilarity.MarkLines(match.JdText, _newJd));
        Fill(NewText, JdSimilarity.MarkLines(_newJd, match.JdText));
    }

    private static void Fill(TextBlock target, IReadOnlyList<JdLine> lines)
    {
        target.Inlines.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            var run = new Run(lines[i].Text.Length == 0 ? " " : lines[i].Text);
            if (lines[i].OnlyHere)
                run.Background = OnlyHereBackground;
            target.Inlines.Add(run);
            if (i < lines.Count - 1)
                target.Inlines.Add(new LineBreak());
        }
    }

    private void SaveAnyway_Click(object sender, RoutedEventArgs e) => Close(DuplicateChoice.SaveAnyway);

    private void OpenSaved_Click(object sender, RoutedEventArgs e) => Close(DuplicateChoice.OpenSaved);

    private void GoBack_Click(object sender, RoutedEventArgs e) => Close(DuplicateChoice.GoBack);

    private void Close(DuplicateChoice choice)
    {
        Choice = choice;
        DialogResult = choice == DuplicateChoice.SaveAnyway;
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}