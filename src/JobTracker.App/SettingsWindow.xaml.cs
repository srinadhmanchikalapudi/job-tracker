using System.IO;
using System.Windows;
using JobTracker.App.ViewModels;
using Microsoft.Win32;

namespace JobTracker.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ChangeFolder_Click(object sender, RoutedEventArgs e)
    {
        var vm = (MainViewModel)DataContext;
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the folder where applications are saved",
            InitialDirectory = Directory.Exists(vm.DataRoot) ? vm.DataRoot : null,
        };
        if (dialog.ShowDialog(this) == true)
            vm.ChangeDataRoot(dialog.FolderName);
    }
}
