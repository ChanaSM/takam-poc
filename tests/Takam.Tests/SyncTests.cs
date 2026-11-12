using Takam.Core;

namespace Takam.Tests;

public static class SyncTests
{
    private static Task<SyncCounts> Run(IndexStore store, string dir, IEnumerable<DocMeta> plan, SyncService.Fetch? fetch = null) =>
        SyncService.SyncAsync(store, plan.ToList(), fetch ?? Fixtures.Fetch, Path.Combine(dir, "raw"), _ => { });

    private static bool FindsForfeitureClause(IndexStore store) =>
        new SearchEngine(store.Snapshot()).Search("חילוט ערבות").Any(h => h.Code == "7.4.1" && h.Number == "4.14");

    [Test]
    public static async Task Second_run_without_changes_does_nothing()
    {
        var (store, dir) = await Fixtures.IndexedStore();
        var downloads = 0;
        var counts = await Run(store, dir, Fixtures.Sample, (m, ct) => { downloads++; return Fixtures.Fetch(m, ct); });
        Check.Equal(Fixtures.Sample.Count, counts.Unchanged, "unchanged");
        Check.Equal(0, counts.Added + counts.Updated + counts.Removed + counts.Failed, "changes");
        Check.Equal(0, downloads, "downloads");
    }

    [Test]
    public static async Task New_edition_replaces_the_old_text()
    {
        var (store, dir) = await Fixtures.IndexedStore();
        var plan = Fixtures.Sample.Select(m => m.Code == "8.1.2" ? m with { Version = 10, ContentVersionId = "cv-new" } : m);
        var counts = await Run(store, dir, plan);
        Check.Equal(1, counts.Updated, "updated");
        Check.Equal(10, store.IndexedDocuments()["8.1.2"].Version, "version");
        var doc = store.Snapshot().Documents.Single(d => d.Meta.Code == "8.1.2");
        Check.Equal(doc.Sections.Count, doc.Sections.Select(s => s.Number).Distinct().Count(), "no duplicated sections");
    }

    [Test]
    public static async Task New_document_is_added_and_searchable()
    {
        var dir = Fixtures.NewTempDir();
        var store = new IndexStore(Path.Combine(dir, "index.json"));
        await Run(store, dir, Fixtures.Sample.Where(m => m.Code != "7.4.1"));
        Check.True(!FindsForfeitureClause(store), "not found before 7.4.1 is added");
        var counts = await Run(store, dir, Fixtures.Sample);
        Check.Equal(1, counts.Added, "added");
        Check.True(FindsForfeitureClause(store), "found after 7.4.1 is added");
    }

    [Test]
    public static async Task Document_that_leaves_the_plan_is_removed()
    {
        var (store, dir) = await Fixtures.IndexedStore();
        var counts = await Run(store, dir, Fixtures.Sample.Where(m => m.Code != "7.4.1"));
        Check.Equal(1, counts.Removed, "removed");
        Check.True(!FindsForfeitureClause(store), "7.4.1 is no longer searchable");
    }

    [Test]
    public static async Task Failed_download_keeps_previous_text()
    {
        var (store, dir) = await Fixtures.IndexedStore();
        var plan = Fixtures.Sample.Select(m => m.Code == "7.4.1" ? m with { Version = 99, ContentVersionId = "cv-broken" } : m);
        var counts = await Run(store, dir, plan, (m, ct) =>
            m.Code == "7.4.1" ? Task.FromResult("<html>error</html>"u8.ToArray()) : Fixtures.Fetch(m, ct));
        Check.Equal(1, counts.Failed, "failed");
        Check.Equal(8, store.IndexedDocuments()["7.4.1"].Version, "old edition kept");
        Check.True(FindsForfeitureClause(store), "old text still searchable");
        Check.Contains("7.4.1", store.LastSync()!.Details, "sync log");
    }

    [Test]
    public static async Task Index_survives_a_reload_from_disk()
    {
        var (store, _) = await Fixtures.IndexedStore();
        var reloaded = new IndexStore(store.Path);
        Check.Equal(store.Stats(), reloaded.Stats(), "stats after reload");
        Check.True(FindsForfeitureClause(reloaded), "search works after reload");
    }
}
