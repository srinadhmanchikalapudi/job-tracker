using System.Windows;
using System.Windows.Controls;
using JobTracker.Core;

namespace JobTracker.App;

public partial class NewApplicationWindow : Window
{
    // What the extractor last put in each box. A box is only overwritten while it is empty or still
    // holds that value, so anything the user typed or corrected is never replaced.
    private string? _autoCompany;
    private string? _autoRole;
    private string? _autoUrl;
    private string? _sourceUrl;

    public NewApplicationWindow()
    {
        InitializeComponent();
        DateBox.SelectedDate = DateTime.Today;
        DataObject.AddPastingHandler(JdBox, JdBox_Pasting);
    }

    /// <summary>Set when the user clicks Save.</summary>
    public NewApplication? Result { get; private set; }

    private void JdBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        // Copying from Chrome/Edge also puts the page address in the HTML clipboard header.
        if (e.DataObject.GetDataPresent(DataFormats.Html) && e.DataObject.GetData(DataFormats.Html) is string html)
            _sourceUrl = JdExtractor.SourceUrlFromHtmlClipboard(html) ?? _sourceUrl;
    }

    private void JdBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var found = JdExtractor.Extract(JdBox.Text, _sourceUrl);
        var filled = new List<string>();

        if (Fill(CompanyBox, found.Company, ref _autoCompany)) filled.Add("company");
        if (Fill(RoleBox, found.Role, ref _autoRole)) filled.Add("role");
        if (Fill(UrlBox, found.Url, ref _autoUrl)) filled.Add("link");

        if (filled.Count > 0)
            AutoFillNote.Text = $"Filled {string.Join(", ", filled)} from the job description. Please check.";

        RefreshCleanup();
    }

    private static bool Fill(TextBox box, string? value, ref string? lastAuto)
    {
        if (string.IsNullOrEmpty(value) || value == box.Text)
            return false;
        if (box.Text.Length > 0 && box.Text != lastAuto)
            return false;

        box.Text = value;
        lastAuto = value;
        return true;
    }

    private void CleanBox_Changed(object sender, RoutedEventArgs e) => RefreshCleanup();

    private void PreviewToggle_Changed(object sender, RoutedEventArgs e) => RefreshCleanup();

    private void RefreshCleanup()
    {
        // Controls raise events while InitializeComponent is still building the tree.
        if (JdBox is null || PreviewBox is null || CleanBox is null || PreviewToggle is null || CleanupNote is null)
            return;

        var cleaning = CleanBox.IsChecked == true;
        var cleaned = JdCleaner.Clean(JdBox.Text);

        CleanupNote.Text = cleaning && cleaned.RemovedLines > 0
            ? $"Clean-up will drop {cleaned.RemovedLines} lines of page clutter."
            : "";

        var showPreview = PreviewToggle.IsChecked == true;
        PreviewBox.Text = cleaning ? cleaned.Text : JdBox.Text;
        PreviewBox.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        JdBox.Visibility = showPreview ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DateBox_Changed(object? sender, SelectionChangedEventArgs e) => UpdateFolderPreview();

    private void Fields_Changed(object sender, TextChangedEventArgs e) => UpdateFolderPreview();

    private void UpdateFolderPreview()
    {
        if (CompanyBox is null || RoleBox is null || SaveButton is null || DateBox is null)
            return;

        var ready = !string.IsNullOrWhiteSpace(CompanyBox.Text) && !string.IsNullOrWhiteSpace(RoleBox.Text);
        SaveButton.IsEnabled = ready;

        var company = FolderNamer.Sanitize(CompanyBox.Text);
        var role = FolderNamer.ApplicationFolderName(DateOnly.FromDateTime(DateBox.SelectedDate ?? DateTime.Today), RoleBox.Text);
        FolderNote.Text = ready ? $@"Saves to {company}\{role}" : "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var raw = JdBox.Text;
        var jd = CleanBox.IsChecked == true ? JdCleaner.Clean(raw).Text : raw;

        Result = new NewApplication(
            CompanyBox.Text.Trim(),
            RoleBox.Text.Trim(),
            jd,
            ResumeBox.Text,
            UrlBox.Text,
            DateOnly.FromDateTime(DateBox.SelectedDate ?? DateTime.Today),
            OriginalJd: jd == raw ? null : raw);
        DialogResult = true;
    }
}
