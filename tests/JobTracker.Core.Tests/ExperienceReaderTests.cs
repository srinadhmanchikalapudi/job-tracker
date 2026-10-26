using JobTracker.Core.Tailoring;

namespace JobTracker.Core.Tests;

public class ExperienceReaderTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    private static ResumeYears? Years(string resume) => ExperienceReader.YearsOf(ResumeLineReader.Read(resume), Today);

    [Fact]
    public void YearsAreCountedFromTheDatesOfTheJobs()
    {
        var years = Years("Alex Rivera\n\n## Experience\nContoso, Senior Engineer, Jan 2020 - Dec 2022\n- Built things\n\nFabrikam, Engineer, 2023 to present\n- Built more\n");

        Assert.True(years!.FromDates);
        Assert.InRange(years.Years, 6.7, 6.9); // Jan 2020 to Oct 2026, with Jan 2023 to Oct 2026 inside it: 6 years 10 months
    }

    [Fact]
    public void AnOpenEndedJobRunsUpToToday() =>
        Assert.InRange(Years("## Experience\nContoso, Engineer, Aug 2024 - Present\n- Built things\n")!.Years, 2.1, 2.2);

    [Fact]
    public void TwoJobsAtTheSameTimeAreNotCountedTwice() =>
        Assert.InRange(Years("## Experience\nContoso, Engineer, 2020 - 2022\n- Built\n\nFabrikam, Consultant, 2021 - 2022\n- Built\n")!.Years, 2.9, 3.1);

    [Fact]
    public void EducationDatesAreNotWork() =>
        Assert.InRange(Years("## Education\nState University, B.S. Computer Science, 2012 - 2016\n\n## Experience\nContoso, Engineer, 2020 - 2022\n- Built\n")!.Years, 2.9, 3.1);

    [Fact]
    public void NumericMonthRangesAreRead() =>
        Assert.InRange(Years("## Experience\nContoso, Engineer, 03/2019 - 02/2021\n- Built\n")!.Years, 1.9, 2.1);

    [Fact]
    public void WithNoDatesASentenceIsUsed_AndSaysSo()
    {
        var years = Years("Alex Rivera\nSenior engineer with 8+ years of experience building APIs.\n\n## Experience\nContoso, Engineer\n- Built things\n");

        Assert.False(years!.FromDates);
        Assert.Equal(8, years.Years);
    }

    [Fact]
    public void NothingAtAllIsNull() => Assert.Null(Years("## Experience\n- Built things\n"));

    [Fact]
    public void TheJobDescriptionSaysHowManyYearsItAsksFor()
    {
        Assert.Equal(8, JdAnalyzer.Analyze("## Requirements\n- 8+ years of experience with C#\n- 3 years of Azure\n").RequiredYears);
        Assert.Equal(3, JdAnalyzer.Analyze("## Requirements\n- 3-5 years building services\n").RequiredYears); // a range asks for its minimum
        Assert.Null(JdAnalyzer.Analyze("## Requirements\n- C# and SQL\n").RequiredYears);
        Assert.Null(JdAnalyzer.Analyze("## About us\nWe have been building software for 20 years.\n## Requirements\n- C#\n").RequiredYears); // company history is not a requirement
        Assert.Null(JdAnalyzer.Analyze("## Requirements\n- C#\n- Founded 15 years ago\n").RequiredYears);
    }

    // ---- title

    [Theory]
    [InlineData(".NET Developer with AI", "Senior Full Stack .NET Developer", 0.7, 0.8)]   // .NET and developer found, AI not
    [InlineData(".NET Developer", "Senior .NET Engineer", 0.99, 1.0)]                      // developer and engineer are the same job
    [InlineData("Senior Software Engineer", "Junior Software Engineer", 0.9, 0.95)]        // same words, lower seniority: only slightly lower
    [InlineData("Data Engineer", "Pastry Chef", 0.0, 0.2)]
    public void TheTitleOnTheResumeIsComparedToTheJobsTitle(string job, string resumeTitle, double min, double max) =>
        Assert.InRange(ExperienceReader.TitleSimilarity(job, [resumeTitle]).Score, min, max);

    [Fact]
    public void TheClosestOfSeveralTitlesWins()
    {
        var match = ExperienceReader.TitleSimilarity("Azure Engineer", ["Software Developer", "Azure Cloud Engineer", "Support Analyst"]);
        Assert.Equal("Azure Cloud Engineer", match.BestResumeTitle);
        Assert.True(match.Score > 0.8);
    }

    [Fact]
    public void TitlesAreTakenFromTheHeadlineAndTheJobHeadings()
    {
        var titles = ExperienceReader.TitlesOf("Alex Rivera\nSenior .NET Developer\nalex@example.com\n\n## Experience\nContoso Insurance, Software Engineer, 2020 - 2022\n- Built things\n\nFabrikam, Lead Developer | Remote, 2022 to present\n- Led a team of 4\n");

        Assert.Contains("Senior .NET Developer", titles);
        Assert.Contains("Software Engineer", titles);
        Assert.Contains("Lead Developer", titles);
        Assert.DoesNotContain(titles, t => t.Contains('@'));
    }

    // ---- leadership

    [Fact]
    public void LeadershipIsOnlyWhatTheResumeShowsInTheWork()
    {
        var lines = ResumeLineReader.Read("## Technical Skills\nLed teams, mentoring, coaching\n\n## Experience\nContoso, Engineer\n- Led a team of 4 engineers on the claims API\n- Mentored junior developers\n- Managed Azure SQL databases\n- Built reports\n");
        var shown = ExperienceReader.LeadershipShownIn(lines);

        Assert.Equal(["Led a team of 4 engineers on the claims API", "Mentored junior developers"], shown.ToArray());
    }

    [Fact]
    public void TheJobDescriptionsLeadershipAskIsFound()
    {
        var jd = JdAnalyzer.Analyze("## Responsibilities\n- Mentor junior engineers and lead design reviews\n- Build APIs\n");
        Assert.Single(jd.Leadership);
        Assert.Empty(JdAnalyzer.Analyze("## Responsibilities\n- Build APIs\n- Manage Azure resources\n").Leadership);
    }
}