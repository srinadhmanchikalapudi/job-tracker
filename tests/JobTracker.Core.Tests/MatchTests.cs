using JobTracker.Core.Tailoring;

namespace JobTracker.Core.Tests;

public class SkillLexiconTests
{
    private static string[] Skills(string text) =>
        SkillLexicon.Default.Find(text).Select(h => h.Skill).Distinct().OrderBy(s => s).ToArray();

    [Fact]
    public void FindsSkillsWrittenWithSymbols() =>
        Assert.Equal([".NET", "C#", "C++"], Skills("Built services in C# and C++ on .NET 8."));

    [Fact]
    public void JavaIsNotJavaScript()
    {
        Assert.Equal(["Java"], Skills("Six years of Java."));
        Assert.Equal(["JavaScript"], Skills("Six years of JavaScript."));
    }

    [Fact]
    public void ShortEverydayWordsOnlyMatchWhenWrittenLikeTheSkill()
    {
        Assert.Empty(Skills("We go further and spark ideas, then spring into action."));
        Assert.Equal(["Go", "Spark", "Spring Boot"], Skills("Services in Go and Spark, plus Spring Boot."));
    }

    [Fact]
    public void AliasesAndPluralsCountAsTheSameSkill()
    {
        Assert.Equal(["Kubernetes"], Skills("Ran k8s clusters."));
        Assert.Equal(["Microservices"], Skills("Split a monolith into three microservice."));
        Assert.Equal(["CI/CD"], Skills("Owned continuous integration for the team."));
        Assert.Equal(["SQL Server"], Skills("Tuned MSSQL indexes.").Where(s => s != "SQL" && s != "Query optimization").ToArray());
    }

    [Fact]
    public void PunctuationAfterASkillDoesNotHideIt() =>
        Assert.Contains("Python", Skills("Languages: Python, Go, and Rust."));

    [Fact]
    public void ASkillInsideALongerWordIsNotAMatch() =>
        Assert.DoesNotContain("SQL", Skills("Worked on PostgreSQL clusters."));

