using System.IO;
using System.Windows;
using JobTracker.App.ViewModels;
using JobTracker.Core;
using Microsoft.Win32;

namespace JobTracker.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>
    /// Pick a folder, then (if the current one holds applications) choose to move them. Moving copies and checks everything before the old
    /// files are removed, so a failure leaves the applications exactly where they were.
    /// </summary>
    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        var picker = new OpenFolderDialog
        {
            Title = "Choose the folder where applications are saved",
            InitialDirectory = Directory.Exists(vm.DataRoot) ? vm.DataRoot : null,
        };
        if (picker.ShowDialog(this) != true)
            return;

        var chosen = picker.FolderName;

        // A folder that already holds other things (Desktop, Documents): keep the company folders in a folder of their own.
        if (DataRootMover.Validate(vm.DataRoot, chosen) is null && DataRootAdvisor.ShouldSuggestSubfolder(chosen))
        {
            var answer = MessageBox.Show(this,
                $"{chosen} already has other files in it.\n\nCreate a folder named \"{DataRootAdvisor.SubfolderName}\" inside it and use that, so your applications stay together?",
                "Choose where to save", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
            if (answer == MessageBoxResult.Cancel)
                return;
            if (answer == MessageBoxResult.Yes)
                chosen = DataRootAdvisor.Suggested(chosen);
        }

        if (DataRootMover.Validate(vm.DataRoot, chosen) is { } problem)
        {
            MessageBox.Show(this, problem, "Choose where to save", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var move = false;
        if (vm.ApplicationCount > 0)
        {
            var dialog = new MoveFilesWindow(vm.DataRoot, chosen, vm.ApplicationCount, DataRootAdvisor.LooksLikeDataFolder(chosen)) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Choice == MoveChoice.Cancel)
                return;
            move = dialog.Choice == MoveChoice.Move;
        }

        var result = await vm.ChangeDataRootAsync(chosen, move);
        if (result.Success)
            vm.ShowToast(result.Message.Split('\n')[0]);
        if (!result.Success || result.Message.Contains('\n'))
            MessageBox.Show(this, result.Message, "Job Tracker",
                MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Closing in the middle of a move would leave the old files in place and a half-written copy; ask the person to wait.
        if (DataContext is MainViewModel { IsMovingFiles: true })
        {
            e.Cancel = true;
            MessageBox.Show(this, "Job Tracker is still moving your applications. Please wait until it finishes.", "Job Tracker",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        base.OnClosing(e);
    }
}