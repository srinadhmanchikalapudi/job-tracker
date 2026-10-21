using System.Windows;
using System.Windows.Controls;
using JobTracker.App.ViewModels;

namespace JobTracker.App.Views;

public partial class MatchView : UserControl
{
    public MatchView() => InitializeComponent();

    private void MarkUsed_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MatchVM match || (sender as FrameworkElement)?.DataContext is not TermVM term)
            return;

        var dialog = new SkillNoteWindow(term.Skill) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            match.Attest(term.Skill, dialog.Note);
    }

    private void Improve_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MatchVM match)
            return;

        var window = new TailorWindow(new TailorVM(match)) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void ResumeWord_Click(object sender, RoutedEventArgs e) => ShowPreview((DataContext as MatchVM)?.PrepareWordPreview(null));

    private void VersionWord_Click(object sender, RoutedEventArgs e) =>
        ShowPreview(((sender as FrameworkElement)?.DataContext as VersionVM)?.PrepareWordPreview());

    private void ShowPreview(WordPreview? preview)
    {
        if (preview is not null)
            new ResumePreviewWindow(preview) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

}
