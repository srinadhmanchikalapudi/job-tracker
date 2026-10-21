using System.Windows;
using JobTracker.App.ViewModels;

namespace JobTracker.App;

public partial class TailorWindow : Window
{
    public TailorWindow(TailorVM viewModel)
    {
        InitializeComponent();
        viewModel.ConfirmSend = destination =>
            MessageBox.Show(this,
                $"To write a suggestion, Job Tracker sends the bullet you picked, the job description, the skills you chose and any notes you wrote to {destination}.\n\n" +
                "The rest of your resume is not sent. Your key stays in Windows Credential Manager. You are only asked this once for this service.\n\nSend it?",
                "Send text to an AI service?", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        DataContext = viewModel;
    }

    private void PreviewWord_Click(object sender, RoutedEventArgs e)
    {
        if (((TailorVM)DataContext).PrepareWordPreview() is { } preview)
            new ResumePreviewWindow(preview) { Owner = this }.ShowDialog();
    }
}
