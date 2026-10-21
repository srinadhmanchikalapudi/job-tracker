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
        viewModel.RefreshBackupStatus(); // OneDrive may have been started or stopped since the last time
        Loaded += (_, _) => viewModel.LoadModelsOnOpen();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private async void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = "Choose the folder where applications are saved",
            InitialDirectory = Directory.Exists(Vm.DataRoot) ? Vm.DataRoot : null,
        };
        if (picker.ShowDialog(this) == true)
            await ApplyFolderAsync(picker.FolderName, offerSubfolder: true);
    }

    /// <summary>The folder OneDrive keeps online: Documents when it is synced, otherwise the OneDrive folder itself.</summary>
    private async void MoveToOneDrive_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.BackupSuggestedFolder is { } folder)
            await ApplyFolderAsync(folder, offerSubfolder: false);
    }

    // Choosing from the list should take effect straight away, not when the box next loses focus.
    private void ModelBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && sender is System.Windows.Controls.ComboBox box)
        {
            box.Text = (string)e.AddedItems[0]!;
            System.Windows.Data.BindingOperations.GetBindingExpression(box, System.Windows.Controls.ComboBox.TextProperty)?.UpdateSource();
        }
    }

    private void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        if (KeyBox.Password.Trim().Length == 0)
            return;
        try
        {
            Vm.SaveAiKey(KeyBox.Password);
            KeyBox.Clear();
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, ex.Message, "Job Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveKey_Click(object sender, RoutedEventArgs e) => Vm.RemoveAiKey();

    private void WhatsNew_Click(object sender, RoutedEventArgs e) =>
        new WhatsNewWindow(Vm.BuildWhatsNew(), Vm.VersionText) { Owner = this }.ShowDialog();

    /// <summary>
    /// Switch to <paramref name="chosen"/>. If the current folder holds applications, ask whether to move them. Moving copies and checks
    /// everything before the old files are removed, so a failure leaves the applications exactly where they were.
    /// </summary>
    private async Task ApplyFolderAsync(string chosen, bool offerSubfolder)
    {
        var vm = Vm;

        // A folder that already holds other things (Desktop, Documents): keep the company folders in a folder of their own.
        if (offerSubfolder && DataRootMover.Validate(vm.DataRoot, chosen) is null && DataRootAdvisor.ShouldSuggestSubfolder(chosen))
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
        if (vm.HasMovableData)
        {
            var dialog = new MoveFilesWindow(vm.DataRoot, chosen, vm.ApplicationCount, DataRootAdvisor.LooksLikeDataFolder(chosen), vm.HasBuiltResume) { Owner = this };
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