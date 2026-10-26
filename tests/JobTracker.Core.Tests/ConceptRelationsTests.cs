using JobTracker.Core.Tailoring;

namespace JobTracker.Core.Tests;

public class ConceptRelationsTests
{
    private static readonly ConceptRelations Rules = ConceptRelations.Default;

    private static IReadOnlyList<RelationHit> Find(string target, RelationKind kind, params string[] lines) => Rules.Find(target, lines, kind);

    [Fact]
    public void EveryTargetAndEverySkillNamedInARuleIsInTheCatalogue()
    {
        var known = SkillLexicon.Default.Skills.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownTargets = Rules.Targets.Where(t => !known.Contains(t)).ToList();
        var unknownTokens = Rules.SkillTokens.Where(t => !known.Contains(t)).ToList();
        Assert.True(unknownTargets.Count == 0, "Targets not in skills.txt: " + string.Join(", ", unknownTargets));
        Assert.True(unknownTokens.Count == 0, "Skills named in a rule but not in skills.txt: " + string.Join(", ", unknownTokens));
    }

    [Fact]
    public void AMalformedRuleIsRefusedLoudly() =>
        Assert.Throws<FormatException>(() => new ConceptRelations(["Azure Functions : serverless"], SkillLexicon.Default));

    // The examples from the product brief: wording that is related to a requirement but does not name it.
    [Theory]
    [InlineData("Azure Functions", "Built serverless applications on Azure for claim intake")]
    [InlineData("GitHub Actions", "Created CI/CD workflows for the claims platform")]
    [InlineData("Query optimization", "Improved SQL performance tuning for reporting queries")]
    public void RelatedWordingIsAPartialMatchNotAStrongOne(string target, string line)
    {
        var hits = Find(target, RelationKind.Partial, line);

        Assert.Single(hits);
        Assert.Equal(line, hits[0].Line);
        Assert.True(hits[0].Reason.Length > 0); // it can say which words it recognised
    }

    [Fact]
    public void ThePartialReasonNamesTheWordsThatMatched() =>
        Assert.Equal("Serverless + Azure", Find("Azure Functions", RelationKind.Partial, "Built serverless applications on Azure")[0].Reason);

    [Fact]
    public void BothThingsMustBeOnTheSameLine()
    {
        Assert.Empty(Find("Azure Functions", RelationKind.Partial, "Built serverless applications", "Deployed services to Azure"));
        Assert.Single(Find("Azure Functions", RelationKind.Partial, "Built serverless applications", "Deployed serverless services to Azure"));
    }

    [Fact]
    public void ImpliedWordingIsStrong()
    {
        Assert.Single(Find("Infrastructure as code", RelationKind.Strong, "Wrote Terraform modules for the platform"));
        Assert.Single(Find("Serverless", RelationKind.Strong, "Built Azure Functions for event handling"));
        Assert.Single(Find("Unit testing", RelationKind.Strong, "Wrote xUnit tests for the claims API"));
    }

    [Fact]
    public void AdjacentWorkIsOnlyRelated_NeverPartialOrStrong()
    {
        var line = "Built background workers processing claim events";
        Assert.Single(Find("Azure Functions", RelationKind.Related, line));
        Assert.Empty(Find("Azure Functions", RelationKind.Partial, line));
        Assert.Empty(Find("Azure Functions", RelationKind.Strong, line));
    }

    [Fact]
    public void ATrailingStarLetsAWordContinue_ButAWholeWordIsNotPartOfAnotherWord()
    {
        Assert.Single(Find("Claims", RelationKind.Partial, "Processed claim adjustments"));
        Assert.Empty(Find("Microservices", RelationKind.Partial, "Cleaned the SOAP endpoints")); // "SOA" is not the start of "SOAP"
        Assert.Single(Find("Microservices", RelationKind.Partial, "Moved a SOA monolith toward services"));
    }

    [Fact]
    public void NothingHappensForATargetWithoutRules() =>
        Assert.Empty(Find("Figma", RelationKind.Partial, "Designed screens in Figma"));
}