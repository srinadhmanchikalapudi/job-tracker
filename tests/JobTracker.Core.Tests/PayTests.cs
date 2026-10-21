using JobTracker.Core.Pay;

namespace JobTracker.Core.Tests;

public class PayExtractorTests
{
    private static string? Show(string text) => PayExtractor.Extract(text)?.Display;

    // ---------------------------------------------------------------- full-time salaries

    [Theory]
    [InlineData("Salary: $120,000 - $150,000 per year", "$120,000 – $150,000 per year")]
    [InlineData("The base salary range for this role is $140,000 to $170,000 annually.", "$140,000 – $170,000 per year")]
    [InlineData("Pay range: $95K - $125K", "$95,000 – $125,000 per year (unit not stated, assumed)")]
    [InlineData("Compensation: $120-150k + equity", "$120,000 – $150,000 per year (unit not stated, assumed)")]
    [InlineData("$130,000.00 - $160,000.00 a year", "$130,000 – $160,000 per year")]
    [InlineData("Estimated salary: $115,000–$135,000/yr", "$115,000 – $135,000 per year")]
    [InlineData("Salary range: £50,000 - £60,000 per annum", "£50,000 – £60,000 per year")]
    [InlineData("Salary: €70.000 - €85.000", null)] // a European thousands point is not understood, so nothing is claimed
    [InlineData("Base pay: C$110,000 - C$135,000 per year", "CA$110,000 – CA$135,000 per year")]
    [InlineData("Salary: 120,000 - 150,000 USD per year", "120,000 – 150,000 USD per year")]
    [InlineData("Up to $150,000 per year depending on experience", "Up to $150,000 per year")]
    [InlineData("Starting at $110,000 annually", "From $110,000 per year")]
    [InlineData("Salary: $100K+", "From $100,000 per year")]
    public void ReadsSalaries(string text, string? expected) => Assert.Equal(expected, Show(text));

    // ---------------------------------------------------------------- contract and hourly work

    [Theory]
    [InlineData("Pay: $50 - $60 an hour", "$50 – $60 per hour")]
    [InlineData("Pay Rate: $55/hr on W2", "$55 per hour")]
    [InlineData("Rate: $65 - $75 per hour (C2C)", "$65 – $75 per hour")]
    [InlineData("$45.00 - $55.00 Hourly", "$45 – $55 per hour")]
    [InlineData("Hourly rate: $52.50", "$52.50 per hour")]
    [InlineData("Compensation: 45-55 USD/hr", "45 – 55 USD per hour")]
    [InlineData("Day rate: $500 - $650 per day", "$500 – $650 per day")]
    [InlineData("This contract pays $8,000 - $10,000 per month.", "$8,000 – $10,000 per month")]
    [InlineData("Weekly pay of $2,400 per week", "$2,400 per week")]
    [InlineData("Contract, 6 months. $70/hour, 1099.", "$70 per hour")]
    [InlineData("Up to $85/hr depending on experience", "Up to $85 per hour")]
    public void ReadsContractAndHourlyRates(string text, string? expected) => Assert.Equal(expected, Show(text));

    [Theory]
    [InlineData("Salary Range: $130,000 - $160,000 USD", "$130,000 – $160,000 per year")]
    [InlineData("Annual Salary: $100,000", "$100,000 per year")]
    [InlineData("Base salary $90,000 + 10% bonus", "$90,000 per year")]
    [InlineData("$25 - $30 per hour + overtime", "$25 – $30 per hour")]
    [InlineData("Pay: $120K-$140K", "$120,000 – $140,000 per year (unit not stated, assumed)")]
    public void ReadsMoreWaysOfSayingIt(string text, string? expected) => Assert.Equal(expected, Show(text));

    // ---------------------------------------------------------------- the same inside a whole posting

    [Fact]
    public void FindsPayInAJobDetailsBoxOfAPastedPage()
    {
        const string page = """
            Senior Backend Engineer
            Northwind Labs · Seattle, WA (Hybrid)

            Job details
            Pay
            $150,000 - $180,000 a year
            Job type
            Full-time

            About the job
            We build claims software with $5 billion in annual claims flowing through it. We raised $120 million in Series C funding last year.
            """;
        var pay = PayExtractor.Extract(page);
        Assert.Equal("$150,000 – $180,000 per year", pay!.Display);
        Assert.Contains("$150,000", pay.Source);
    }

    [Fact]
    public void ReadsAnIndeedStyleContractPosting()
    {
        const string page = "Contract .NET Developer\nFabrikam Systems - Remote\n\nPay\n$60 - $70 an hour\n\nJob type\nContract\nW2\n\nFull job description\nWe need a developer for six months.";
        Assert.Equal("$60 – $70 per hour", Show(page));
    }

    [Fact]
    public void ReadsPayWrittenInAParagraph()
    {
        const string text = "At Contoso the expected salary range for this position is between $128,000 and $162,000 per year, based on location and skills. We also offer a 401(k) with a 4% match.";
        Assert.Equal("$128,000 – $162,000 per year", Show(text));
    }

    // ---------------------------------------------------------------- nothing stated

