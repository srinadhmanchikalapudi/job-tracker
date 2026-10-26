using System.Text.RegularExpressions;

namespace JobTracker.Core.Tailoring;

/// <param name="Text">The line as shown (bullet marks removed).</param>
/// <param name="Role">What kind of line it is: a section heading, a job heading, a bullet, or plain text.</param>
/// <param name="Section">The heading it sits under ("Experience", "Technical Skills"), or empty.</param>
/// <param name="InSkillsList">True when the line is in a skills section: a keyword listed there is read as a mention, not as work the person did.</param>
public sealed record ResumeLine(string Text, LineRole Role, string Section, bool InSkillsList);

/// <summary>Reads a resume into lines that know which section they are in, so a skill can be told apart from a skill that was used.</summary>
public static partial class ResumeLineReader
{
    [GeneratedRegex(@"skill|technolog|competenc|tools|tech stack|proficienc", RegexOptions.IgnoreCase)]
    private static partial Regex SkillsHeading();

    public static IReadOnlyList<ResumeLine> Read(string resumeText)
    {
        var result = new List<ResumeLine>();
        var section = "";
        foreach (var (line, role) in ResumeStructure.Annotate(resumeText).Select(r => (r.Line, r.Role)))
        {
            if (role == LineRole.Section)
            {
                section = line.Display.Trim();
                continue; // a heading is not evidence of anything
            }

            result.Add(new ResumeLine(line.Display, role, section, SkillsHeading().IsMatch(section)));
        }

        return result;
    }
}