namespace JobTracker.Core.Tailoring;

/// <summary>How much a requirement matters for a kind of job. The numbers are the weights used in the score.</summary>
public enum Importance
{
    Optional = 1,
    Normal = 2,
    Important = 3,
    Critical = 5,
}

/// <summary>
/// A kind of technical job and what it cares about. The same job description is scored differently for a .NET developer and for a cloud engineer:
/// Azure Functions outweighs Terraform for the first, and the other way round for the second. Chosen from the job title, or by the person.
/// </summary>
public sealed class RoleProfile
{
    private readonly Dictionary<string, Importance> _skills;
    private readonly Dictionary<SkillCategory, Importance> _categories;

    internal RoleProfile(string id, string name, string[] titleSignals, string[] bodySignals, string[] critical, string[] important, string[] optional,
        Dictionary<SkillCategory, Importance> categories)
    {
        Id = id;
        Name = name;
        TitleSignals = titleSignals;
        BodySignals = bodySignals;
        _categories = categories;
        _skills = new Dictionary<string, Importance>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in optional) _skills[s] = Importance.Optional;
        foreach (var s in important) _skills[s] = Importance.Important;
        foreach (var s in critical) _skills[s] = Importance.Critical;
    }

    public string Id { get; }
    public string Name { get; }

    /// <summary>Words in a job title that point to this kind of job.</summary>
    public IReadOnlyList<string> TitleSignals { get; }

    /// <summary>Skills that, found in the job description, point to this kind of job.</summary>
    public IReadOnlyList<string> BodySignals { get; }

    public IEnumerable<string> NamedSkills => _skills.Keys;

    public Importance Of(string skill, SkillCategory category) =>
        _skills.TryGetValue(skill, out var named) ? named : _categories.TryGetValue(category, out var byCategory) ? byCategory : Importance.Normal;
}

public sealed record RoleChoice(RoleProfile Profile, bool Detected, string Reason);

