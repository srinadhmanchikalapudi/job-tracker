using System.Windows;
using System.Windows.Controls;
using JobTracker.App.ViewModels;
using JobTracker.Core;
using JobTracker.Core.Resumes;
using Microsoft.Win32;

namespace JobTracker.App;

public partial class NewApplicationWindow : Window
{
    // What the extractor last put in each box. A box is only overwritten while it is empty or still
    // holds that value, so anything the user typed or corrected is never replaced.
    private string? _autoCompany;
    private string? _autoRole;
    private string? _autoUrl;
    private string? _sourceUrl;

    // Set once the person picks a type themselves, after which the pasted page no longer changes it.
    private bool _jobTypeChosenByUser;
    private bool _settingJobType;

    // The resume file the person attached (its text was read into the box). A copy is kept with the application.
    private string? _attachedResumePath;
    private bool _readingResume;

    // The resume built from scratch (if there is one), offered as a one-click way to fill the resume box.
    private readonly Func<string?>? _builtResume;

    // Looks for saved applications of the same company whose job description is nearly the same (company, job description -> matches).
    private readonly Func<string, string, IReadOnlyList<SimilarJd>>? _findSimilar;

    public NewApplicationWindow(JobType initialJobType = JobType.FullTime, Func<string?>? builtResume = null, Func<string, string, IReadOnlyList<SimilarJd>>? findSimilar = null)
    {
        InitializeComponent();
        _builtResume = builtResume;
        _findSimilar = findSimilar;
        UseBuiltButton.Visibility = builtResume?.Invoke() is null ? Visibility.Collapsed : Visibility.Visible;
        DateBox.SelectedDate = DateTime.Today;
        JobTypeBox.ItemsSource = MainViewModel.JobTypeOptions;
        SetJobType(initialJobType);
        DataObject.AddPastingHandler(JdBox, JdBox_Pasting);
    }

    /// <summary>Set when the user clicks Save.</summary>
    public NewApplication? Result { get; private set; }

    /// <summary>Set when the person chose "Open the saved one" on the possible-duplicate window: nothing was saved, and this is the folder to show.</summary>
    public string? OpenExistingFolder { get; private set; }

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
        if (!_jobTypeChosenByUser && found.JobType is { } detected && detected != SelectedJobType)
        {
            SetJobType(detected);
            filled.Add("job type");
        }

        if (filled.Count > 0)
            AutoFillNote.Text = $"Filled {string.Join(", ", filled)} from the job description. Please check.";

        // Read from what was pasted, not the cleaned copy: a "Pay" box on a job page is the sort of thing clean-up can drop.
        var pay = JobTracker.Core.Pay.PayExtractor.Extract(JdBox.Text);
        PayNote.Text = JdBox.Text.Trim().Length == 0 ? "" : pay is null ? "Pay: not stated in this posting." : $"Pay found: {pay.Display}";

