using System.IO;
using JobTracker.Core.Export;

namespace JobTracker.App.ViewModels;

/// <summary>A Word document that has been made and is ready to look at, open or download.</summary>
/// <param name="SuggestedName">The upload-ready name of the file ("First_Last_Resume.docx"), used both where it is saved and when it is downloaded.</param>
/// <param name="Heading">What it is, for the window: "Version 3 of your resume".</param>
public sealed record WordPreview(DocxExport Export, string SuggestedName, string Heading);

public static class WordPreviews
{
    /// <summary>
    /// Makes the Word document for a piece of resume text, saving it in the application's folder under the upload-ready name. Each preview rewrites that file, so it always matches the one last shown. Returns null (after telling the person why)
    /// if it cannot be written, which happens when the same file is open in Word.
    /// </summary>
    public static WordPreview? Make(MainViewModel main, string applicationFolder, string resumeText, string heading)
    {
        WordPreview? preview = null;
        var fileName = DownloadNames.Suggest(resumeText);
        // Editing a Word file, reading it back and checking it takes a moment on a big resume: show the busy cursor so it is not taken for a hang.
        var cursor = System.Windows.Input.Mouse.OverrideCursor;
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try
        {
            var export = ResumeDocxExporter.Export(applicationFolder, resumeText, Path.Combine(applicationFolder, fileName));
            preview = new WordPreview(export, fileName, heading);
        }
        catch (IOException)
        {
            main.ShowToast($"{fileName} is open in another program. Close it there and try again");
        }
        catch (UnauthorizedAccessException ex)
        {
            main.ShowToast("Could not write the Word file: " + ex.Message);
        }
        finally
        {
            System.Windows.Input.Mouse.OverrideCursor = cursor;
        }

        return preview;
    }
}
