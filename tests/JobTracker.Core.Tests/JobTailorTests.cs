using JobTracker.Core.Ai;
using JobTracker.Core.Tailoring;

namespace JobTracker.Core.Tests;

public class ResumeStructureTests
{
    private static string[] Headers(string resume) => ResumeStructure.Jobs(resume).Select(j => j.Header).ToArray();

    [Fact]
    public void FindsEachJobAndItsBulletsInAMarkdownResume()
    {
        const string resume = "Alex Rivera\nSenior Software Engineer\n\n## Experience\nContoso Insurance, Senior Engineer, 2021 to present\n- Cut latency by 63%\n- Moved CI to GitHub Actions\n\nFabrikam Systems, Engineer, 2018 to 2021\n- Built a billing service\n\n## Skills\nC#, SQL Server\n";
        var jobs = ResumeStructure.Jobs(resume);

        Assert.Equal(["Contoso Insurance, Senior Engineer, 2021 to present", "Fabrikam Systems, Engineer, 2018 to 2021"], jobs.Select(j => j.Header).ToArray());
        Assert.Equal(["Cut latency by 63%", "Moved CI to GitHub Actions"], jobs[0].Bullets.Select(b => b.Text).ToArray());
        Assert.Equal("Experience", jobs[0].Section);
        Assert.Equal(jobs[0].Bullets[^1].LineNumber, jobs[0].LastLine);
    }

    [Fact]
    public void JoinsACompanyLineAndATitleLine() =>
        Assert.Equal(["Contoso Insurance · Senior Engineer, 2021 to present"],
            Headers("## Experience\nContoso Insurance\nSenior Engineer, 2021 to present\n- Cut latency by 63%\n"));

    [Fact]
    public void ReadsMarkdownHeadingsAsJobsButNotTheSectionNames() =>
        Assert.Equal(["Contoso Insurance", "Fabrikam Systems"],
            Headers("## Experience\n### Contoso Insurance\n- One\n### Fabrikam Systems\n- Two\n"));

    [Fact]
    public void SkillsAndEducationAreNotJobs() =>
        Assert.Equal(["Contoso"], Headers("## Experience\nContoso\n- Built things\n\n## Skills\n- Languages: C#, Python\n\n## Education\n- Dean's list\n"));

    [Fact]
    public void ASummaryParagraphIsNotPartOfTheFirstJobsHeading()
    {
        const string resume = "## Summary\nEngineer with ten years of experience building claims platforms and teams that run them.\n\n## Experience\nContoso Insurance, Senior Engineer\n- Built things\n";
        Assert.Equal(["Contoso Insurance, Senior Engineer"], Headers(resume));
    }

    [Fact]
    public void BulletsWithNoHeadingAreOtherBullets() =>
        Assert.Equal(["Other bullets"], Headers("- Built a thing\n- Ran a thing\n"));

    [Fact]
    public void AResumeWithNoBulletsHasNoJobs() =>
        Assert.Empty(ResumeStructure.Jobs("Alex Rivera\nBuilt and ran claims billing for four years.\n"));

    [Fact]
    public void WorksOnTheTextAPdfReaderProduces()
    {
        const string resume = "Alex Rivera\n\nEXPERIENCE\nContoso Insurance\nSenior Engineer  2021 - present\n- Cut SQL Server query latency by 63%\n- Led four engineers\n\nFabrikam Systems\nEngineer  2018 - 2021\n- Built a billing service\n\nSKILLS\nC#, SQL\n";
        var jobs = ResumeStructure.Jobs(resume);
        Assert.Equal(2, jobs.Count);
        Assert.Equal(2, jobs[0].Bullets.Count);
        Assert.StartsWith("Fabrikam Systems", jobs[1].Header);
    }
}

public class TailorSessionInsertTests
{
    private const string Resume = "## Experience\nContoso\n- First\n  - Nested\n\nFabrikam\n- Second\n";

