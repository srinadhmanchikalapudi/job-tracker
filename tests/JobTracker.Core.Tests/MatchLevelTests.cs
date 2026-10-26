using JobTracker.Core.Tailoring;

namespace JobTracker.Core.Tests;

public class MatchLevelTests
{
    private static TermMatch Match(string jd, string resume, string skill, TailorState? state = null) =>
        ResumeMatcher.Match(JdAnalyzer.Analyze("## Requirements\n" + jd), resume, state).Matches.Single(m => m.Term.Skill == skill);

    // The four examples from the product brief.
    [Fact]
    public void ServerlessOnAzure_IsAPartialMatchForAzureFunctions()
    {
        var m = Match("- Azure Functions\n", "## Experience\nContoso, Engineer\n- Built serverless applications on Azure for claim intake\n", "Azure Functions");

        Assert.Equal(MatchStatus.Partial, m.Status);
        Assert.Equal("Serverless + Azure", m.EvidenceLines.Single().Why);
        Assert.True(m.UsedInExperience);
    }

    [Fact]
    public void CiCdWorkflows_AreAPartialMatchForGitHubActions() =>
        Assert.Equal(MatchStatus.Partial, Match("- GitHub Actions\n", "- Created CI/CD workflows for the claims platform\n", "GitHub Actions").Status);

    [Fact]
    public void SqlPerformanceTuning_IsAPartialMatchForQueryOptimization() =>
        Assert.Equal(MatchStatus.Partial, Match("- Query optimization\n", "- Led SQL performance tuning for reporting\n", "Query optimization").Status);

    [Fact]
    public void DotNetCoreWebApi_IsAStrongMatchForAspNetCore()
    {
        var m = Match("- ASP.NET Core\n", "- Built .NET Core Web API services for claim intake\n", "ASP.NET Core");

        Assert.Equal(MatchStatus.InResume, m.Status);
        Assert.Null(m.ImpliedBy); // the resume names it (in a spelling the catalogue knows)
    }

    [Fact]
    public void WordingThatMeansTheRequirement_IsStrongAndSaysWhatImpliedIt()
    {
        var m = Match("- Infrastructure as code\n", "- Wrote Terraform modules for the platform\n", "Infrastructure as code");

        Assert.Equal(MatchStatus.InResume, m.Status);
        Assert.Equal("Terraform", m.ImpliedBy);
        Assert.Equal(["Wrote Terraform modules for the platform"], m.ResumeLines.ToArray());
    }

    [Fact]
    public void ANamedSkillBeatsAnImpliedOne_AndShowsTheLinesItIsIn()
    {
        var m = Match("- Docker\n", "- Ran Kubernetes clusters\n- Built Docker images for the API\n", "Docker");

        Assert.Equal(MatchStatus.InResume, m.Status);
        Assert.Null(m.ImpliedBy);
        Assert.Equal(["Built Docker images for the API"], m.EvidenceLines.Select(e => e.Text).ToArray());
    }

    [Fact]
    public void AMissingSkillShowsRelatedWorkAsPotentialEvidence_ButStaysMissing()
    {
        var resume = "## Experience\nContoso, Engineer\n- Built background workers processing claim events\n- Deployed the API to Azure App Service\n- Painted a fence\n";
        var m = Match("- Azure Functions\n", resume, "Azure Functions");

        Assert.Equal(MatchStatus.Missing, m.Status);
        Assert.Empty(m.EvidenceLines);
        Assert.Equal(["Built background workers processing claim events", "Deployed the API to Azure App Service"], m.RelatedLines.Select(r => r.Text).ToArray());
    }

    [Fact]
    public void ASkillsListThatNamesARelatedToolIsNotPotentialEvidence_ButRelatedWorkIs()
    {
        var onlyListed = Match("- Kubernetes\n", "## Technical Skills\nC#, .NET, Azure, Docker\n", "Kubernetes");
        Assert.Equal(MatchStatus.Missing, onlyListed.Status);
        Assert.Empty(onlyListed.RelatedLines);

        var worked = Match("- Kubernetes\n", "## Technical Skills\nC#, Docker\n\n## Experience\nContoso, Engineer\n- Ran Docker builds for the claims API\n", "Kubernetes");
        Assert.Equal(["Ran Docker builds for the claims API"], worked.RelatedLines.Select(r => r.Text).ToArray());
    }

    [Fact]
    public void AMissingSkillWithNothingRelatedHasNoEvidenceAtAll()
    {
        var m = Match("- RabbitMQ\n", "- Wrote reports in Excel\n", "RabbitMQ");

        Assert.Equal(MatchStatus.Missing, m.Status);
        Assert.Empty(m.RelatedLines);
        Assert.Empty(m.EvidenceLines);
    }

    [Fact]
    public void ASkillOnlyInASkillsListIsNotCountedAsUsed_OneInABulletIs()
    {
        var listed = Match("- Kubernetes\n", "## Technical Skills\nC#, .NET, Kubernetes\n", "Kubernetes");
        Assert.Equal(MatchStatus.InResume, listed.Status);
        Assert.False(listed.UsedInExperience);
        Assert.True(listed.EvidenceLines.Single().InSkillsList);

        var used = Match("- Kubernetes\n", "## Technical Skills\nC#, Kubernetes\n\n## Experience\nContoso, Engineer\n- Ran Kubernetes clusters for the claims API\n", "Kubernetes");
        Assert.True(used.UsedInExperience);
        Assert.False(used.EvidenceLines[0].InSkillsList); // the work-history line comes first
        Assert.Equal(2, used.EvidenceLines.Count);
    }

    [Fact]
    public void AClaimedSkillStillShowsRelatedWordingAsEvidence_ButDoesNotMakeTheResumeSayIt()
    {
        var state = new TailorState();
        state.Attest("Azure Functions", "Used them at Contoso for claim events");
        var m = Match("- Azure Functions\n", "- Built serverless applications on Azure\n", "Azure Functions", state);

        Assert.Equal(MatchStatus.Attested, m.Status);
        Assert.Equal("Used them at Contoso for claim events", m.Note);
        Assert.Single(m.EvidenceLines);
    }

    [Fact]
    public void TheStrictCountIgnoresPartialMatches()
    {
        var r = ResumeMatcher.Match(JdAnalyzer.Analyze("## Requirements\n- Azure Functions\n- Docker\n"), "- Built serverless applications on Azure\n- Ran Docker builds\n");

        Assert.Equal(2, r.CoreTotal);
        Assert.Equal(1, r.CoreInResume);
        Assert.Equal(1, r.CorePartial);
        Assert.Equal(50, r.CoreCoveragePercent);
    }
}