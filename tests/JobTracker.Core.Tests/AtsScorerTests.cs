using JobTracker.Core.Tailoring;

namespace JobTracker.Core.Tests;

public class AtsScorerTests
{
    private static readonly DateOnly Today = new(2026, 10, 25);

    private const string Jd = """
        # .NET Developer with AI

        ## About us
        We are a healthcare software company that has been building software for 20 years.

        ## Requirements
        - 8+ years of experience building services in C# and ASP.NET Core
        - Strong SQL Server and Azure experience
        - Azure Functions for event-driven processing
        - Docker and Kubernetes
        - Mentor junior engineers

        ## Nice to have
        - Terraform
        - RabbitMQ
        """;

    private const string Resume = """
        Alex Rivera
        Senior .NET Developer

        ## Experience
        Contoso Insurance, Senior .NET Developer, Jan 2016 - Present
        - Built ASP.NET Core and C# services for claim intake
        - Tuned SQL Server queries and indexes
        - Ran Docker and Kubernetes clusters on Azure
        - Built Azure Functions for event-driven claim processing
        - Mentored junior engineers and led design reviews
        - Wrote Terraform modules and RabbitMQ consumers

        ## Skills
        C#, .NET, SQL Server, Azure, Docker
        """;

    private static ScoreReport Score(string resume, string title = ".NET Developer with AI", string jd = Jd, TailorState? state = null) =>
        AtsScorer.Score(JdAnalyzer.Analyze(jd), resume, title, state, today: Today);

    // ---- weighting

    [Fact]
    public void MissingAnImportantRequirementCostsFarMoreThanMissingAnOptionalOne()
    {
        var full = Score(Resume).Overall;
        var withoutAspNet = Score(Resume.Replace("Built ASP.NET Core and C# services", "Built C# services")).Overall;
        var withoutRabbit = Score(Resume.Replace(" and RabbitMQ consumers", "")).Overall;

        Assert.True(full - withoutAspNet >= 3 * (full - withoutRabbit), $"ASP.NET Core cost {full - withoutAspNet}, RabbitMQ cost {full - withoutRabbit}");
        Assert.True(full - withoutRabbit >= 0);
    }

    [Fact]
    public void ForADotNetRoleAzureFunctionsOutweighsTerraform_ForACloudRoleTheReverse()
    {
        var jd = "## Requirements\n- Azure Functions\n- Terraform\n- Angular\n";
        var dotnet = Score("- Built things\n", ".NET Developer", jd);
        var cloud = Score("- Built things\n", "Cloud Engineer", jd);

        Assert.True(dotnet.Find("Azure Functions")!.Weight > dotnet.Find("Terraform")!.Weight);
        Assert.True(cloud.Find("Terraform")!.Weight > cloud.Find("Azure Functions")!.Weight);
        Assert.True(cloud.Find("Terraform")!.Weight > cloud.Find("Angular")!.Weight);
        Assert.Equal("dotnet", dotnet.Role.Profile.Id);
        Assert.Equal("cloud", cloud.Role.Profile.Id);
    }

    [Fact]
    public void ARequirementTheJobMerelyPrefersWeighsLessThanOneItRequires()
    {
        var report = Score("- Built things\n", ".NET Developer", "## Requirements\n- Docker\n\n## Nice to have\n- Kubernetes\n");
        Assert.True(report.Find("Docker")!.Weight > report.Find("Kubernetes")!.Weight);
    }

    [Theory]
    [InlineData(".NET Developer with AI", "dotnet")]   // a .NET job that mentions AI, not an AI job
    [InlineData("Senior Full Stack Developer", "fullstack")]
    [InlineData("Azure Cloud Engineer", "azure")]
    [InlineData("DevOps Engineer", "cloud")]
    [InlineData("AI Engineer", "ai")]
    [InlineData("Machine Learning Engineer", "ai")]
    [InlineData("Data Engineer", "data")]
    [InlineData("Software Engineer", "software")]
    public void TheKindOfJobIsRecognisedFromTheTitle(string title, string expected) =>
        Assert.Equal(expected, RoleProfiles.Detect(title, new JdAnalysis([])).Profile.Id);