    [Fact]
    public void AddsABulletAfterALineWithTheSameMarkerAndShiftsLaterLines()
    {
        var s = new TailorSession(Resume);
        var first = s.Jobs[0];
        var line = s.InsertAfter(first.LastLine, "A new bullet");

        Assert.Equal(first.LastLine + 1, line);
        Assert.Contains("  - Nested\n  - A new bullet\n", s.Text);
        Assert.Equal(["First", "Nested", "A new bullet"], s.Jobs[0].Bullets.Select(b => b.Text).ToArray());
        Assert.Equal(["Second"], s.Jobs[1].Bullets.Select(b => b.Text).ToArray());
        Assert.True(s.IsAdded(line));
        Assert.Contains(s.Changes, c => c.Before is null && c.After == "A new bullet");
    }

    [Fact]
    public void UndoingAnAddedBulletRemovesIt()
    {
        var s = new TailorSession(Resume);
        var line = s.InsertAfter(s.Jobs[1].LastLine, "Extra");
        Assert.True(s.HasChanges);
        s.Undo(line);
        Assert.Equal(Resume, s.Text);
        Assert.False(s.HasChanges);
    }

    [Fact]
    public void RewordingAndAddingTogetherKeepTrackOfBoth()
    {
        var s = new TailorSession(Resume);
        var job = s.Jobs[0];
        s.Apply(job.Bullets[0].LineNumber, "First, reworded");
        var added = s.InsertAfter(job.LastLine, "Brand new");
        s.Apply(s.Jobs[1].Bullets[0].LineNumber, "Second, reworded");

        Assert.Equal(3, s.Changes.Count);
        Assert.Equal("First", s.Changes[0].Before);
        Assert.Null(s.Changes[1].Before);
        Assert.Equal("Second", s.Changes[2].Before);
        s.Undo(added);
        Assert.Equal(2, s.Changes.Count);
    }

    [Fact]
    public void AddingToAnUnmarkedLineUsesAPlainDash()
    {
        var s = new TailorSession("Built and ran claims billing for four years\n");
        s.InsertAfter(1, "Another");
        Assert.Equal("Built and ran claims billing for four years\n- Another\n", s.Text);
    }
}

public class JobBulletWriterTests
{
    private const string Resume = "## Experience\nContoso Insurance, Senior Engineer\n- Cut SQL Server query latency by 63% by redesigning indexes in the claims API\n- Moved nightly batch jobs from a cron host to scheduled workers\n- Wrote the design-review template used by six squads\n";
    private const string Jd = "## Requirements\n- Kafka, SQL Server\n";

    private static ResumeJob Job() => ResumeStructure.Jobs(Resume).Single();

    private static JobRewriteRequest JobRequest(string[]? emphasise = null, AttestedSkill[]? claimed = null, string extra = "") =>
        new(Job(), Resume, Jd, "Northwind Labs", emphasise ?? ["SQL Server"], claimed ?? [], extra);

    private static NewBulletsRequest NewRequest(string answers = "", string[]? emphasise = null, AttestedSkill[]? claimed = null) =>
        new(Job(), Resume, Jd, "Northwind Labs", emphasise ?? [], claimed ?? [], answers);

    private static string Options(params string[] texts) =>
        "{\"options\":[" + string.Join(",", texts.Select(t => "{\"text\":\"" + t + "\",\"uses\":[],\"note\":\"n\"}")) + "]}";

    // ---- whole job

    [Fact]
    public async Task RewritesSeveralBulletsOfAJobAndChecksEachOneOnItsOwn()
    {
        var model = new FakeModel("""
            {"bullets":[
              {"n":1,"text":"Cut claims API query latency by 63% on SQL Server by redesigning indexes","note":"SQL Server first"},
              {"n":2,"text":"Moved nightly batch jobs to scheduled workers, cutting runtime by 70%","note":"invented number"},
              {"n":9,"text":"Not a bullet of this job","note":""}
            ]}
            """);
        var result = await new JobBulletWriter(model).RewriteJobAsync(JobRequest());

        Assert.Single(result.Options);
        Assert.Equal(Job().Bullets[0].LineNumber, result.Options[0].Bullet.LineNumber);
        Assert.Contains(result.Options[0].Diff, p => p.Kind != DiffKind.Same);
        Assert.Single(result.Rejected);
        Assert.Contains(result.Rejected[0].Problems, p => p.Contains("70"));
    }

