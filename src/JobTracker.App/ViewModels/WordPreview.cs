using System.IO;
using JobTracker.Core;
using JobTracker.Core.Export;

namespace JobTracker.App.ViewModels;

/// <summary>A Word document that has been made and is ready to look at, open or download.</summary>
/// <param name="SuggestedName">The upload-ready name ("First_Last_Resume.docx"): what the file is called when it is the resume in use, and what it is called when downloaded.</param>
/// <param name="Heading">What it is, for the window: "Version 3 of your resume".</param>
/// <param name="UseAsResume">Set for a version that is not the resume in use yet: makes it the resume (and so makes the upload-ready file). Null for the resume in use.</param>
public sealed record WordPreview(DocxExport Export, string SuggestedName, string Heading, Action? UseAsResume = null);

public static class WordPreviews
{
    /// <summary>
    /// The upload-ready file for the resume that is in use: copied or laid out, named First_Last_Resume.docx, in the application's folder. This is the
    /// same file that is made when a resume is chosen. Returns null (after telling the person why) when it cannot be written, usually because it is open in Word.
    /// </summary>
    public static WordPreview? MakeCurrent(MainViewModel main, JobApplication app, string heading) =>
        Guard(main, DownloadNames.Suggest(main.Store.ReadResume(app)), () =>
        {
            var export = CurrentResumeWord.Refresh(main.Store, app);
            if (export is null)
            {
                main.ShowToast("Your resume is empty, so there is no Word file to make");
                return null;
            }

            return new WordPreview(export, Path.GetFileName(export.Path), heading);
        });

    /// <summary>
    /// A Word document for text that is not the resume in use (a saved version, or the resume built from scratch). A version is written to its own place
    /// (<paramref name="relativeOutput"/>) so that it never replaces the upload-ready file of the resume in use.
    /// </summary>
    public static WordPreview? Make(MainViewModel main, string applicationFolder, string resumeText, string heading, string? relativeOutput = null, Action? useAsResume = null)
    {
        var suggested = DownloadNames.Suggest(resumeText);
        var output = Path.Combine(applicationFolder, relativeOutput ?? suggested);
        return Guard(main, Path.GetFileName(output), () =>
            new WordPreview(ResumeDocxExporter.Export(applicationFolder, resumeText, output), suggested, heading, useAsResume));
    }

    private static WordPreview? Guard(MainViewModel main, string fileName, Func<WordPreview?> make)
    {
        // Editing a Word file, reading it back and checking it takes a moment on a big resume: show the busy cursor so it is not taken for a hang.
        var cursor = System.Windows.Input.Mouse.OverrideCursor;
        System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try
        {
            return make();
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

        return null;
    }
}