    [Fact]
    public void AJobDescriptionThatLeansHardOnOneStackIsRecognisedWhenTheTitleSaysNothing()
    {
        var jd = JdAnalyzer.Analyze("## Requirements\n- C#\n- ASP.NET Core\n- Entity Framework\n- .NET\n");
        Assert.Equal("dotnet", RoleProfiles.Detect("Software Engineer", jd).Profile.Id);
    }

    [Fact]
    public void ThePersonCanChooseTheKindOfJob()
    {
        var state = new TailorState { RoleId = "cloud" };
        var report = Score("- Built things\n", ".NET Developer", "## Requirements\n- Terraform\n- Azure Functions\n", state);

        Assert.Equal("cloud", report.Role.Profile.Id);
        Assert.False(report.Role.Detected);
    }

    [Fact]
    public void EveryNameInARoleProfileIsInTheCatalogue()
    {
        var known = SkillLexicon.Default.Skills.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = RoleProfiles.All.SelectMany(p => p.NamedSkills.Select(s => (p.Id, s))).Where(x => !known.Contains(x.s)).ToList();
        Assert.True(unknown.Count == 0, string.Join(", ", unknown));
    }

    // ---- credit

    [Fact]
    public void AClosePhraseEarnsLessThanTheNamedSkillAndMoreThanNothing()
    {
        var jd = "## Requirements\n- Azure Functions\n";
        var named = Score("## Experience\nContoso, Engineer\n- Built Azure Functions\n", ".NET Developer", jd);
        var partial = Score("## Experience\nContoso, Engineer\n- Built serverless applications on Azure\n", ".NET Developer", jd);
        var missing = Score("## Experience\nContoso, Engineer\n- Painted fences\n", ".NET Developer", jd);

        Assert.Equal(MatchLevel.Strong, named.Find("Azure Functions")!.Level);
        Assert.Equal(MatchLevel.Partial, partial.Find("Azure Functions")!.Level);
        Assert.Equal(1.0, named.Find("Azure Functions")!.Credit);
        Assert.Equal(0.5, partial.Find("Azure Functions")!.Credit);
        Assert.Equal(0.0, missing.Find("Azure Functions")!.Credit);
        Assert.True(named.Components.Single(c => c.Part == ScorePart.Cloud).Score > partial.Components.Single(c => c.Part == ScorePart.Cloud).Score);
        Assert.True(partial.Components.Single(c => c.Part == ScorePart.Cloud).Score > missing.Components.Single(c => c.Part == ScorePart.Cloud).Score);
    }

    [Fact]
    public void ASkillOnlyListedEarnsLessThanOneThatWasUsed()
    {
        var jd = "## Requirements\n- Kubernetes\n";
        var used = Score("## Experience\nContoso, Engineer\n- Ran Kubernetes clusters\n", "Cloud Engineer", jd);
        var listed = Score("## Skills\nKubernetes\n", "Cloud Engineer", jd);

        Assert.Equal(1.0, used.Find("Kubernetes")!.Credit);
        Assert.Equal(0.8, listed.Find("Kubernetes")!.Credit);
    }

    [Fact]
    public void MentioningASkillManyTimesDoesNotEarnMore()
    {
        var once = Score("## Experience\nContoso, Engineer\n- Ran Kubernetes clusters\n", "Cloud Engineer", "## Requirements\n- Kubernetes\n");
        var often = Score("## Experience\nContoso, Engineer\n- Ran Kubernetes clusters\n- Kubernetes upgrades\n- Kubernetes autoscaling\n- Kubernetes networking\n", "Cloud Engineer", "## Requirements\n- Kubernetes\n");

        Assert.Equal(once.Find("Kubernetes")!.Credit, often.Find("Kubernetes")!.Credit);
    }

