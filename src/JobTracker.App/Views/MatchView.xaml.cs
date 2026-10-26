using System.Windows;
using System.Windows.Controls;
using JobTracker.App.ViewModels;

namespace JobTracker.App.Views;

public partial class MatchView : UserControl
{
    public MatchView() => InitializeComponent();

    // "I've used it" on a chip: the skill comes from the chip.
    private void MarkUsed_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MatchVM match && (sender as FrameworkElement)?.DataContext is TermVM term)
            Confirm(match, term.Skill);
    }

    // "I've used it" in the detail panel: the skill is the one that was clicked.
    private void DetailConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MatchVM { Selected: { } term } match)
            Confirm(match, term.Skill);
    }

    private void Confirm(MatchVM match, string skill)
    {
        var dialog = new SkillNoteWindow(skill) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true)
            match.Attest(skill, dialog.Note);
    }

    // "Write a bullet…": opens the Improve window ready for this one requirement. Nothing is sent until the person clicks Write new bullets there.
    private void DetailWrite_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MatchVM { Selected: { } term } match)
            return;

        var tailor = new TailorVM(match);
        tailor.PrepareForSkill(term.Skill);
        new TailorWindow(tailor) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private void DetailUndo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MatchVM { Selected: { } term })
            term.UndoCommand.Execute(null);
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e) => (DataContext as MatchVM)?.CloseDetailCommand.Execute(null);

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
