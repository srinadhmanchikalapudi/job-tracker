using System.Text;

namespace JobTracker.Core.Resumes;

/// <param name="Text">The resume as text, one bullet per line ("- text"), ready to review and save as resume.md.</param>
/// <param name="FileKind">"PDF", "Word", "Markdown" or "Text".</param>
/// <param name="Warnings">Things the person should look at before saving (for example a two-column layout).</param>
/// <param name="NoTextLayer">True for a PDF that is only pictures of text. There is no OCR, so there is nothing to read.</param>
public sealed record ResumeExtraction(string Text, string FileKind, IReadOnlyList<string> Warnings, bool NoTextLayer);

/// <summary>A resume file could not be read. The message is written to be shown to the person.</summary>
public sealed class ResumeReadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Turns a resume file (PDF, Word .docx, Markdown or text) into the plain text Job Tracker keeps as resume.md, keeping the structure that
/// matters (headings, one bullet per line, wrapped lines joined). There is no OCR: a scanned PDF is reported, not guessed at.
/// </summary>
public static class ResumeFileReader
{
    public const long MaxBytes = 25L * 1024 * 1024;

    public static readonly IReadOnlyList<string> SupportedExtensions = [".pdf", ".docx", ".md", ".txt"];

    /// <summary>The filter for an Open File dialog.</summary>
    public const string DialogFilter = "Resume files (*.pdf;*.docx;*.md;*.txt)|*.pdf;*.docx;*.md;*.txt|All files (*.*)|*.*";

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <exception cref="ResumeReadException">The file is missing, too big, an unsupported type, damaged or protected.</exception>
    public static ResumeExtraction Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new ResumeReadException("That file could not be found.");

        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".doc")
            throw new ResumeReadException("Old Word files (.doc) cannot be read. In Word, choose Save As and pick Word Document (.docx) or PDF, then attach that.");
        if (!SupportedExtensions.Contains(extension))
            throw new ResumeReadException("That kind of file cannot be read. Use a PDF, a Word document (.docx), or a Markdown or text file.");

        long length;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch (IOException ex)
        {
            throw new ResumeReadException("That file could not be opened: " + ex.Message, ex);
        }

        if (length == 0)
            throw new ResumeReadException("That file is empty.");
        if (length > MaxBytes)
            throw new ResumeReadException($"That file is {length / 1048576.0:N0} MB, which is far too big for a resume (the limit is {MaxBytes / 1048576} MB).");

        try
        {
            return extension switch
            {
                ".pdf" => PdfResumeReader.Read(path),
                ".docx" => DocxResumeReader.Read(path),
                _ => ReadText(path, extension == ".md" ? "Markdown" : "Text"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ResumeReadException("That file could not be opened (is it open in another program?): " + ex.Message, ex);
        }
    }

    /// <summary>Reads a whole file even when another program (Word, a PDF viewer) has it open.</summary>
    internal static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static ResumeExtraction ReadText(string path, string kind)
    {
        var bytes = ReadShared(path);
        string text;
        if (bytes is [0xFF, 0xFE, ..])
        {
            text = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2); // Notepad's "Unicode" (UTF-16)
        }
        else if (bytes is [0xFE, 0xFF, ..])
        {
            text = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else
        {
            try
            {
                text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // Not UTF-8: an older text file saved by Notepad in the Windows code page.
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                text = Encoding.GetEncoding(1252).GetString(bytes);
            }
        }

        text = text.TrimStart('\uFEFF'); // a UTF-8 byte-order mark decodes to an invisible character that would sit in front of the first line

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(ResumeText.NormaliseBulletGlyph);
        return new ResumeExtraction(ResumeText.Finish(lines), kind, [], false);
    }
}