    [Fact]
    public void ASkillTheyClaimedButNeverWroteDownEarnsNothingFromAnAts()
    {
        var state = new TailorState();
        state.Attest("Terraform", "Used it at Contoso");
        var report = Score("## Experience\nContoso, Engineer\n- Built things\n", "Cloud Engineer", "## Requirements\n- Terraform\n", state);

        Assert.Equal(MatchLevel.Claimed, report.Find("Terraform")!.Level);
        Assert.Equal(0, report.Find("Terraform")!.Credit);
        Assert.Equal(RewriteSource.UserConfirmed, report.Find("Terraform")!.Source);
    }

    // ---- experience

    [Fact]
    public void YearsOfExperienceAreComparedWithWhatTheJobAsks()
    {
        var enough = Score(Resume); // Jan 2016 to today: well over 8 years
        var few = Score(Resume.Replace("Jan 2016", "Jan 2023"));

        Assert.Equal(1.0, enough.Components.Single(c => c.Part == ScorePart.Years).Score);
        Assert.InRange(few.Components.Single(c => c.Part == ScorePart.Years).Score, 0.3, 0.5); // about 3.8 of 8
        Assert.Equal(8, few.YearsAsked);
        Assert.True(few.Overall < enough.Overall);
    }

    [Fact]
    public void TheTitleOnTheResumeCountsToo()
    {
        var fits = Score(Resume);
        var doesNotFit = Score(Resume.Replace("Senior .NET Developer", "Pastry Chef"));
        Assert.True(fits.Components.Single(c => c.Part == ScorePart.Title).Score > doesNotFit.Components.Single(c => c.Part == ScorePart.Title).Score);
    }

    [Fact]
    public void LeadershipShownInTheWorkCounts_AndOnlyWhenTheJobAsks()
    {
        var shown = Score(Resume);
        var notShown = Score(Resume.Replace("Mentored junior engineers and led design reviews", "Reviewed designs"));
        Assert.True(shown.Components.Single(c => c.Part == ScorePart.Leadership).Score > notShown.Components.Single(c => c.Part == ScorePart.Leadership).Score);

        var notAsked = Score(Resume, jd: "## Requirements\n- C#\n- Docker\n");
        Assert.DoesNotContain(notAsked.Components, c => c.Part == ScorePart.Leadership); // the job does not ask, so it is not measured
    }

    // ---- the explanation