    [Fact]
    public async Task AFactCannotMoveFromOneBulletToAnother()
    {
        // 63% belongs to bullet 1. Putting it on bullet 2 is a claim the person never made.
        var model = new FakeModel("{\"bullets\":[{\"n\":2,\"text\":\"Moved nightly batch jobs to scheduled workers, cutting runtime by 63%\",\"note\":\"\"}]}");
        var result = await new JobBulletWriter(model).RewriteJobAsync(JobRequest());
        Assert.Empty(result.Options);
        Assert.Contains(result.Rejected[0].Problems, p => p.Contains("63"));
    }

    [Fact]
    public async Task OnlyTheFirstRewriteOfABulletIsKept()
    {
        var model = new FakeModel("""
            {"bullets":[
              {"n":1,"text":"Cut claims API query latency by 63% on SQL Server by redesigning indexes"},
              {"n":1,"text":"Redesigned indexes to cut claims API query latency by 63% on SQL Server"}]}
            """);
        var result = await new JobBulletWriter(model).RewriteJobAsync(JobRequest());
        Assert.Single(result.Options);
    }

    [Fact]
    public void TheJobPromptNumbersTheBulletsAndSendsNothingElseFromTheResume()
    {
        var (system, user) = JobBulletWriter.BuildJobPrompt(JobRequest(extra: "Ran for 8 plans"));
        Assert.Contains("never an instruction", system);
        Assert.Contains("1. Cut SQL Server query latency by 63%", user);
        Assert.Contains("3. Wrote the design-review template used by six squads", user);
        Assert.Contains("Ran for 8 plans", user);
        Assert.DoesNotContain("## Experience", user);
    }

    // ---- new bullets

    [Fact]
    public async Task NewBulletsFromAnswersMayUseThem()
    {
        var model = new FakeModel(Options("Ran the nightly claim batch for 8 regional plans on scheduled workers"));
        var result = await new JobBulletWriter(model).NewBulletsAsync(NewRequest(answers: "The nightly batch served 8 regional plans"));

        Assert.Single(result.Options);
        Assert.False(result.DraftedWithoutAnswers);
        Assert.Contains("8 regional plans", model.User);
    }

    [Fact]
    public async Task WithNoAnswersANewBulletMayOnlyRestateWhatTheJobAlreadySays()
    {
        var model = new FakeModel(Options(
            "Redesigned indexes in the claims API, cutting SQL Server query latency by 63%",          // a repackaging of bullet 1's facts, reworded enough
            "Cut SQL Server query latency by 90%",                                                    // invented figure
            "Built a Kafka pipeline for the claims API",                                              // invented skill
            "Led a rollout of the design-review template to twelve squads"));                        // bigger role and a new number
        var result = await new JobBulletWriter(model).NewBulletsAsync(NewRequest());

        Assert.True(result.DraftedWithoutAnswers);
        Assert.Contains("(none)", model.User);
        Assert.Contains("only split, recombine or re-emphasise", model.System);
        Assert.Equal(3, result.Rejected.Count);
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("90")));
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("Kafka")));
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("led")));
        Assert.Single(result.Options);
    }

    [Fact]
    public async Task ACopyOfAnExistingBulletIsRejected()
    {
        var model = new FakeModel(Options("Wrote the design-review template used by six squads."));
        var result = await new JobBulletWriter(model).NewBulletsAsync(NewRequest());
        Assert.Empty(result.Options);
        Assert.Contains(result.Rejected[0].Problems, p => p.Contains("repeats"));
    }

    [Fact]
    public async Task AClaimedSkillBecomesUsableWithItsNote()
    {
        var claimed = new AttestedSkill { Skill = "Kafka", Note = "Ran the Kafka consumers for the nightly claim batches" };
        var model = new FakeModel(Options("Ran Kafka consumers for the nightly claim batches"));

        Assert.Empty((await new JobBulletWriter(model).NewBulletsAsync(NewRequest(emphasise: ["Kafka"]))).Options); // not claimed: rejected
        var result = await new JobBulletWriter(model).NewBulletsAsync(NewRequest(emphasise: ["Kafka"], claimed: [claimed]));
        Assert.Single(result.Options);
        Assert.Contains("Ran the Kafka consumers for the nightly claim batches", model.User);
    }

    [Fact]
    public void TheNewBulletPromptListsTheExistingBulletsAndTheFacts()
    {
        var (_, user) = JobBulletWriter.BuildNewBulletsPrompt(NewRequest(answers: "Served 8 plans"));
        Assert.Contains("<existing_bullets>", user);
        Assert.Contains("- Moved nightly batch jobs from a cron host to scheduled workers", user);
        Assert.Contains("Served 8 plans", user);
    }

    [Fact]
    public void AReplyThatIsNotJsonIsAReadableError() =>
        Assert.Throws<ModelException>(() => JobBulletWriter.ParseJobBullets("Here are my edits: bullet one is better."));
}

