using System.IO;
using JobTracker.Core.Export;

namespace JobTracker.App.ViewModels;

/// <summary>A Word document that has been made and is ready to look at, open or download.</summary>
/// <param name="SuggestedName">The file name offered when the person downloads it.</param>
/// <param name="Heading">What it is, for the window: "Version 3 of your resume".</param>
public sealed record WordPreview(DocxExport Export, string SuggestedName, string Heading);

public static class WordPreviews
{
    /// <summary>
    /// Makes the Word document for a piece of resume text, saving it in the application's folder. Returns null (after telling the person why)
    /// if it cannot be written, which happens when the same file is open in Word.
    /// </summary>
    public static WordPreview? Make(MainViewModel main, string applicationFolder, string company, string resumeText, string fileName, string heading)
    {
        WordPreview? preview = null;
        try
        {
            var export = ResumeDocxExporter.Export(applicationFolder, resumeText, Path.Combine(applicationFolder, fileName));
            preview = new WordPreview(export, DownloadNames.Suggest(resumeText, company), heading);
        }
        catch (IOException)
        {
            main.ShowToast($"{fileName} is open in another program. Close it there and try again");
        }
        catch (UnauthorizedAccessException ex)
        {
            main.ShowToast("Could not write the Word file: " + ex.Message);
        }

        return preview;
    }
}