    [Fact]
    public void ThePartsAreWorthExactly100PointsBetweenThem()
    {
        var report = Score(Resume);
        Assert.InRange(report.Components.Sum(c => c.Share), 99.999, 100.001);
        Assert.InRange(report.Components.Sum(c => c.Earned), report.Overall - 0.5001, report.Overall + 0.5001);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheWhyAddsUpExactly(bool fullResume)
    {
        var report = Score(fullResume ? Resume : "## Experience\nContoso, Engineer, 2021 - 2022\n- Built C# services\n- Ran Docker\n");

        Assert.Equal(report.Overall, report.Breakdown.Where(l => l.Gained).Sum(l => l.Points));
        Assert.Equal(100 - report.Overall, report.Breakdown.Where(l => !l.Gained).Sum(l => l.Points));
        Assert.All(report.Breakdown, l => Assert.True(l.Points > 0));
    }

    [Fact]
    public void TheWhyNamesWhatIsMissing()
    {
        var report = Score("## Experience\nContoso, Engineer, 2021 - 2022\n- Built C# services\n");
        var cloudLoss = report.Breakdown.Single(l => !l.Gained && l.Label == "cloud and DevOps");

        Assert.Contains("Docker", cloudLoss.Detail);
        Assert.Contains("Kubernetes", cloudLoss.Detail);
    }

    [Fact]
    public void EachRequirementsPointsAddUpToItsPartsPoints()
    {
        var report = Score(Resume);
        foreach (var component in report.Components.Where(c => c.Part is ScorePart.Core or ScorePart.Cloud or ScorePart.Architecture or ScorePart.Domain))
        {
            var reqs = report.Requirements.Where(r => PartOf(r) == component.Part).ToList();
            Assert.InRange(reqs.Sum(r => r.Points), component.Earned - 0.001, component.Earned + 0.001);
            Assert.InRange(reqs.Sum(r => r.MaxPoints), component.Share - 0.001, component.Share + 0.001);
        }
    }

    [Fact]
    public void EveryPointIsAccountedFor_RequirementsPlusYearsTitleAndLeadershipShown()
    {
        var report = Score(Resume);
        var fromRequirements = report.Requirements.Sum(r => r.Points);
        var others = report.Components.Where(c => c.Part is ScorePart.Years or ScorePart.Title).Sum(c => c.Earned);
        var leadershipSlice = report.Components.Single(c => c.Part == ScorePart.Leadership).Earned - report.Requirements.Where(r => r.Group == ScoreGroup.Leadership).Sum(r => r.Points);

        Assert.InRange(fromRequirements + others + leadershipSlice, report.Overall - 0.5001, report.Overall + 0.5001);
    }

    [Fact]
    public void ARequirementIsWorthWhatItsWeightIsOfTheWholePool()
    {
        var report = Score(Resume);
        var aspNet = report.Find("ASP.NET Core")!;
        var rabbit = report.Find("RabbitMQ")!;

        Assert.InRange(aspNet.MaxPoints / rabbit.MaxPoints, aspNet.Weight / rabbit.Weight - 0.001, aspNet.Weight / rabbit.Weight + 0.001);
        Assert.True(aspNet.MaxPoints > 10 * rabbit.MaxPoints); // missing the one costs far more than missing the other
    }

    [Fact]
    public void OneLineAboutMentoringDoesNotOutweighAnImportantTechnicalRequirement()
    {
        var report = Score("## Experience\nContoso, Engineer, 2021 - 2022\n- Built things\n", ".NET Developer", "## Requirements\n- Kubernetes\n- Docker\n- Mentor junior engineers\n");
        Assert.True(report.Find("Kubernetes")!.MaxPoints > report.Find("Mentoring")!.MaxPoints);
    }

    private static ScorePart PartOf(Requirement r) => SkillCategories.GroupOf(r.Category) switch
    {
        ScoreGroup.Cloud => ScorePart.Cloud,
        ScoreGroup.Architecture => ScorePart.Architecture,
        ScoreGroup.Domain => ScorePart.Domain,
        ScoreGroup.Leadership => ScorePart.Leadership,
        _ => ScorePart.Core,
    };

    [Fact]
    public void TheBarsAreInAFixedOrderWithExperienceCombined()
    {
        var bars = Score(Resume).Bars.Select(b => b.Label).ToArray();
        Assert.Equal(["Skill Match", "Cloud Match", "Architecture Match", "Experience Match", "Leadership Match"], bars.Where(b => b != "Domain Match").ToArray());
        Assert.Single(bars, b => b == "Experience Match");
    }

    // ---- risk and rewrite source

    [Fact]
    public void RisksAreRankedByHowMuchMissingTheRequirementCouldHurt()
    {
        var report = Score("## Experience\nContoso, Engineer, 2021 - 2022\n- Wrote reports\n", ".NET Developer", "## Requirements\n- ASP.NET Core\n- Docker\n\n## Nice to have\n- RabbitMQ\n");

        Assert.Equal(RiskLevel.High, report.Risks.Single(r => r.Name == "ASP.NET Core").Level);
        Assert.Equal(RiskLevel.Low, report.Risks.Single(r => r.Name == "RabbitMQ").Level);
        Assert.Equal("Missing required ASP.NET Core", report.Risks.Single(r => r.Name == "ASP.NET Core").Text);
        Assert.Equal(["ASP.NET Core", "Docker", "RabbitMQ"], report.Risks.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void ARequirementThatIsMatchedHasNoRiskAndNoSource()
    {
        var found = Score(Resume).Find("C#")!;
        Assert.Null(found.Risk);
        Assert.Null(found.Source);
    }

    [Fact]
    public void TheSourceOfACaseForAddingARequirementIsHonest()
    {
        var resume = "## Experience\nContoso, Engineer, 2021 - 2022\n- Built background workers processing claim events\n- Built serverless applications on Azure\n";
        var state = new TailorState();
        state.Attest("Terraform", "Used at Contoso");
        var report = Score(resume, ".NET Developer", "## Requirements\n- Terraform\n- Azure Functions\n- RabbitMQ\n- Kubernetes\n", state);

        Assert.Equal(RewriteSource.UserConfirmed, report.Find("Terraform")!.Source);          // they said so
        Assert.Equal(RewriteSource.SystemInferred, report.Find("Azure Functions")!.Source);   // related wording is in the resume
        Assert.Equal(RewriteSource.JobDescriptionOnly, report.Find("RabbitMQ")!.Source);      // nothing points to it
        Assert.Equal(RewriteSource.JobDescriptionOnly, report.Find("Kubernetes")!.Source);
    }

    [Fact]
    public void TheTopMissingKeywordsAreTheHeaviestOnesNotInTheResume()
    {
        var report = Score("## Experience\nContoso, Engineer, 2021 - 2022\n- Built C# services\n");
        var top = report.TopMissing.Select(r => r.Name).ToList();

        Assert.DoesNotContain("C#", top);
        Assert.True(report.TopMissing.Zip(report.TopMissing.Skip(1), (a, b) => a.Weight >= b.Weight).All(x => x));
        Assert.Contains("ASP.NET Core", top);
    }

    // ---- honesty and edges

    [Fact]
    public void TheSameTextsAlwaysGiveTheSameScore() =>
        Assert.Equivalent(Score(Resume).Breakdown, Score(Resume).Breakdown);

    [Fact]
    public void AJobDescriptionWithNothingRecognisableHasNothingToScore()
    {
        var report = Score(Resume, jd: "Great team, great perks, come grow with us.");

        Assert.False(report.HasRequirements);
        Assert.Equal(0, report.Overall);
        Assert.Empty(report.Breakdown);
    }

    [Fact]
    public void AnEmptyResumeScoresZeroWithoutFailing()
    {
        var report = Score("");
        Assert.Equal(0, report.Overall);
        Assert.True(report.Risks.Count > 0);
    }

    [Fact]
    public void AddingARequirementToTheResumeNeverLowersTheScore()
    {
        var before = Score("## Experience\nContoso, Engineer, 2021 - 2022\n- Built C# services\n");
        var after = Score("## Experience\nContoso, Engineer, 2021 - 2022\n- Built C# services\n- Ran Docker and Kubernetes on Azure\n");
        Assert.True(after.Overall > before.Overall);
    }

    [Theory]
    [InlineData(3, 62)]
    [InlineData(7, 51)]
    [InlineData(4, 0)]
    public void ApportionedNumbersAddUpToTheTotal(int count, int total)
    {
        var amounts = Enumerable.Range(0, count).Select(i => 3.0 * (i + 1) + 0.37 * i).ToList();
        var scaled = amounts.Select(a => a * total / amounts.Sum()).ToList(); // amounts that add up to the total, but are not whole numbers

        var parts = AtsScorer.Apportion(scaled, total);

        Assert.Equal(total, parts.Sum());
        Assert.All(parts.Zip(scaled), p => Assert.InRange(p.First, Math.Floor(p.Second), Math.Floor(p.Second) + 1));
    }
}