public static class RoleProfiles
{
    private static Dictionary<SkillCategory, Importance> Cats(params (SkillCategory, Importance)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    public static readonly RoleProfile DotNet = new("dotnet", ".NET Developer",
        [".net", "dotnet", "c#", "asp.net", "csharp"], ["C#", ".NET", "ASP.NET Core", "Entity Framework"],
        critical: ["C#", ".NET", "ASP.NET Core", "SQL Server", "Entity Framework", "REST", "SQL"],
        important:
        [
            "Azure", "Azure Functions", "Azure DevOps", "Azure App Service", "Azure SQL", "Azure Service Bus", "Azure Key Vault", "Azure AD", "Docker", "Kubernetes",
            "Microservices", "CI/CD", "Unit testing", "Integration testing", "xUnit", "NUnit", "MSTest", "Git", "OAuth", "JWT", "Event-driven", "Angular", "React",
            "JavaScript", "TypeScript", "LINQ", "Dapper", "Design patterns", "SOLID", "Clean architecture", "API design", "Application Insights", "Cosmos DB", "Blazor",
            "WPF", "SignalR", "Agile", "Scrum", "Code review", "Domain-driven design", "System design", "Serverless", "Azure Logic Apps", "Azure Event Hubs",
        ],
        optional:
        [
            "Terraform", "Redis", "RabbitMQ", "GraphQL", "Kafka", "Helm", "Ansible", "Pulumi", "Python", "Java", "Go", "Rust", "Kotlin", "Swift", "PHP", "Ruby", "MongoDB",
            "Cassandra", "Elasticsearch", "DynamoDB", "Snowflake", "Spark", "Hadoop", "Airflow", "dbt", "Tableau", "Looker", "AWS", "GCP", "AWS Lambda", "CloudFormation",
        ],
        Cats((SkillCategory.Cloud, Importance.Important), (SkillCategory.AI, Importance.Optional), (SkillCategory.Tool, Importance.Optional)));

    public static readonly RoleProfile FullStack = new("fullstack", "Full Stack Developer",
        ["full stack", "full-stack", "fullstack", "mern", "mean stack"], ["React", "Angular", "JavaScript", "Node.js", "TypeScript"],
        critical: ["JavaScript", "TypeScript", "React", "Angular", "Vue", "HTML", "CSS", "REST", "SQL", "Node.js"],
        important:
        [
            "C#", ".NET", "ASP.NET Core", "Java", "Python", "Spring Boot", "Git", "Docker", "Azure", "AWS", "CI/CD", "Microservices", "GraphQL", "MongoDB", "PostgreSQL",
            "SQL Server", "Redux", "RxJS", "Next.js", "Unit testing", "Jest", "Cypress", "OAuth", "JWT", "Kubernetes", "Express", "API design", "Agile",
        ],
        optional: ["Terraform", "Kafka", "Spark", "Hadoop", "Ansible", "Pulumi", "Helm", "Airflow", "dbt", "Rust", "Go", "Scala", "Kotlin"],
        Cats((SkillCategory.Web, Importance.Important), (SkillCategory.DevOps, Importance.Normal), (SkillCategory.AI, Importance.Optional)));

    public static readonly RoleProfile AzureEngineer = new("azure", "Azure Engineer",
        ["azure"], ["Azure", "Azure DevOps", "Azure Functions", "ARM templates", "Bicep"],
        critical:
        [
            "Azure", "Azure DevOps", "Azure Functions", "Azure App Service", "Azure Key Vault", "Azure AD", "Azure SQL", "ARM templates", "Bicep", "Terraform",
            "Kubernetes", "Docker", "CI/CD", "Infrastructure as code", "PowerShell", "Azure Service Bus", "Azure Event Hubs", "Azure Logic Apps", "Azure Blob Storage",
        ],
        important:
        [
            "Microservices", "Application Insights", "Monitoring", "Observability", "Linux", "Git", "C#", ".NET", "Python", "Helm", "GitHub Actions", "Serverless",
            "Event-driven", "Azure Data Factory", "Cosmos DB", "SQL Server", "Cloud-native", "High availability", "Disaster recovery", "Secure coding",
        ],
        optional: ["Angular", "React", "Redis", "RabbitMQ", "GraphQL", "AWS", "GCP", "Spark", "Vue", "jQuery", "WPF"],
        Cats((SkillCategory.Cloud, Importance.Critical), (SkillCategory.DevOps, Importance.Important), (SkillCategory.Web, Importance.Optional)));

    public static readonly RoleProfile CloudEngineer = new("cloud", "Cloud / DevOps Engineer",
        ["cloud engineer", "cloud architect", "devops", "sre", "site reliability", "platform engineer", "infrastructure engineer"],
        ["Terraform", "Kubernetes", "Docker", "CI/CD", "Infrastructure as code"],
        critical:
        [
            "Terraform", "Kubernetes", "Docker", "CI/CD", "Infrastructure as code", "AWS", "Azure", "GCP", "Linux", "Ansible", "Helm", "Bash", "Monitoring",
            "Observability", "Prometheus", "Grafana", "GitHub Actions", "Jenkins", "GitLab CI", "Azure DevOps", "ArgoCD", "CloudFormation", "Bicep", "ARM templates", "Pulumi",
        ],
        important:
        [
            "Git", "PowerShell", "Python", "Datadog", "Splunk", "OpenTelemetry", "Nginx", "High availability", "Disaster recovery", "On-call", "Scalability",
            "Secure coding", "Go", "Microservices", "Application Insights", "Cloud-native", "Serverless", "Azure Functions", "AWS Lambda",
        ],
        optional: ["Angular", "React", "Vue", "CSS", "HTML", "jQuery", "Redux", "Bootstrap", "Tailwind", "WPF", "Blazor", "TypeScript", "JavaScript"],
        Cats((SkillCategory.Web, Importance.Optional), (SkillCategory.Framework, Importance.Optional), (SkillCategory.Cloud, Importance.Important), (SkillCategory.AI, Importance.Optional)));

    public static readonly RoleProfile Software = new("software", "Software Engineer",
        ["software engineer", "software developer", "developer", "programmer"], [],
        critical: [],
        important: [],
        optional: [],
        Cats((SkillCategory.Language, Importance.Important), (SkillCategory.Architecture, Importance.Important), (SkillCategory.Testing, Importance.Important),
            (SkillCategory.Practice, Importance.Important), (SkillCategory.Tool, Importance.Optional)));

    public static readonly RoleProfile AiEngineer = new("ai", "AI Engineer",
        [" ai ", "ai engineer", "ai/ml", "machine learning", "ml engineer", "llm", "generative", "artificial intelligence", "data scientist"],
        ["Machine learning", "LLM", "Generative AI", "PyTorch", "TensorFlow", "OpenAI", "RAG"],
        critical: ["Python", "Machine learning", "Artificial intelligence", "Generative AI", "LLM", "OpenAI", "RAG", "PyTorch", "TensorFlow", "NLP", "LangChain", "Prompt engineering"],
        important:
        [
            "Semantic Kernel", "Hugging Face", "Vector database", "MLOps", "pandas", "NumPy", "scikit-learn", "Docker", "Kubernetes", "Azure", "AWS", "GCP", "SQL", "REST",
            "FastAPI", "Git", "CI/CD", "Computer vision", "Databricks", "Spark", "Azure Functions",
        ],
        optional: ["Angular", "React", "Vue", "jQuery", "WPF", "Blazor", "CSS", "HTML"],
        Cats((SkillCategory.AI, Importance.Critical), (SkillCategory.Cloud, Importance.Important), (SkillCategory.Data, Importance.Important), (SkillCategory.Web, Importance.Optional)));

    public static readonly RoleProfile DataEngineer = new("data", "Data Engineer",
        ["data engineer", "etl", "big data", "analytics engineer", "data platform", "bi developer", "data"],
        ["ETL", "Spark", "Airflow", "Data pipelines", "Data warehouse", "Snowflake", "Databricks"],
        critical:
        [
            "SQL", "Python", "ETL", "Data pipelines", "Data warehouse", "Spark", "Airflow", "Snowflake", "Databricks", "Azure Data Factory", "Data modeling", "Data lake",
            "dbt", "Kafka", "Synapse", "BigQuery", "Redshift", "Delta Lake", "Scala",
        ],
        important:
        [
            "AWS", "Azure", "GCP", "Docker", "Git", "CI/CD", "Terraform", "PostgreSQL", "SQL Server", "MongoDB", "Hadoop", "Power BI", "Tableau", "Looker", "Kubernetes", "Linux",
            "Query optimization", "SSIS",
        ],
        optional: ["Angular", "React", "Vue", "jQuery", "WPF", "Blazor", "CSS", "HTML", "Redux"],
        Cats((SkillCategory.Data, Importance.Important), (SkillCategory.Web, Importance.Optional), (SkillCategory.Framework, Importance.Optional)));

    /// <summary>In the order used to break a tie: ".NET Developer with AI" is a .NET job that mentions AI, not an AI job.</summary>
    public static IReadOnlyList<RoleProfile> All { get; } = [DotNet, FullStack, AzureEngineer, CloudEngineer, AiEngineer, DataEngineer, Software];

    public static RoleProfile? ById(string? id) => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Picks the kind of job from its title (each matching word counts 3) and, when the title says nothing, from the skills the job description leans on
    /// (each counts 1, and at least 3 are needed). A plain "Software Engineer" stays a software engineer.
    /// </summary>
    public static RoleChoice Detect(string jobTitle, JdAnalysis jd)
    {
        var title = " " + jobTitle.ToLowerInvariant().Replace('/', ' ') + " ";
        var best = Software;
        var bestScore = 0;
        var reason = "No more specific kind of job was recognised from the title or the job description.";

        foreach (var profile in All.Where(p => p != Software))
        {
            var fromTitle = profile.TitleSignals.Count(s => title.Contains(s, StringComparison.Ordinal)) * 3;
            var fromBody = fromTitle > 0
                ? 0
                : Math.Min(4, profile.BodySignals.Count(s => jd.Terms.Any(t => string.Equals(t.Skill, s, StringComparison.OrdinalIgnoreCase))));
            if (fromTitle == 0 && fromBody < 3)
                fromBody = 0;

            var score = fromTitle + fromBody;
            if (score > bestScore)
            {
                (best, bestScore) = (profile, score);
                reason = fromTitle > 0
                    ? $"The job title mentions {profile.TitleSignals.First(s => title.Contains(s, StringComparison.Ordinal)).Trim()}."
                    : $"The job description leans on {string.Join(", ", profile.BodySignals.Where(s => jd.Terms.Any(t => string.Equals(t.Skill, s, StringComparison.OrdinalIgnoreCase))).Take(3))}.";
            }
        }

        return new RoleChoice(best, true, reason);
    }
}