    [Theory]
    [InlineData("Competitive salary and benefits. Pay is commensurate with experience.")]
    [InlineData("Salary: DOE")]
    [InlineData("Salary: Competitive")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Great team, great perks, come grow with us.")]
    public void GivesNothingWhenThePostingStatesNoPay(string text) => Assert.Null(PayExtractor.Extract(text));

    [Fact]
    public void GivesNothingForNull() => Assert.Null(PayExtractor.Extract(null));

    // ---------------------------------------------------------------- money that is not pay

    [Theory]
    [InlineData("We have raised $120 million in funding and have $5M in annual revenue.")]
    [InlineData("A $10,000 sign-on bonus is available for the right candidate.")]
    [InlineData("Relocation assistance of up to $15,000 is provided.")]
    [InlineData("Annual learning stipend of $2,000.")]
    [InlineData("A 401(k) plan with company match up to $6,000 a year.")]
    [InlineData("Equity grant valued at $200,000 vesting over four years.")]
    [InlineData("You will manage a $5 billion portfolio.")]
    [InlineData("Employees get a $50 gift card at the holidays.")]
    [InlineData("Series B funding of $45,000,000 led by investors.")]
    public void IgnoresMoneyThatIsNotPay(string text) => Assert.Null(PayExtractor.Extract(text));

    [Fact]
    public void ABonusNextToRealPayDoesNotReplaceIt()
    {
        const string text = "Salary: $120,000 - $140,000 per year. Plus a $10,000 sign-on bonus.";
        Assert.Equal("$120,000 – $140,000 per year", Show(text));
    }

    [Theory]
    [InlineData("Expect 40-50 hours per week during launches.")]
    [InlineData("3-5 years of experience per team.")]
    [InlineData("Call 555-0100 or 555-0111 per the instructions.")]
    [InlineData("Founded in 2015 - 2019 growth years per annum report.")]
    public void NumbersThatAreNotMoneyAreIgnored(string text) => Assert.Null(PayExtractor.Extract(text));

    // ---------------------------------------------------------------- several figures, and the details

    [Fact]
    public void WithOneRangePerCityItTakesTheFirstAndSaysThereAreMore()
    {
        const string text = "Pay range by location:\nSeattle: $150,000 - $180,000 per year\nAustin: $130,000 - $160,000 per year\nRemote: $120,000 - $150,000 per year";
        var pay = PayExtractor.Extract(text)!;
        Assert.Equal("$150,000 – $180,000 per year", pay.Display);
        Assert.Equal(2, pay.OtherRanges);
    }

    [Fact]
    public void ARangeBeatsASingleFigureAndAPayLabelBeatsAStrayAmount()
    {
        const string text = "You will own a $20 budget line.\nSalary: $90,000 - $110,000 per year\nTeam lunch is up to $15 per person per day.";
        Assert.Equal("$90,000 – $110,000 per year", Show(text));
    }

    [Fact]
    public void KeepsTheLineItCameFrom()
    {
        var pay = PayExtractor.Extract("About the role\nSalary: $90,000 - $110,000 per year (DOE)\nMore text")!;
        Assert.Equal("Salary: $90,000 - $110,000 per year (DOE)", pay.Source);
    }

    [Fact]
    public void WorksWithNonBreakingSpacesAndEnDashesFromWebPages()
    {
        Assert.Equal("$120,000 – $150,000 per year", Show("Salary: $120,000 – $150,000 per year"));
    }

    [Fact]
    public void AnHourlyRateShowsWhatItIsRoughlyWorthInAYear()
    {
        var pay = PayExtractor.Extract("Pay: $50 - $60 an hour")!;
        Assert.Equal("About $104,000 – $124,800 per year (40 hours a week for 52 weeks)", pay.YearlyEquivalent);
        Assert.Null(PayExtractor.Extract("Salary: $120,000 - $150,000 per year")!.YearlyEquivalent);
    }

    [Fact]
    public void ADailyRateIsConvertedOver260Days()
    {
        var pay = PayExtractor.Extract("Day rate: $500 per day")!;
        Assert.Equal("About $130,000 per year (260 working days)", pay.YearlyEquivalent);
    }

    [Fact]
    public void ImplausibleFiguresAreNotPay()
    {
        Assert.Null(PayExtractor.Extract("Salary: $1 - $2 per year"));
        Assert.Null(PayExtractor.Extract("Rate: $5,000 - $9,000 per hour"));
    }

    [Fact]
    public void TheSameTextAlwaysGivesTheSameAnswer()
    {
        const string text = "Salary: $120,000 - $150,000 per year";
        Assert.Equal(PayExtractor.Extract(text), PayExtractor.Extract(text));
    }
}

public class PayShortFormTests
{
    private static string? Short(string text) => PayExtractor.Extract(text)?.Short;

