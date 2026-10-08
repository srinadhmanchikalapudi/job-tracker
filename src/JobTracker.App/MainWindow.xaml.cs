using System.Windows;
using System.Windows.Input;
using JobTracker.App.ViewModels;
using JobTracker.Core;

namespace JobTracker.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        var settings = AppSettings.Load();
        _vm = new MainViewModel(settings);
        _vm.AttachUpdates(AppInfo.CreateUpdateService(settings));
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.StartupUpdateCheckAsync();
    }

    private void NewApplication_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new NewApplicationWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } input)
            _vm.AddApplication(input);
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        new SettingsWindow(_vm) { Owner = this }.ShowDialog();

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        // An install from here closes the app, so ask first. A copy that was not put in place by the setup program just opens the download page.
        if (_vm.CanInstallInPlace &&
            MessageBox.Show(this, $"Install version {_vm.AvailableVersionText} now? Job Tracker will close, update and start again. Your applications are not touched.",
                "Update Job Tracker", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        await _vm.InstallUpdateAsync();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        if (e.Key == Key.N)
        {
            NewApplication_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.F)
        {
            _vm.SelectedTabIndex = 0;
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }
}