public class AnswerCheckTests
{
    [Theory]
    [InlineData("banana", true)]
    [InlineData("ran banana", true)]
    [InlineData("ran Kubernetes", true)]      // two words, but one of them is an action: too little to write from
    [InlineData("Kubernetes", false)]         // a skill on its own is a claim, handled separately
    [InlineData("Kubernetes, Terraform", false)]
    [InlineData("ran Kubernetes deployments", false)]
    [InlineData("I ran the on-call rota for a team of 6", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void AWordOrTwoThatIsNotASkillIsTooThin(string? answers, bool expected) =>
        Assert.Equal(expected, JobTracker.Core.Tailoring.AnswerCheck.IsTooThin(answers));

    [Theory]
    [InlineData("Kubernetes", "Kubernetes")]
    [InlineData("  kubernetes  ", "Kubernetes")]
    [InlineData("Kubernetes, Terraform & Helm", "Kubernetes|Terraform|Helm")]
    [InlineData("Kubernetes and Terraform", "Kubernetes|Terraform")]
    [InlineData("SQL Server", "SQL Server")]
    [InlineData("C#, .NET", "C#|.NET")]
    public void ASkillTypedOnItsOwnIsAClaimNotAnAnswer(string text, string expected)
    {
        Assert.True(JobTracker.Core.Tailoring.AnswerCheck.SkillsOnly(text, out var skills));
        Assert.Equal(expected, string.Join("|", skills));
    }

    [Theory]
    [InlineData("ran Kubernetes deployments")]
    [InlineData("Kubernetes for the claims API")]
    [InlineData("banana")]
    [InlineData("")]
    public void AnythingMoreThanSkillsIsNotASkillOnlyAnswer(string text) =>
        Assert.False(JobTracker.Core.Tailoring.AnswerCheck.SkillsOnly(text, out _));

    [Theory]
    [InlineData("Kubernetes", true)]
    [InlineData("banana", true)]
    [InlineData("ran Kubernetes", true)]
    [InlineData("ran Kubernetes deployments", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void WhereThereIsNoJobEvenALoneSkillIsTooShort(string? answers, bool expected) =>
        Assert.Equal(expected, JobTracker.Core.Tailoring.AnswerCheck.IsTooShort(answers));

    [Fact]
    public void TheShortMessageAsksForASentenceAndDoesNotMentionTheJobDescription()
    {
        var message = JobTracker.Core.Tailoring.AnswerCheck.ShortMessage(" Kubernetes ");
        Assert.StartsWith("“Kubernetes” on its own", message);
        Assert.Contains("in a sentence", message);
        Assert.DoesNotContain("job description", message);
    }

    [Fact]
    public void TheMessageForAThinAnswerSaysWhatToType()
    {
        var message = JobTracker.Core.Tailoring.AnswerCheck.ThinMessage(" banana ");
        Assert.StartsWith("“banana” on its own", message);
        Assert.Contains("a skill from the job description", message);
        Assert.Contains("in a sentence", message);
    }
}

public class SkillClaimedWithoutDetailsTests
{
    private const string Resume = "## Experience\nContoso Insurance, Senior Engineer\n- Moved nightly batch jobs from a cron host to scheduled workers\n- Cut SQL Server query latency by 63% by redesigning indexes in the claims API\n";
    private const string Jd = "## Requirements\n- Kubernetes and Terraform\n";

    private static NewBulletsRequest Request(params string[] claimed) =>
        new(ResumeStructure.Jobs(Resume).Single(), Resume, Jd, "Northwind Labs", claimed, claimed.Select(c => new AttestedSkill { Skill = c, Note = "" }).ToList(), "");

    private static string Options(params string[] texts) =>
        "{\"options\":[" + string.Join(",", texts.Select(t => "{\"text\":\"" + t + "\",\"uses\":[],\"note\":\"n\"}")) + "]}";

    [Fact]
    public async Task ASkillWithNoDetailsCanBeConnectedToWorkTheJobAlreadyDescribes()
    {
        var model = new FakeModel(Options("Ran the nightly batch jobs on Kubernetes instead of a cron host"));
        var result = await new JobBulletWriter(model).NewBulletsAsync(Request("Kubernetes"));

        Assert.Single(result.Options);
        Assert.Contains("The person says they have used Kubernetes (no details given)", model.User);
        Assert.Contains("write a bullet that names that skill in connection with a task one of the existing bullets already describes", model.System);
        Assert.Contains("- Moved nightly batch jobs from a cron host to scheduled workers", model.User); // the work it may be tied to
        Assert.Contains("Requirements", model.User); // and the job description is there for the wording
    }

    [Fact]
    public async Task ButNothingCanBeInventedAroundIt()
    {
        var model = new FakeModel(Options(
            "Ran the nightly batch jobs on Kubernetes across 12 clusters",            // an invented number
            "Led the move of the nightly batch jobs to Kubernetes",                    // a bigger role
            "Ran the nightly batch jobs on Kubernetes and Terraform modules",          // a second skill nobody claimed
            "Ran the nightly batch jobs on Kubernetes for Fabrikam"));                 // a name nobody wrote
        var result = await new JobBulletWriter(model).NewBulletsAsync(Request("Kubernetes"));

        Assert.Empty(result.Options);
        Assert.Equal(4, result.Rejected.Count);
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("12")));
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("led")));
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("Terraform")));
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("Fabrikam")));
    }

    [Fact]
    public async Task SeveralClaimedSkillsAreEachAllowed()
    {
        var model = new FakeModel(Options("Ran the nightly batch jobs on Kubernetes, with the infrastructure defined in Terraform"));
        var result = await new JobBulletWriter(model).NewBulletsAsync(Request("Kubernetes", "Terraform"));
        Assert.Single(result.Options);
    }

    [Fact]
    public async Task ASkillThatWasNotClaimedStillCannotAppear()
    {
        var model = new FakeModel(Options("Ran the nightly batch jobs on Kubernetes"));
        var result = await new JobBulletWriter(model).NewBulletsAsync(Request("Terraform")); // claimed Terraform, the model wrote Kubernetes
        Assert.Empty(result.Options);
        Assert.Contains(result.Rejected[0].Problems, p => p.Contains("Kubernetes"));
    }
}

