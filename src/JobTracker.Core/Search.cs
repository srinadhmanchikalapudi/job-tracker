namespace JobTracker.Core;

/// <summary>
/// Search seam: v1 is plain keyword matching. A semantic (embedding) provider can be
/// swapped in later without touching the UI.
/// </summary>
public interface ISearchProvider
{
    IReadOnlyList<SearchHit> Search(IEnumerable<JobApplication> applications, string query, SearchScope scope);
}

/// <summary>
/// Case-insensitive keyword search. An application matches when every term appears somewhere in the
/// scoped text (JD, Resume, or either). Hits are the lines containing at least one term.
/// </summary>
public sealed class KeywordSearchProvider(ApplicationStore store) : ISearchProvider
{
    public IReadOnlyList<SearchHit> Search(IEnumerable<JobApplication> applications, string query, SearchScope scope)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0)
            return [];

        var hits = new List<SearchHit>();
        foreach (var app in applications)
        {
            var sources = new List<(BulletSource Source, string Text)>();
            if (scope is SearchScope.Both or SearchScope.Jd)
                sources.Add((BulletSource.Jd, store.ReadJd(app)));
            if (scope is SearchScope.Both or SearchScope.Resume)
                sources.Add((BulletSource.Resume, store.ReadResume(app)));

            var allText = string.Join('\n', sources.Select(s => s.Text));
            if (!terms.All(t => allText.Contains(t, StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (var (source, text) in sources)
            foreach (var (lineNumber, line) in BulletText.Lines(text))
            {
                if (terms.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase)))
                    hits.Add(new SearchHit(app, source, lineNumber, line));
            }
        }

        return hits;
    }
}
