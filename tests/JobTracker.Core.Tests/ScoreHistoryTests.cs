using JobTracker.Core.Tailoring;

namespace JobTracker.Core.Tests;

public class ScoreHistoryTests
{
    private const string Jd = "# .NET Developer\n\n## Requirements\n- C# and ASP.NET Core\n- Docker and Kubernetes\n- Azure Functions\n\n## Nice to have\n- Terraform\n";
    private const string V1 = "Alex Rivera\n\n## Experience\nContoso, .NET Developer, 2020 - 2023\n- Built C# services\n";
    private const string V2 = V1 + "- Ran Docker builds\n";
    private const string V3 = V2 + "- Ran Kubernetes clusters and built Azure Functions\n";

    private static ScoreReport Score(string text) => AtsScorer.Score(JdAnalyzer.Analyze(Jd), text, ".NET Developer", today: new DateOnly(2026, 10, 25));

    private static HistorySource Src(string label, int day, string text, bool current = false) => new(label, new DateTime(2026, 10, day), text, current);

    [Fact]
    public void TheScoreIsListedInTheOrderThingsHappened_AndRisesWithEachBetterVersion()
    {
        var history = ScoreHistory.Build([Src("Version 3", 3, V3, true), Src("Version 2", 2, V2), Src("Version 1", 1, V1)], Score);

        Assert.Equal([ScoreHistory.OriginalLabel, "Version 2", "Version 3"], history.Select(h => h.Label).ToArray());
        Assert.True(history[0].Overall < history[1].Overall && history[1].Overall < history[2].Overall);
        Assert.Null(history[0].Delta);
        Assert.Equal(history[1].Overall - history[0].Overall, history[1].Delta);
        Assert.True(history[2].IsCurrent);
    }

    [Fact]
    public void EachStepSaysWhichChangesRaisedTheScore()
    {
        var history = ScoreHistory.Build([Src("Version 1", 1, V1), Src("Version 2", 2, V2), Src("Version 3", 3, V3)], Score);

        Assert.Contains(history[1].Changes, c => c.Name == "Docker" && c.Text == "Docker: missing → matched" && c.Points > 0);
        Assert.Contains(history[2].Changes, c => c.Name == "Kubernetes" && c.Points > 0);
        Assert.Contains(history[2].Changes, c => c.Name == "Azure Functions" && c.Text.EndsWith("matched"));
        Assert.DoesNotContain(history[1].Changes, c => c.Name == "Kubernetes"); // not yet
        Assert.True(history[2].Changes.Zip(history[2].Changes.Skip(1), (a, b) => Math.Abs(a.Points) >= Math.Abs(b.Points)).All(x => x)); // biggest first
    }

    [Fact]
    public void TheChangesAccountForTheScoreMove()
    {
        var history = ScoreHistory.Build([Src("Version 1", 1, V1), Src("Version 2", 2, V3)], Score);
        Assert.InRange(history[1].Changes.Sum(c => c.Points), history[1].Delta!.Value - 1.0, history[1].Delta!.Value + 1.0);
    }

    [Fact]
    public void ANewerResumeThatIsNotBetterShowsTheDropAndWhy()
    {
        var history = ScoreHistory.Build([Src("Version 1", 1, V3), Src("Version 2", 2, V1)], Score);

        Assert.True(history[1].Delta < 0);
        Assert.Contains(history[1].Changes, c => c.Name == "Kubernetes" && c.Text == "Kubernetes: matched → missing" && c.Points < 0);
        Assert.StartsWith("-", history[1].Summary);
    }

    [Fact]
    public void TheVersionInUseAndASavedVersionWithTheSameTextAreOneEntry()
    {
        var history = ScoreHistory.Build([Src("Version 1", 1, V1), Src("Version 2", 2, V2), Src("Your resume now", 5, V2, current: true)], Score);

        Assert.Equal(2, history.Count);
        Assert.Equal("Version 2", history[1].Label); // keeps the saved version's name and date
        Assert.True(history[1].IsCurrent);
        Assert.Equal(new DateTime(2026, 10, 2), history[1].At);
    }

    [Fact]
    public void AnUnsavedCurrentResumeIsItsOwnEntry()
    {
        var history = ScoreHistory.Build([Src("Version 2", 2, V1), Src("Your resume now", 5, V2, current: true)], Score);
        Assert.Equal(["Original resume", "Your resume now"], history.Select(h => h.Label).ToArray());
    }

    [Fact]
    public void ASingleResumeIsJustItsLabelAndScore()
    {
        var history = ScoreHistory.Build([Src("Your resume now", 1, V1, current: true)], Score);
        Assert.Single(history);
        Assert.Equal("Your resume now", history[0].Label);
        Assert.Equal("first score", history[0].Summary);
    }

    [Fact]
    public void AMoreExperiencedResumeShowsTheYearsChange()
    {
        var shorter = V1.Replace("2020 - 2023", "2022 - 2023");
        var history = ScoreHistory.Build([Src("Version 1", 1, shorter), Src("Version 2", 2, V1)], jd => AtsScorer.Score(JdAnalyzer.Analyze(Jd + "- 5+ years of experience\n"), jd, ".NET Developer", today: new DateOnly(2026, 10, 25)));
        Assert.Contains(history[1].Changes, c => c.Name == "Years of experience" && c.Points > 0);
    }

    [Fact]
    public void RestoringAnOldVersionKeepsTheOldResumesDateForItsBackup()
    {
        using var temp = new TempDir();
        var resume = Path.Combine(temp.Path, "resume.md");
        File.WriteAllText(resume, "original resume text");
        File.SetLastWriteTime(resume, new DateTime(2026, 1, 5, 9, 0, 0));
        var chosen = ResumeVersions.SaveNew(temp.Path, "improved resume text");

        var backup = ResumeVersions.UseAsResume(temp.Path, chosen);

        Assert.NotNull(backup);
        Assert.Equal(new DateTime(2026, 1, 5, 9, 0, 0), File.GetLastWriteTime(backup!.Path)); // the backup is the old resume, so it is old
        Assert.Equal("improved resume text", File.ReadAllText(resume));
    }
}