    [Fact]
    public void EverySkillLineParses()
    {
        var all = SkillLexicon.Default.Skills;
        Assert.True(all.Count > 200);
        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

public class JdAnalyzerTests
{
    private const string Jd = """
        # Senior Software Engineer

        ## About us
        We are a friendly company that loves Slack and Figma and Rust.

        ## What you will do
        - Design and operate .NET APIs on SQL Server
        - Move batch jobs onto Kafka

        ## Requirements
        - 6+ years building services in C#
        - Comfortable with Docker

        ## Nice to have
        - Experience with Terraform
        - Healthcare domain knowledge
        """;

    private static JdTerm Term(JdAnalysis a, string skill) => a.Terms.Single(t => t.Skill == skill);

    [Fact]
    public void WeighsSkillsByTheSectionTheyAreIn()
    {
        var a = JdAnalyzer.Analyze(Jd);
        Assert.Equal(TermWeight.Required, Term(a, "C#").Weight);
        Assert.Equal(TermWeight.Required, Term(a, "Docker").Weight);
        Assert.Equal(TermWeight.Duty, Term(a, "SQL Server").Weight);
        Assert.Equal(TermWeight.Duty, Term(a, "Kafka").Weight);
        Assert.Equal(TermWeight.Preferred, Term(a, "Terraform").Weight);
        Assert.Equal(TermWeight.Preferred, Term(a, "Healthcare").Weight);
    }

    [Fact]
    public void IgnoresTheAboutUsSection() =>
        Assert.DoesNotContain(JdAnalyzer.Analyze(Jd).Terms, t => t.Skill is "Figma" or "Rust");

    [Fact]
    public void ListsRequiredFirst()
    {
        var a = JdAnalyzer.Analyze(Jd);
        Assert.Equal(TermWeight.Required, a.Terms[0].Weight);
        Assert.Equal(TermWeight.Preferred, a.Terms[^1].Weight);
    }

    [Fact]
    public void AMarkerOnTheLineBeatsTheSection()
    {
        var a = JdAnalyzer.Analyze("## What you will do\n- Build APIs in Go\n- Terraform is a plus\n- Must have Docker\n");
        Assert.Equal(TermWeight.Preferred, Term(a, "Terraform").Weight);
        Assert.Equal(TermWeight.Required, Term(a, "Docker").Weight);
        Assert.Equal(TermWeight.Duty, Term(a, "Go").Weight);
    }

    [Fact]
    public void TheStrongestMentionWins()
    {
        var a = JdAnalyzer.Analyze("## Nice to have\n- Kafka\n\n## Requirements\n- Kafka experience\n");
        Assert.Equal(TermWeight.Required, Term(a, "Kafka").Weight);
        Assert.Equal(2, Term(a, "Kafka").Count);
    }

    [Fact]
    public void KeepsTheLineAsEvidence() =>
        Assert.Equal("Move batch jobs onto Kafka", Term(JdAnalyzer.Analyze(Jd), "Kafka").Evidence);

    [Fact]
    public void ReadsAPlainPastedPageWithNoMarkdown()
    {
        var a = JdAnalyzer.Analyze("Senior Platform Engineer\n\nRequirements:\n6+ years with Kubernetes and Terraform\n\nNice to have:\nPython\n");
        Assert.Equal(TermWeight.Required, Term(a, "Kubernetes").Weight);
        Assert.Equal(TermWeight.Preferred, Term(a, "Python").Weight);
    }

    [Fact]
    public void ANoiseOnlyDescriptionGivesNoSkills() =>
        Assert.Empty(JdAnalyzer.Analyze("Great team, great perks, come grow with us.").Terms);

    [Fact]
    public void SameTextSameAnswer() =>
        Assert.Equal(JdAnalyzer.Analyze(Jd).Terms.Select(t => t.Skill), JdAnalyzer.Analyze(Jd).Terms.Select(t => t.Skill));
}

public class ResumeMatcherTests
{
    private const string Jd = "## Requirements\n- C#, SQL Server, Kafka and Docker\n\n## Nice to have\n- Terraform\n";
    private const string Resume = "## Experience\n- Built C# services on SQL Server for claims processing\n- Ran Docker builds in CI\n";

    private static MatchReport Report(TailorState? state = null) =>
        ResumeMatcher.Match(JdAnalyzer.Analyze(Jd), Resume, state);

    [Fact]
    public void SeparatesWhatTheResumeSaysFromWhatItDoesNot()
    {
        var r = Report();
        Assert.Equal(MatchStatus.InResume, r.Matches.Single(m => m.Term.Skill == "C#").Status);
        Assert.Equal(MatchStatus.InResume, r.Matches.Single(m => m.Term.Skill == "Docker").Status);
        Assert.Equal(MatchStatus.Missing, r.Matches.Single(m => m.Term.Skill == "Kafka").Status);
        Assert.Equal(MatchStatus.Missing, r.Matches.Single(m => m.Term.Skill == "Terraform").Status);
    }

    [Fact]
    public void CountsCoverageFromTheTexts()
    {
        var r = Report();
        Assert.Equal(4, r.CoreTotal); // C#, SQL Server, Kafka, Docker (not SQL as well: the job description asked for SQL Server)
        Assert.Equal(3, r.CoreInResume); // C#, SQL Server, Docker
        Assert.Equal(75, r.CoreCoveragePercent);
        Assert.Equal(1, r.PreferredTotal);
        Assert.Equal(0, r.PreferredInResume);
    }

    [Fact]
    public void ShowsWhereTheResumeCoversASkill()
    {
        var line = Report().Matches.Single(m => m.Term.Skill == "SQL Server").ResumeLines.Single();
        Assert.Equal("Built C# services on SQL Server for claims processing", line);
    }

    [Fact]
    public void AClaimedSkillIsKeptApartAndDoesNotRaiseCoverage()
    {
        var state = new TailorState();
        state.Attest("Kafka", "Ran the consumers for nightly claim batches");
        var r = Report(state);

        var kafka = r.Matches.Single(m => m.Term.Skill == "Kafka");
        Assert.Equal(MatchStatus.Attested, kafka.Status);
        Assert.Equal("Ran the consumers for nightly claim batches", kafka.Note);
        Assert.Equal(75, r.CoreCoveragePercent);
        Assert.Equal(1, r.CoreAttested);
    }

    [Fact]
    public void ADismissedSkillStopsCounting()
    {
        var state = new TailorState();
        state.Dismiss("Kafka");
        var r = Report(state);
        Assert.DoesNotContain(r.Matches, m => m.Term.Skill == "Kafka");
        Assert.Equal(100, r.CoreCoveragePercent);
    }

    [Fact]
    public void AttestingAgainReplacesTheNoteAndRestoresADismissedSkill()
    {
        var state = new TailorState();
        state.Dismiss("Kafka");
        state.Attest("Kafka", "first");
        state.Attest("kafka", "second");
        Assert.Single(state.Attested);
        Assert.Equal("second", state.FindAttested("Kafka")!.Note);
        Assert.False(state.IsDismissed("Kafka"));
    }

    [Fact]
    public void AResumeThatSaysEverythingIsFullyCovered()
    {
        var r = ResumeMatcher.Match(JdAnalyzer.Analyze(Jd), Resume + "- Used Kafka and Terraform\n");
        Assert.Equal(100, r.CoreCoveragePercent);
    }

    [Fact]
    public void StateIsSavedNextToTheApplication()
    {
        var folder = Path.Combine(Path.GetTempPath(), "jt-tailor-" + Guid.NewGuid().ToString("N"));
        try
        {
            var state = new TailorState();
            state.Attest("Kafka", "Ran the consumers");
            state.Dismiss("Figma");
            state.Save(folder);

            var back = TailorState.Load(folder);
            Assert.Equal("Ran the consumers", back.FindAttested("Kafka")!.Note);
            Assert.True(back.IsDismissed("Figma"));
            Assert.Empty(TailorState.Load(Path.Combine(folder, "nothing-here")).Attested);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        }
    }
}

public class NestedSkillTests
{
    [Fact]
    public void AJobDescriptionAskingForSqlServerDoesNotAlsoAskForSql()
    {
        var a = JdAnalyzer.Analyze("## Requirements\n- SQL Server\n");
        Assert.Equal(["SQL Server"], a.Terms.Select(t => t.Skill).ToArray());
    }

    [Fact]
    public void AResumeSayingSqlServerStillCoversAJobDescriptionThatAsksForSql()
    {
        var r = ResumeMatcher.Match(JdAnalyzer.Analyze("## Requirements\n- Strong SQL\n"), "- Tuned SQL Server indexes\n");
        Assert.Equal(MatchStatus.InResume, r.Matches.Single(m => m.Term.Skill == "SQL").Status);
    }

    [Fact]
    public void AResumeSayingOnlySqlIsOnlyAPartialMatchForSqlServer()
    {
        var r = ResumeMatcher.Match(JdAnalyzer.Analyze("## Requirements\n- SQL Server\n"), "- Wrote SQL reports\n");
        var match = r.Matches.Single(m => m.Term.Skill == "SQL Server");

        Assert.Equal(MatchStatus.Partial, match.Status); // generic SQL is related to SQL Server, but it is not SQL Server
        Assert.Equal(["Wrote SQL reports"], match.EvidenceLines.Select(e => e.Text).ToArray());
        Assert.Equal(0, r.CoreInResume); // and the strict count still does not claim it
    }
}
