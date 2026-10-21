using JobTracker.Core.Resumes;

namespace JobTracker.Core.Export;

public enum DocxMethod
{
    /// <summary>A copy of the person's own Word file with only their changes made in it.</summary>
    EditedOriginal,

    /// <summary>A clean single-column document laid out from the resume text.</summary>
    Generated,
}

/// <param name="Note">One or two sentences for the person: how the file was made and, if their layout could not be kept, why.</param>
/// <param name="Differences">Empty when the finished file, read back, says exactly what the resume text says.</param>
public sealed record DocxExport(string Path, DocxMethod Method, string Note, IReadOnlyList<string> Differences)
{
    public bool Verified => Differences.Count == 0;
}

/// <summary>
/// Makes the Word document for a resume. If the person attached a Word file it is edited in place (a copy), so their look is kept; otherwise,
/// or when that cannot be done with certainty, a clean ATS-friendly document is laid out. Either way the finished file is read back and compared
/// with the resume text, so a mistake is caught here and not by a hiring manager.
/// </summary>
public static class ResumeDocxExporter
{
    public static DocxExport Export(string applicationFolder, string resumeText, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        string? whyNotOriginal = null;

        var original = Directory.Exists(applicationFolder)
            ? Directory.EnumerateFiles(applicationFolder, ApplicationStore.OriginalResumePrefix + ".docx").FirstOrDefault()
            : null;

        if (original is not null)
        {
            var attempt = TryEditOriginal(original, resumeText, outputPath, out whyNotOriginal);
            if (attempt is not null)
                return attempt;
        }

        DocxResumeWriter.Write(outputPath, resumeText);
        var differences = Check(outputPath, resumeText);
        var note = "A clean one-column Word document laid out from your resume text, with a standard font and real headings and bullets, which applicant tracking systems read reliably.";
        if (whyNotOriginal is not null)
            note += $" Your own Word layout could not be kept: {whyNotOriginal}";
        else if (original is null)
            note += " Your original file is not a Word document, so its layout is not used.";
        return new DocxExport(outputPath, DocxMethod.Generated, note, differences);
    }

    private static DocxExport? TryEditOriginal(string original, string resumeText, string outputPath, out string? why)
    {
        why = null;
        string baseline;
        try
        {
            baseline = ResumeFileReader.Read(original).Text;
        }
        catch (ResumeReadException ex)
        {
            why = ex.Message;
            return null;
        }

        if (ResumeTextCompare.Differences(baseline, resumeText).Count == 0)
        {
            File.WriteAllBytes(outputPath, ResumeFileReader.ReadShared(original));
            return new DocxExport(outputPath, DocxMethod.EditedOriginal, "Your own Word file, unchanged: there are no edits to make.", []);
        }

        if (!DocxResumePatcher.TryPatch(original, baseline, resumeText, outputPath, out var reason))
        {
            why = reason;
            TryDelete(outputPath);
            return null;
        }

        var differences = Check(outputPath, resumeText);
        if (differences.Count > 0)
        {
            why = "Editing it did not come out right, so it was not used. " + differences[0];
            TryDelete(outputPath);
            return null;
        }

        return new DocxExport(outputPath, DocxMethod.EditedOriginal,
            "A copy of your own Word file with only your changes made in it. Your fonts and layout are kept, and your original file is untouched.", []);
    }

    private static IReadOnlyList<string> Check(string docxPath, string resumeText)
    {
        try
        {
            return ResumeTextCompare.Differences(resumeText, ResumeFileReader.Read(docxPath).Text);
        }
        catch (ResumeReadException ex)
        {
            return [ex.Message];
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a leftover temp file is harmless; the next export overwrites it
        }
    }
}
