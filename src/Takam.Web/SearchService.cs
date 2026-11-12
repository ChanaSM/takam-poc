using Takam.Core;

namespace Takam.Web;

public sealed record SearchResponse(
    string Query, int DocumentCount, List<SearchHit> Results, List<SearchHit> PartialResults, List<string> SiteStyleMatches);

public sealed record StatusResponse(IndexStats Stats, string? LastSync);

/// <summary>
/// What the web endpoints call. Rebuilds the in-memory search engine only when the index file
/// changed (for example after `update` ran in another window).
/// </summary>
public sealed class SearchService(IndexStore store)
{
    private readonly object _lock = new();
    private IndexData? _builtFrom;
    private SearchEngine? _engine;

    public SearchResponse Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
            return new SearchResponse("", 0, [], [], []);
        var engine = Engine();
        var hits = engine.Search(query);
        var partial = engine.SearchPartial(query, hits.Select(h => h.SectionKey).ToHashSet());
        return new SearchResponse(query, hits.Select(h => h.Code).Distinct().Count(), hits, partial, engine.SiteStyleSearch(query));
    }

    public SectionView? Section(string key) => store.GetSection(key);

    public StatusResponse Status() => new(store.Stats(), store.LastSync()?.RunAt[..10]);

    public IEnumerable<object> Documents() => store.Snapshot().Documents
        .OrderByDescending(d => d.Meta.DocType).ThenBy(d => d.Meta.Code)
        .Select(d => new { d.Meta.Code, d.Meta.Name, d.Meta.DocType, d.Meta.Version, d.Meta.ValidFrom, d.Meta.ParentCode, d.Meta.Forms, Sections = d.Sections.Count });

    private SearchEngine Engine()
    {
        lock (_lock)
        {
            var data = store.Snapshot();
            if (!ReferenceEquals(data, _builtFrom))
            {
                _engine = new SearchEngine(data);
                _builtFrom = data;
            }
            return _engine!;
        }
    }
}
