using System.Windows;
using System.Windows.Threading;

namespace JobTracker.App;

public partial class App : Application
{
    public App()
    {
        // Disk problems (a file locked by OneDrive sync, a deleted folder) should show a message, not crash the app.
        DispatcherUnhandledException += OnUnhandledException;
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Job Tracker", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