    [Theory]
    [InlineData("Pay: $60 - $70 an hour", "$60–$70/hr")]
    [InlineData("Salary: $140,000 - $170,000 per year", "$140k–$170k/yr")]
    [InlineData("Salary: $125,500 - $150,000 per year", "$125.5k–$150k/yr")]
    [InlineData("Salary: $123,456 per year", "$123,456/yr")]
    [InlineData("Up to $85/hr depending on experience", "Up to $85/hr")]
    [InlineData("Day rate: $500 - $650 per day", "$500–$650/day")]
    [InlineData("This contract pays $8,000 - $10,000 per month.", "$8,000–$10,000/mo")]
    [InlineData("Salary: 120,000 - 150,000 USD per year", "120k–150k USD/yr")]
    [InlineData("Salary range: £50,000 - £60,000 per annum", "£50k–£60k/yr")]
    [InlineData("Starting at $110,000 annually", "From $110k/yr")]
    public void IsCompactEnoughForTheList(string text, string expected) => Assert.Equal(expected, Short(text));

    [Fact]
    public void SurvivesBeingSavedAsJson()
    {
        var pay = PayExtractor.Extract("Salary: $140,000 - $170,000 per year")!;
        var json = System.Text.Json.JsonSerializer.Serialize(pay);
        Assert.DoesNotContain("Display", json); // worked out from the figures, not stored
        Assert.Equal(pay, System.Text.Json.JsonSerializer.Deserialize<PayInfo>(json));
    }
}

public class PayCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jt-paycache-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_root, true);

    private (ApplicationStore Store, JobApplication App) Seed(string jd)
    {
        var store = new ApplicationStore(_root);
        var app = store.Create(new NewApplication("Acme", "Engineer", jd, "Alex", null, new DateOnly(2026, 10, 1)));
        return (store, new ApplicationStore(_root).List().Single(a => a.FolderPath == app.FolderPath));
    }

    [Fact]
    public void RemembersThePayAndDoesNotReadTheFileAgain()
    {
        var (store, app) = Seed("Engineer\nPay: $60 - $70 an hour\n");
        var cache = new PayCache(_root);
        Assert.False(store.TryKnownPay(app, cache, out _)); // nothing remembered yet
        Assert.Equal("$60–$70/hr", store.ReadPayCached(app, cache)!.Short);
        cache.Save();

        // Change the words but keep the size and the last-changed time: only a file that is read again would notice.
        var jdPath = app.JdPath;
        var stamp = File.GetLastWriteTimeUtc(jdPath);
        var text = File.ReadAllText(jdPath);
        File.WriteAllText(jdPath, text.Replace("$60 - $70", "$80 - $90"));
        File.SetLastWriteTimeUtc(jdPath, stamp);

        var later = new PayCache(_root);
        Assert.True(store.TryKnownPay(app, later, out var pay));
        Assert.Equal("$60–$70/hr", pay!.Short);
    }

    [Fact]
    public void ADescriptionThatChangedIsReadAgain()
    {
        var (store, app) = Seed("Engineer\nPay: $60 - $70 an hour\n");
        var cache = new PayCache(_root);
        store.ReadPayCached(app, cache);
        cache.Save();

        File.WriteAllText(app.JdPath, "Engineer\nSalary: $100,000 - $120,000 per year, and a little more text\n");

        var later = new PayCache(_root);
        Assert.False(store.TryKnownPay(app, later, out _));
        Assert.Equal("$100k–$120k/yr", store.ReadPayCached(app, later)!.Short);
    }

    [Fact]
    public void RemembersThatThereIsNoPayToo()
    {
        var (store, app) = Seed("Engineer\nCompetitive salary.\n");
        var cache = new PayCache(_root);
        Assert.Null(store.ReadPayCached(app, cache));
        cache.Save();

        Assert.True(store.TryKnownPay(app, new PayCache(_root), out var pay));
        Assert.Null(pay);
    }

    [Fact]
    public void ACorruptOrMissingCacheFileIsJustEmpty()
    {
        var (store, app) = Seed("Engineer\nPay: $60 - $70 an hour\n");
        Directory.CreateDirectory(Path.Combine(_root, ".jobtracker"));
        File.WriteAllText(Path.Combine(_root, ".jobtracker", PayCache.FileName), "{ this is not json");
        var cache = new PayCache(_root);
        Assert.False(store.TryKnownPay(app, cache, out _));
        Assert.Equal("$60–$70/hr", store.ReadPayCached(app, cache)!.Short);
    }

    [Fact]
    public void TheOriginalPasteCountsToo()
    {
        var store = new ApplicationStore(_root);
        var created = store.Create(new NewApplication("Acme", "Engineer", "Engineer\nGreat team.\n", "Alex", null, new DateOnly(2026, 10, 1),
            OriginalJd: "Engineer\nPay\n$55 - $65 an hour\nGreat team.\n"));
        var app = new ApplicationStore(_root).List().Single(a => a.FolderPath == created.FolderPath);
        var cache = new PayCache(_root);
        Assert.Equal("$55–$65/hr", store.ReadPayCached(app, cache)!.Short);
        cache.Save();

        File.WriteAllText(Path.Combine(app.FolderPath, ApplicationStore.OriginalJdFile), "Engineer\nPay\n$75 - $85 an hour\nGreat team and more.\n");
        Assert.False(store.TryKnownPay(app, new PayCache(_root), out _)); // the original changed, so the answer is not trusted
    }
}