        RefreshCleanup();
    }

    // ---------------------------------------------------------------- the resume: paste it, or attach a file

    private void ResumeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ResumeHint is not null && ResumeBox is not null)
            ResumeHint.Visibility = ResumeBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AttachResume_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Attach your resume", Filter = ResumeFileReader.DialogFilter, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
            await AttachResumeAsync(dialog.FileName);
    }

    private static bool HasResumeFile(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 };

    private void Resume_DragOver(object sender, DragEventArgs e)
    {
        // Only files are handled here; text dragged into the box keeps its normal behaviour.
        if (!HasResumeFile(e))
            return;
        e.Effects = DragDropEffects.Copy;
        DropOverlay.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void Resume_DragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private async void Resume_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
            return;

        e.Handled = true;
        await AttachResumeAsync(files.FirstOrDefault(ResumeFileReader.IsSupported) ?? files[0]);
    }

    /// <summary>Puts the resume built in "My resume" in the box (asking first if the box already has different text).</summary>
    public bool UseBuiltResume(Func<string, bool>? confirmReplace = null)
    {
        if (_builtResume?.Invoke() is not { } text)
            return false;

        var existing = ResumeBox.Text.Trim();
        if (existing.Length > 0 && existing != text.Trim())
        {
            var replace = confirmReplace?.Invoke(existing) ??
                          MessageBox.Show(this, "The resume box already has text. Replace it with the resume you built?", "Use my resume",
                              MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            if (!replace)
                return false;
        }

        // The text no longer comes from an attached file, so the file is not kept as this application's original.
        _attachedResumePath = null;
        AttachmentChip.Visibility = Visibility.Collapsed;
        ShowResumeWarning(null);
        ResumeBox.Text = text.TrimEnd();
        return true;
    }

    private void UseBuilt_Click(object sender, RoutedEventArgs e) => UseBuiltResume();

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        _attachedResumePath = null;
        AttachmentChip.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Reads a resume file and puts its text in the box for the person to check. If the box already holds different text, asks before replacing it.
    /// The file itself is kept with the application when it is saved. Anything the person should look at (two columns, no text layer) is shown.
    /// </summary>
    /// <param name="askToReplace">Decides what to do when the box already has text: true to replace it, false to keep it (and still attach the file), null to cancel.</param>
    public async Task AttachResumeAsync(string path, Func<string, bool?>? askToReplace = null)
    {
        if (_readingResume)
            return;

        _readingResume = true;
        AttachButton.IsEnabled = false;
        ShowResumeWarning(null);

        // Read off the UI thread (a big PDF should not freeze the window), then come back to it explicitly rather than relying on an ambient context.
        (ResumeExtraction? Result, string? Error) read;
        try
        {
            read = await Task.Run(() => TryRead(path)).ConfigureAwait(false);
        }
        catch
        {
            await Dispatcher.InvokeAsync(FinishReading);
            throw;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                ApplyResume(path, read.Result, read.Error, askToReplace);
            }
            finally
            {
                FinishReading();
            }
        });
    }

    private static (ResumeExtraction? Result, string? Error) TryRead(string path)
    {
        try
        {
            return (ResumeFileReader.Read(path), null);
        }
        catch (ResumeReadException ex)
        {
            return (null, ex.Message);
        }
    }

    private void FinishReading()
    {
        _readingResume = false;
        AttachButton.IsEnabled = true;
    }

    private void ApplyResume(string path, ResumeExtraction? result, string? error, Func<string, bool?>? askToReplace)
    {
        if (result is null)
        {
            ShowResumeWarning(error);
            return;
        }

        var name = System.IO.Path.GetFileName(path);
        if (result.Text.Length > 0)
        {
            var existing = ResumeBox.Text.Trim();
            if (existing.Length > 0 && existing != result.Text.Trim())
            {
                var replace = askToReplace is not null ? askToReplace(name) : AskToReplace(name);
                if (replace is null)
                    return; // cancelled: nothing changes
                if (replace == true)
                    ResumeBox.Text = result.Text;
            }
            else
            {
                ResumeBox.Text = result.Text;
            }
        }

        _attachedResumePath = path;
        AttachmentText.Text = $"Attached: {name} ({result.FileKind})";
        AttachmentChip.Visibility = Visibility.Visible;
        ShowResumeWarning(result.Warnings.Count == 0 ? null : string.Join("\n", result.Warnings));
    }
    private bool? AskToReplace(string fileName)
    {
        var answer = MessageBox.Show(this,
            $"The resume box already has text.\n\nReplace it with the text from {fileName}?\n\nYes replaces it. No keeps your text and still attaches the file. Cancel attaches nothing.",
            "Attach resume", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
        return answer switch { MessageBoxResult.Yes => true, MessageBoxResult.No => false, _ => null };
    }

    private void ShowResumeWarning(string? message)
    {
        ResumeWarningText.Text = message ?? "";
        ResumeWarningBox.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private JobType SelectedJobType => JobTypeBox.SelectedValue is JobType type ? type : JobType.FullTime;

    private void SetJobType(JobType type)
    {
        _settingJobType = true;
        JobTypeBox.SelectedValue = type;
        _settingJobType = false;
    }

    private void JobTypeBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingJobType)
            _jobTypeChosenByUser = true;
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

        // Same company, nearly the same description: ask before filing a second copy. Nothing is lost by going back; everything stays typed in.
        if (_findSimilar?.Invoke(CompanyBox.Text.Trim(), jd) is { Count: > 0 } similar)
        {
            var ask = new DuplicateJdWindow(CompanyBox.Text.Trim(), RoleBox.Text.Trim(), jd, similar) { Owner = this };
            ask.ShowDialog();
            if (ask.Choice == DuplicateChoice.GoBack)
                return;
            if (ask.Choice == DuplicateChoice.OpenSaved)
            {
                OpenExistingFolder = ask.Selected.FolderPath;
                DialogResult = false;
                return;
            }
        }

        Result = new NewApplication(
            CompanyBox.Text.Trim(),
            RoleBox.Text.Trim(),
            jd,
            ResumeBox.Text,
            UrlBox.Text,
            DateOnly.FromDateTime(DateBox.SelectedDate ?? DateTime.Today),
            OriginalJd: jd == raw ? null : raw,
            JobType: SelectedJobType,
            ResumeFilePath: _attachedResumePath);
        DialogResult = true;
    }
}
