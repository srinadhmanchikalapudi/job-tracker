namespace JobTracker.Core;

/// <summary>The global library of starred bullets, persisted to &lt;root&gt;\.jobtracker\bullets.json.</summary>
public sealed class BulletStore(string root)
{
    private readonly string _path = Path.Combine(root, ApplicationStore.AppDataFolder, "bullets.json");

    public IReadOnlyList<StarredBullet> List() =>
        (AtomicFile.ReadJson<List<StarredBullet>>(_path) ?? [])
            .OrderByDescending(b => b.StarredAt)
            .ToList();

    public bool IsStarred(string appFolder, BulletSource source, string line)
    {
        var key = BulletText.Key(line);
        return List().Any(b => Matches(b, appFolder, source, key));
    }

    /// <summary>Stars the line if it isn't starred, otherwise unstars it. Returns true when it is now starred.</summary>
    public bool Toggle(string appFolder, string company, string role, BulletSource source, string line)
    {
        var all = (AtomicFile.ReadJson<List<StarredBullet>>(_path) ?? []);
        var key = BulletText.Key(line);

        var removed = all.RemoveAll(b => Matches(b, appFolder, source, key));
        if (removed == 0)
        {
            all.Add(new StarredBullet
            {
                Text = BulletText.Clean(line),
                Source = source,
                AppFolder = appFolder,
                Company = company,
                Role = role,
            });
        }

        AtomicFile.WriteJson(_path, all);
        return removed == 0;
    }

    public void Remove(Guid id)
    {
        var all = AtomicFile.ReadJson<List<StarredBullet>>(_path) ?? [];
        if (all.RemoveAll(b => b.Id == id) > 0)
            AtomicFile.WriteJson(_path, all);
    }

    /// <summary>Filters the library by keyword (all terms must appear in the bullet or its company/role) and optional source.</summary>
    public IReadOnlyList<StarredBullet> Search(string query, BulletSource? source = null)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return List()
            .Where(b => source is null || b.Source == source)
            .Where(b => terms.All(t =>
                b.Text.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                b.Company.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                b.Role.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static bool Matches(StarredBullet b, string appFolder, BulletSource source, string key) =>
        b.Source == source &&
        string.Equals(b.AppFolder, appFolder, StringComparison.OrdinalIgnoreCase) &&
        BulletText.Key(b.Text) == key;
}
