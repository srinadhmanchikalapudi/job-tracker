namespace JobTracker.Core.Export;

/// <summary>Suggests a file name for the Word document the person downloads: "Alex Rivera - Resume - Northwind Labs.docx".</summary>
public static class DownloadNames
{
    public static string Suggest(string resumeText, string company)
    {
        var parts = new List<string>();
        if (Tailoring.ResumeStructure.PersonName(resumeText) is { } person)
            parts.Add(person);
        parts.Add("Resume");
        if (company.Trim().Length > 0)
            parts.Add(company.Trim());
        return FolderNamer.Sanitize(string.Join(" - ", parts)) + ".docx";
    }
}
