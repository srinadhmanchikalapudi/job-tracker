using JobTracker.Core;

namespace JobTracker.Core.Tests;

public class JdSimilarityTests
{
    private const string BackendJd = """
        # Senior Backend Engineer

        About Northwind Labs
        Northwind Labs builds insurance software used by regional health plans across the country. We are a remote friendly team of about two hundred people.

        What you will do
        - Design and operate .NET APIs on SQL Server for claims intake
        - Move nightly batch jobs onto Kafka consumers
        - Review code, mentor engineers and improve our deployment pipeline
        - Work with product managers to plan quarterly releases

        Requirements
        - 6+ years building services in C#
        - Comfortable with Docker and Kubernetes
        - Clear written communication

        Benefits
        Health cover, a learning budget, and flexible hours. Northwind Labs is an equal opportunity employer and welcomes applicants of every background.
        """;

    // The same posting listed again: one bullet reworded, the rest untouched, plus a sentence about the office.
    private const string BackendRepost = """
        # Senior Backend Engineer

        About Northwind Labs
        Northwind Labs builds insurance software used by regional health plans across the country. We are a remote friendly team of about two hundred people.

        What you will do
        - Design and operate .NET APIs on SQL Server for claims intake
        - Move our nightly batch jobs onto Kafka consumers
        - Review code, mentor engineers and improve our deployment pipeline
        - Work with product managers to plan quarterly releases

        Requirements
        - 6+ years building services in C#
        - Comfortable with Docker and Kubernetes
        - Clear written communication

        Benefits
        Health cover, a learning budget, and flexible hours. Northwind Labs is an equal opportunity employer and welcomes applicants of every background. Our office is in Denver.
        """;

    // A different role at the same company: the same company blurb and benefits, but different duties and requirements.
    private const string DataJd = """
        # Data Analyst

        About Northwind Labs
        Northwind Labs builds insurance software used by regional health plans across the country. We are a remote friendly team of about two hundred people.

        What you will do
        - Build dashboards that show claim volumes and processing times
        - Write SQL to answer questions from the operations and finance teams
        - Clean and validate monthly data feeds from our partner plans
        - Present findings to leadership each quarter and recommend changes

        Requirements
        - 3+ years of analytics experience
        - Strong skills in Tableau or Power BI
        - Comfortable explaining numbers to people who are not analysts
        - A degree in statistics, economics or a related field

        Benefits
        Health cover, a learning budget, and flexible hours. Northwind Labs is an equal opportunity employer and welcomes applicants of every background.
        """;

    [Fact]
    public void AnIdenticalDescriptionScoresOne() => Assert.Equal(1.0, JdSimilarity.Score(BackendJd, BackendJd));

    [Fact]
    public void CapitalisationPunctuationAndSpacingDoNotCount() =>
        Assert.Equal(1.0, JdSimilarity.Score(BackendJd, BackendJd.ToUpperInvariant().Replace("\n", "\n\n").Replace("-", "*")));

    [Fact]
    public void ARepostWithSmallEditsIsWellAboveTheThreshold()
    {
        var score = JdSimilarity.Score(BackendJd, BackendRepost);
        Assert.InRange(score, 0.85, 1.0);
    }

    [Fact]
    public void ADifferentRoleAtTheSameCompanyIsBelowTheThreshold()
    {
        var score = JdSimilarity.Score(BackendJd, DataJd);
        Assert.True(score < JdSimilarity.Threshold, $"score was {score:0.00}");
        Assert.True(score > 0.2, $"score was {score:0.00} (the shared company text should still show)");
    }

    [Fact]
    public void ItDoesNotMatterWhichOrderTheyAreGiven() =>
        Assert.Equal(JdSimilarity.Score(BackendJd, DataJd), JdSimilarity.Score(DataJd, BackendJd), 10);

    [Theory]
    [InlineData("", "")]
    [InlineData("Senior Engineer needed", "Senior Engineer needed")]
    [InlineData("short", "A long description with plenty of words in it, enough to be judged as a job description by anybody at all, really")]
    public void TooLittleTextIsNeverCalledADuplicate(string a, string b) => Assert.Equal(0, JdSimilarity.Score(a, b));

    [Theory]
    [InlineData("Acme", "ACME", true)]
    [InlineData("Acme Corp.", "Acme corporation", true)]
    [InlineData("Acme, Inc", "Acme", true)]
    [InlineData("Acme Labs", "Acme", false)]
    [InlineData("Acme", "Apex", false)]
    [InlineData("", "", false)]
    [InlineData("Inc", "Inc", true)]
    public void CompaniesAreComparedByTheirNamesWithoutLegalSuffixes(string a, string b, bool same) =>
        Assert.Equal(same, JdSimilarity.CompanyKey(a).Length > 0 && JdSimilarity.CompanyKey(a) == JdSimilarity.CompanyKey(b));

    private static JobApplication App(string company, string role, string folder) => new()
    {
        FolderPath = folder,
        Meta = new ApplicationMeta { Company = company, Role = role, AppliedDate = new DateOnly(2026, 10, 1) },
    };

    [Fact]
    public void OnlyTheSameCompanysCloseDescriptionsAreReturned_ClosestFirst()
    {
        var jds = new Dictionary<string, string>
        {
            ["a"] = BackendRepost, ["b"] = DataJd, ["c"] = BackendJd, ["d"] = BackendJd,
        };
        var saved = new[]
        {
            App("Northwind Labs", "Senior Backend Engineer", "a"),
            App("Northwind Labs", "Data Analyst", "b"),
            App("NORTHWIND LABS, Inc.", "Senior Backend Engineer (again)", "c"),
            App("Contoso", "Senior Backend Engineer", "d"), // same text, other employer
        };

        var found = JdSimilarity.FindSimilar(saved, a => jds[a.FolderPath], "Northwind Labs", BackendJd);

        Assert.Equal(["c", "a"], found.Select(f => f.Application.FolderPath).ToArray());
        Assert.Equal(100, found[0].Percent);
        Assert.InRange(found[1].Percent, 85, 99);
    }

    [Fact]
    public void NothingIsReturnedWhenThereAreNoPriorApplicationsOrNoCompany()
    {
        Assert.Empty(JdSimilarity.FindSimilar([], _ => "", "Northwind Labs", BackendJd));
        Assert.Empty(JdSimilarity.FindSimilar([App("Northwind Labs", "x", "a")], _ => BackendJd, "  ", BackendJd));
    }

    [Fact]
    public void LinesTheOtherSideLacksAreMarked_AndTheRestAreNot()
    {
        var saved = JdSimilarity.MarkLines(BackendJd, BackendRepost);
        var pasted = JdSimilarity.MarkLines(BackendRepost, BackendJd);

        Assert.Equal(["- Move nightly batch jobs onto Kafka consumers", "Health cover, a learning budget, and flexible hours. Northwind Labs is an equal opportunity employer and welcomes applicants of every background."],
            saved.Where(l => l.OnlyHere).Select(l => l.Text.Trim()).ToArray());
        Assert.Equal(2, pasted.Count(l => l.OnlyHere));
        Assert.Contains(pasted, l => l.OnlyHere && l.Text.Contains("Our office is in Denver"));
        Assert.Equal(BackendJd.Replace("\r\n", "\n").Split('\n').Length, saved.Count);
    }

    [Fact]
    public void IdenticalTextHasNoMarkedLines() => Assert.DoesNotContain(JdSimilarity.MarkLines(BackendJd, BackendJd), l => l.OnlyHere);
}