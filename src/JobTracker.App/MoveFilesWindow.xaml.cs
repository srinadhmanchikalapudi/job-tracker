using System.Windows;

namespace JobTracker.App;

public enum MoveChoice { Cancel, Move, Keep }

/// <summary>Asks what to do with the applications already saved when the folder is changed.</summary>
public partial class MoveFilesWindow : Window
{
    public MoveFilesWindow(string oldRoot, string newRoot, int count, bool newFolderHasApplications)
    {
        InitializeComponent();

        var noun = count == 1 ? "1 application" : $"{count} applications";
        Summary.Text = $"You have {noun} saved in {oldRoot}.\n\nThe new folder is {newRoot}." +
                       (newFolderHasApplications ? " It already holds some applications; they are kept, and anything with the same name is added next to it, never overwritten." : "");
        MoveButton.Content = count == 1 ? "Move 1 application" : $"Move {count} applications";
        KeepText.Text = newFolderHasApplications
            ? "Switches to the new folder and its applications. The applications in the old folder stay where they are and will not appear in Job Tracker unless you switch back."
            : "Starts fresh in the new folder. The applications in the old folder stay where they are and will not appear in Job Tracker unless you switch back.";
    }

    public MoveChoice Choice { get; private set; } = MoveChoice.Cancel;

    private void Move_Click(object sender, RoutedEventArgs e) => Close(MoveChoice.Move);

    private void Keep_Click(object sender, RoutedEventArgs e) => Close(MoveChoice.Keep);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(MoveChoice.Cancel);

    private void Close(MoveChoice choice)
    {
        Choice = choice;
        DialogResult = choice != MoveChoice.Cancel;
    }
}
