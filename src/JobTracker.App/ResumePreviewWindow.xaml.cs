using System.Diagnostics;
using System.IO;
using System.Windows;
using JobTracker.App.ViewModels;
using JobTracker.App.Views;
using JobTracker.Core.Export;
using Microsoft.Win32;

namespace JobTracker.App;

/// <summary>
/// Shows the finished Word document on screen, and lets the person open its folder, open it in Word, or download a copy. The preview is read from the
/// real .docx, so it shows what was written, not what was intended.
/// </summary>
public partial class ResumePreviewWindow : Window
{
    // One notch of the mouse wheel moves the page by this many pixels per 120 units of wheel movement. WPF's own step is about 48, which feels slow
    // on a page this long.
    private const double WheelPixelsPerNotch = 200;

    private readonly WordPreview _preview;
    private string? _downloadedTo;

    public ResumePreviewWindow(WordPreview preview)
    {
        InitializeComponent();
        _preview = preview;

        HeadingText.Text = preview.Heading;
        Title = "Word document: " + preview.Heading;
        NoteText.Text = preview.Export.Note;
        PathBox.Text = preview.Export.Path;
        NameText.Inlines.Add(new System.Windows.Documents.Run("Ready to upload or attach: ") { Foreground = System.Windows.Media.Brushes.Gray });
        NameText.Inlines.Add(new System.Windows.Documents.Run(preview.SuggestedName) { FontWeight = FontWeights.SemiBold });
        NameText.Inlines.Add(new System.Windows.Documents.Run("  Select it in the folder and press Ctrl+C, or click Copy file.") { Foreground = System.Windows.Media.Brushes.Gray });
        StatusText.Text = preview.Export.Verified ? "Checked: the file says exactly what your resume says." : "";

        if (!preview.Export.Verified)
        {
            WarningBox.Visibility = Visibility.Visible;
            WarningText.Text = "Read back, the finished file does not say exactly what your resume says, so check it before you send it:\n• " +
                               string.Join("\n• ", preview.Export.Differences);
        }

        Viewer.PreviewMouseWheel += Viewer_PreviewMouseWheel;

        try
        {
            Viewer.Document = DocxPreviewDocument.Build(DocxPreviewModel.Read(preview.Export.Path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or JobTracker.Core.Resumes.ResumeReadException)
        {
            StatusText.Text = "The preview could not be drawn, but the file was made. Use Open in Word to look at it. (" + ex.Message + ")";
        }
    }

    private void Viewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        Viewer.ApplyTemplate();
        if (Viewer.Template.FindName("PART_ContentHost", Viewer) is not System.Windows.Controls.ScrollViewer scroller)
            return;

        scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta / 120.0 * WheelPixelsPerNotch);
        e.Handled = true;
    }

    /// <summary>The folder window with the file selected.</summary>
    private void OpenLocation_Click(object sender, RoutedEventArgs e) => Reveal(_preview.Export.Path);

    private void ShowCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadedTo is not null)
            Reveal(_downloadedTo);
    }

    private static void Reveal(string path)
    {
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path } });
    }

    /// <summary>Puts the file itself on the clipboard, the same as selecting it in File Explorer and pressing Ctrl+C.</summary>
    private void CopyFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var files = new System.Collections.Specialized.StringCollection { _preview.Export.Path };
            Clipboard.SetFileDropList(files);
            StatusText.Text = $"Copied {_preview.SuggestedName}. Paste it into an email or an upload box.";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            StatusText.Text = "Windows would not let this program use the clipboard just now. Try again, or select the file in its folder and press Ctrl+C.";
        }
    }

    private void OpenInWord_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_preview.Export.Path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            StatusText.Text = "Windows could not open it: " + ex.Message;
        }
    }

    private void Download_Click(object sender, RoutedEventArgs e)
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var dialog = new SaveFileDialog
        {
            Title = "Download your resume as a Word document",
            FileName = _preview.SuggestedName,
            DefaultExt = ".docx",
            Filter = "Word document (*.docx)|*.docx",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.Copy(_preview.Export.Path, dialog.FileName, overwrite: true);
            _downloadedTo = dialog.FileName;
            StatusText.Text = "Saved a copy to " + dialog.FileName;
            ShowCopyButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = "Could not save the copy there: " + ex.Message;
        }
    }
}
