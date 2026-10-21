using System.Text.RegularExpressions;

namespace JobTracker.Core.Tailoring;

/// <param name="LineNumber">1-based line in the resume text as it is now.</param>
/// <param name="Raw">The line as written, with its bullet marker and indent.</param>
/// <param name="Text">The words of the bullet, without the marker.</param>
public sealed record ResumeBullet(int LineNumber, string Raw, string Text);

/// <param name="Before">The words the line had, or null for a bullet that was added.</param>
public sealed record BulletChange(int LineNumber, string? Before, string After);

/// <summary>
/// A working copy of a resume while it is being tailored. Only the lines the person accepted change, and added bullets are new lines; every other
/// character stays as it was. Nothing is written to disk here: the person saves the result as a new version.
/// </summary>
public sealed class TailorSession
{
    private static readonly Regex Marker = new(@"^(?<indent>\s*(?:[-*•·▪●○◦‣]\s+)?)(?<text>.*?)(?<tail>\r?)$", RegexOptions.Compiled);

    private sealed class Line(string text, bool added)
    {
        public string Text { get; set; } = text;

        /// <summary>The line as it was in the saved resume (null for an added line).</summary>
        public string? Original { get; } = added ? null : text;

        public bool IsAdded { get; } = added;
        public bool IsChanged => IsAdded || Original != Text;
    }

    private readonly List<Line> _lines;
    private readonly string _originalText;

    public TailorSession(string resumeText)
    {
        _originalText = resumeText;
        _lines = resumeText.Split('\n').Select(l => new Line(l, added: false)).ToList();
    }

    /// <summary>The resume as it is now, with the accepted changes in it.</summary>
    public string Text => string.Join("\n", _lines.Select(l => l.Text));

    public string OriginalText => _originalText;

    public bool HasChanges => _lines.Any(l => l.IsChanged);

    public IReadOnlyList<BulletChange> Changes =>
        _lines.Select((l, i) => (l, i)).Where(x => x.l.IsChanged)
            .Select(x => new BulletChange(x.i + 1, x.l.IsAdded ? null : BulletText.Clean(x.l.Original!), BulletText.Clean(x.l.Text))).ToList();

    /// <summary>The lines that can be reworded: bullets, or (for a resume with no bullet marks) sentences of six words or more.</summary>
    public IReadOnlyList<ResumeBullet> Bullets
    {
        get
        {
            var structured = BulletText.Structure(Text);
            var bullets = structured.Where(l => l.Kind == LineKind.Bullet).ToList();
            if (bullets.Count == 0)
                bullets = structured.Where(l => l.Kind == LineKind.Text && l.Display.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 6).ToList();
            return bullets.Select(l => new ResumeBullet(l.LineNumber, l.Raw, l.Display)).ToList();
        }
    }

    /// <summary>The jobs of the resume as it is now, each with its bullets.</summary>
    public IReadOnlyList<ResumeJob> Jobs => ResumeStructure.Jobs(Text);

    public bool IsChanged(int lineNumber) => lineNumber >= 1 && lineNumber <= _lines.Count && _lines[lineNumber - 1].IsChanged;

    public bool IsAdded(int lineNumber) => lineNumber >= 1 && lineNumber <= _lines.Count && _lines[lineNumber - 1].IsAdded;

    /// <summary>Replaces the words of one line, keeping its bullet marker and indent.</summary>
    public void Apply(int lineNumber, string newText)
    {
        var line = At(lineNumber);
        var parts = Marker.Match(line.Text);
        line.Text = parts.Groups["indent"].Value + newText.Trim() + parts.Groups["tail"].Value;
    }

    /// <summary>
    /// Adds a bullet after <paramref name="afterLine"/>, with the same marker and indent as that line (a plain "- " if it has none).
    /// Line numbers after it move down by one. Returns the new bullet's line number.
    /// </summary>
    public int InsertAfter(int afterLine, string newText)
    {
        var reference = At(afterLine);
        var prefix = Marker.Match(reference.Text).Groups["indent"].Value;
        if (prefix.Trim().Length == 0)
            prefix = prefix + "- ";
        var tail = reference.Text.EndsWith('\r') ? "\r" : "";
        _lines.Insert(afterLine, new Line(prefix + newText.Trim() + tail, added: true));
        return afterLine + 1;
    }

    /// <summary>Puts a changed line back, or removes an added one.</summary>
    public void Undo(int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > _lines.Count)
            return;
        var line = _lines[lineNumber - 1];
        if (line.IsAdded)
            _lines.RemoveAt(lineNumber - 1);
        else
            line.Text = line.Original!;
    }

    private Line At(int lineNumber) =>
        lineNumber >= 1 && lineNumber <= _lines.Count ? _lines[lineNumber - 1] : throw new ArgumentOutOfRangeException(nameof(lineNumber));
}
