using System.Globalization;
using System.Text;

namespace JobTracker.Core.Export;

/// <summary>
/// The file name of the Word document, made to be uploaded or attached as it is: "Priya_Raman_Resume.docx".
/// First and last name, then "Resume", joined with underscores. No spaces (some sites and mail programs turn them into %20), no punctuation, no accents,
/// and no company or version number, because the file goes to that company and a recruiter sees only your name. Which application it belongs to is
/// told by the folder it is saved in.
/// </summary>
public static class DownloadNames
{
    private static readonly HashSet<string> Titles = new(StringComparer.OrdinalIgnoreCase) { "dr", "mr", "mrs", "ms", "miss", "mx", "prof", "sir" };
    private static readonly HashSet<string> Suffixes = new(StringComparer.OrdinalIgnoreCase) { "jr", "sr", "ii", "iii", "iv", "phd", "md", "mba", "pmp", "cpa", "pe", "esq" };

    public static string Suggest(string resumeText)
    {
        var parts = new List<string>(NameParts(Tailoring.ResumeStructure.PersonName(resumeText) ?? ""));
        parts.Add("Resume");
        var name = string.Join("_", parts);
        return FolderNamer.Sanitize(name) + ".docx";
    }

    /// <summary>First and last name from the line at the top of the resume, cleaned for a file name. A middle name or initial is left out.</summary>
    public static IReadOnlyList<string> NameParts(string person)
    {
        var beforeComma = person.Split(',')[0];
        var words = beforeComma.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Clean)
            .Where(w => w.Length > 0 && !Titles.Contains(w) && !Suffixes.Contains(w))
            .ToList();
        // A lone initial ("K.") is not a name worth keeping when there is more than one word.
        var named = words.Where(w => w.Length > 1).ToList();
        if (named.Count == 0)
            return [];
        return named.Count == 1 ? [named[0]] : [named[0], named[^1]];
    }

    private static string Clean(string word)
    {
        var folded = word.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in folded)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
                sb.Append(c);
        }

        return sb.ToString().Trim('-');
    }
}