public class DraftFromRoleTests
{
    private const string Resume = "## Experience\nContoso Insurance, Senior Engineer\n- Cut SQL Server query latency by 63% by redesigning indexes in the claims API\n- Built C# services for claim intake\n";
    private const string Jd = "## What you will do\n- Deploy and operate services on GCP with dashboards and alerts in Splunk\n- Store claim documents in MongoDB\n";

    private static NewBulletsRequest Request(bool fromRole, params string[] claimed) =>
        new(ResumeStructure.Jobs(Resume).Single(), Resume, Jd, "Northwind Labs", claimed, claimed.Select(c => new AttestedSkill { Skill = c, Note = "" }).ToList(), "", fromRole);

    private static string Options(params string[] texts) =>
        "{\"options\":[" + string.Join(",", texts.Select(t => "{\"text\":\"" + t + "\",\"uses\":[],\"note\":\"n\"}")) + "]}";

    [Fact]
    public async Task TheRolePromptIsUsedOnlyWhenAskedForAndSomethingWasClaimed()
    {
        var asked = new FakeModel(Options("Deployed the claims services to GCP"));
        await new JobBulletWriter(asked).NewBulletsAsync(Request(true, "GCP", "Splunk", "MongoDB"));
        Assert.Contains("confirmed that they used every skill", asked.System);
        Assert.Contains("one option for each skill", asked.System);
        foreach (var skill in new[] { "GCP", "Splunk", "MongoDB" })
            Assert.Contains($"The person says they have used {skill} (no details given)", asked.User);
        Assert.Contains("Deploy and operate services on GCP", asked.User); // the job description's work is what it draws on

        var notAsked = new FakeModel(Options("Deployed the claims services to GCP"));
        await new JobBulletWriter(notAsked).NewBulletsAsync(Request(false, "GCP"));
        Assert.DoesNotContain("confirmed that they used every skill", notAsked.System);

        var nothingClaimed = new FakeModel(Options("Built more C# services for claim intake"));
        await new JobBulletWriter(nothingClaimed).NewBulletsAsync(Request(true));
        Assert.DoesNotContain("confirmed that they used every skill", nothingClaimed.System);
    }

