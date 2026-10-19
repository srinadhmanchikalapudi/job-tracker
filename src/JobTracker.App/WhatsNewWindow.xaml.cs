using System.Windows;
using JobTracker.App.ViewModels;

namespace JobTracker.App;

/// <summary>Shows what changed in each version, inside the app, so nobody has to open GitHub for it.</summary>
public partial class WhatsNewWindow : Window
{
    public WhatsNewWindow(IReadOnlyList<WhatsNewEntry> entries, string subtitle)
    {
        InitializeComponent();
        Subtitle.Text = subtitle;
        Versions.ItemsSource = entries;
        EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
