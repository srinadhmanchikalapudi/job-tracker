using System.Windows;
using JobTracker.App.ViewModels;

namespace JobTracker.App;

public partial class ResumeBuilderWindow : Window
{
    private readonly ResumeBuilderVM _vm;

    public ResumeBuilderWindow(ResumeBuilderVM viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        _vm.ConfirmSend = destination =>
            MessageBox.Show(this,
                $"To write bullets, Job Tracker sends the answers you typed and the title, company and bullets of this job to {destination}.\n\n" +
                "Nothing else from your resume is sent. Your key stays in Windows Credential Manager. You are only asked this once for this service.\n\nSend it?",
                "Send text to an AI service?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        DataContext = _vm;
        Closing += (_, _) => _vm.Flush();
    }

    private void JobUp_Click(object sender, RoutedEventArgs e) => _vm.MoveSelectedJob(-1);

    private void JobDown_Click(object sender, RoutedEventArgs e) => _vm.MoveSelectedJob(1);

    private void RemoveJob_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedJob is not { } job)
            return;
        if (MessageBox.Show(this, $"Remove \"{job.Heading}\" and its {job.Bullets.Count} bullet(s) from your resume?", "Remove job",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            _vm.RemoveSelectedJob();
    }

    private void RemoveSchool_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedSchool is not { } school)
            return;
        if (MessageBox.Show(this, $"Remove \"{school.Heading}\" from your resume?", "Remove school",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            _vm.RemoveSelectedSchool();
    }

    private void PreviewWord_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.PrepareWordPreview() is { } preview)
            new ResumePreviewWindow(preview) { Owner = this }.ShowDialog();
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetDataObject(_vm.PreviewText, copy: true);
            SaveStatusToast("Copied the resume text");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            SaveStatusToast("The clipboard is busy, try again");
        }
    }

    private void SaveStatusToast(string message) => _vm.SaveStatus = message;

    private void StartOver_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Delete this resume and start again with a blank one? This cannot be undone.", "Start over",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
            _vm.StartOver();
    }
}
