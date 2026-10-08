namespace JobTracker.Core.Tests;

public class JdExtractorTests
{
    [Fact]
    public void LabeledFields_AreUsed()
    {
        var r = JdExtractor.Extract("Job Title: Senior Backend Engineer\nCompany: Acme Corp\nLocation: Remote\n\nWe build things.");

        Assert.Equal("Acme Corp", r.Company);
        Assert.Equal("Senior Backend Engineer", r.Role);
    }

    [Fact]
    public void FullPagePaste_WithNavigationNoise_FindsTitleAndAboutCompany()
    {
        const string page = """
            Skip to main content
            Home
            Jobs
            Search jobs
            Staff Platform Engineer
            Remote · Full-time
            Apply now

            About Globex Corporation
            Globex builds widgets for everyone.

            Responsibilities
            - Build services
            """;

        var r = JdExtractor.Extract(page);

        Assert.Equal("Staff Platform Engineer", r.Role);
        Assert.Equal("Globex Corporation", r.Company);
    }

    [Fact]
    public void TitleWithAtSuffix_SplitsRoleAndCompany()
    {
        var r = JdExtractor.Extract("Senior Data Engineer at Initech\nBangalore, KA\nWe are looking for...");

        Assert.Equal("Senior Data Engineer", r.Role);
        Assert.Equal("Initech", r.Company);
    }

    [Fact]
    public void LinkedInStyle_CompanyIsLineAfterTitle()
    {
        const string page = "Senior Backend Engineer\nUmbrella Health · Seattle, WA (Hybrid)\n2 weeks ago · 100 applicants\nEasy Apply\nAbout the job\nWe are looking for...";

        var r = JdExtractor.Extract(page, "https://www.linkedin.com/jobs/view/123456/");

        Assert.Equal("Senior Backend Engineer", r.Role);
        Assert.Equal("Umbrella Health", r.Company);
        Assert.Equal("https://www.linkedin.com/jobs/view/123456/", r.Url);
    }

    [Theory]
    [InlineData("https://boards.greenhouse.io/acme-corp/jobs/123", "Acme Corp")]
    [InlineData("https://job-boards.greenhouse.io/initech/jobs/9", "Initech")]
    [InlineData("https://jobs.lever.co/globex/abc-123", "Globex")]
    [InlineData("https://jobs.ashbyhq.com/umbrella/xyz", "Umbrella")]
    [InlineData("https://apply.workable.com/hooli/j/ABC/", "Hooli")]
    [InlineData("https://contoso.wd5.myworkdayjobs.com/en-US/External/job/X", "Contoso")]
    [InlineData("https://careers.fabrikam.com/job/42", "Fabrikam")]
    [InlineData("https://www.tailspin.com/careers/engineer", "Tailspin")]
    public void CompanyFromJobBoardUrl(string url, string expected)
    {
        var r = JdExtractor.Extract("Some pasted text with no company mentioned", url);

        Assert.Equal(expected, r.Company);
    }

    [Fact]
    public void AggregatorUrl_DoesNotInventCompany()
    {
        var r = JdExtractor.Extract("blah blah", "https://www.indeed.com/viewjob?jk=abc");

        Assert.Null(r.Company);
        Assert.Equal("https://www.indeed.com/viewjob?jk=abc", r.Url);
    }

    [Fact]
    public void AboutSections_ThatAreNotCompanyNames_AreIgnored()
    {
        var r = JdExtractor.Extract("Software Engineer\nAbout the job\nAbout Us\nAbout this role\n");

        Assert.Equal("Software Engineer", r.Role);
        Assert.Null(r.Company);
    }

    [Fact]
    public void UrlInText_UsedWhenNoSourceUrl()
    {
        var r = JdExtractor.Extract("Apply at https://jobs.lever.co/globex/123, thanks.\nQA Engineer");

        Assert.Equal("https://jobs.lever.co/globex/123", r.Url);
        Assert.Equal("Globex", r.Company);
    }

    [Fact]
    public void NonWebSourceUrl_IsRejected()
    {
        var r = JdExtractor.Extract("Developer", "file:///C:/secret.txt");

        Assert.Null(r.Url);
    }

    [Fact]
    public void SentencesMentioningRoles_AreNotTitles()
    {
        var r = JdExtractor.Extract("We are looking for a passionate engineer.\nExperience: 5 years as a developer\n");

        Assert.Null(r.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void EmptyText_ReturnsNothing(string text)
    {
        var r = JdExtractor.Extract(text);

        Assert.Null(r.Company);
        Assert.Null(r.Role);
        Assert.Null(r.Url);
    }

    [Fact]
    public void SourceUrlFromHtmlClipboard_ReadsHeader()
    {
        const string html = "Version:0.9\r\nStartHTML:0000000105\r\nEndHTML:0000000200\r\nSourceURL:https://boards.greenhouse.io/acme/jobs/1\r\n<html>...";

        Assert.Equal("https://boards.greenhouse.io/acme/jobs/1", JdExtractor.SourceUrlFromHtmlClipboard(html));
        Assert.Null(JdExtractor.SourceUrlFromHtmlClipboard("Version:0.9\r\n<html>"));
        Assert.Null(JdExtractor.SourceUrlFromHtmlClipboard(null));
    }
}