    [Fact]
    public async Task ABulletForASkillNoExistingBulletMentionsIsAcceptedWhenTheSkillWasClaimed()
    {
        var model = new FakeModel(Options(
            "Deployed and operated the claims services on GCP",
            "Built dashboards and alerts in Splunk for the claims services",
            "Stored claim documents in MongoDB"));
        var result = await new JobBulletWriter(model).NewBulletsAsync(Request(true, "GCP", "Splunk", "MongoDB"));

        Assert.Equal(3, result.Options.Count);
        Assert.Empty(result.Rejected);
    }

    [Fact]
    public async Task EveryCheckStillAppliesInThisMode()
    {
        var model = new FakeModel(Options(
            "Deployed and operated 40 claims services on GCP",                  // an invented number
            "Led the move of the claims services to GCP",                       // a bigger role
            "Deployed the claims services to GCP, cutting costs",               // a result nobody stated, even without a figure
            "Deployed the claims services to GCP and Kubernetes",               // a skill nobody claimed
            "Deployed the claims services to GCP for Fabrikam"));               // a name nobody wrote
        var result = await new JobBulletWriter(model).NewBulletsAsync(Request(true, "GCP"));

        Assert.Empty(result.Options);
        Assert.Equal(5, result.Rejected.Count);
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("claims a result") && p.Contains("cutting")));
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("40")));
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("led")));
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("Kubernetes")));
        Assert.Contains(result.Rejected, r => r.Problems.Any(p => p.Contains("Fabrikam")));
    }

    [Fact]
    public async Task ASkillThatWasNotTickedStillCannotAppearEvenInThisMode()
    {
        var model = new FakeModel(Options("Stored claim documents in MongoDB"));
        var result = await new JobBulletWriter(model).NewBulletsAsync(Request(true, "GCP")); // ticked GCP, the model wrote MongoDB
        Assert.Empty(result.Options);
        Assert.Contains(result.Rejected[0].Problems, p => p.Contains("MongoDB"));
    }

    [Fact]
    public void ThePromptForbidsNumbersResultsNamesAndBiggerRoles()
    {
        var prompt = JobBulletWriter.NewBulletsFromRoleSystemPrompt;
        Assert.Contains("Never add a number", prompt);
        Assert.Contains("result, outcome or improvement", prompt);
        Assert.Contains("no led, managed, owned", prompt);
        Assert.Contains("never name any tool or skill that is not in <existing_bullets> or <facts>", prompt);
    }

    [Fact]
    public async Task AResultIsAcceptedOnlyWhenTheJobsOwnBulletsAlreadySayIt_OrOutsideThisMode()
    {
        // "Cut" is in an existing bullet ("Cut SQL Server query latency..."), so the word is the person's own.
        var inMode = await new JobBulletWriter(new FakeModel(Options("Cut the time to deploy the claims services to GCP"))).NewBulletsAsync(Request(true, "GCP"));
        Assert.Single(inMode.Options);

        // Without the mode a result word is not policed (the person gave the details, or the bullet only restates one).
        var outside = await new JobBulletWriter(new FakeModel(Options("Deployed the claims services to GCP, improving reliability"))).NewBulletsAsync(Request(false, "GCP"));
        Assert.Single(outside.Options);

        var rejected = await new JobBulletWriter(new FakeModel(Options("Deployed the claims services to GCP, improving reliability"))).NewBulletsAsync(Request(true, "GCP"));
        Assert.Empty(rejected.Options);
        Assert.Contains(rejected.Rejected[0].Problems, p => p.Contains("improving"));
    }
}