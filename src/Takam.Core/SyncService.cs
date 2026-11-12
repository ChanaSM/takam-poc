namespace Takam.Core;

/// <summary>
/// Brings the index in line with the site: adds new documents, re-indexes new editions, removes
/// documents that left the sample or expired, and leaves everything else alone.
/// A full rebuild is the same routine on an empty index, so there is one code path.
/// </summary>
public static class SyncService
{
    public delegate Task<byte[]> Fetch(DocMeta meta, CancellationToken ct);

    public static async Task<SyncCounts> SyncAsync(
        IndexStore store, IReadOnlyList<DocMeta> plan, Fetch fetch, string rawDir,
        Action<string>? log = null, CancellationToken ct = default)
    {
        log ??= Console.WriteLine;
        Directory.CreateDirectory(rawDir);
        var existing = store.IndexedDocuments();
        var wanted = plan.ToDictionary(m => m.Code);
        var counts = new SyncCounts();
        var details = new List<string>();

        void Report(string message)
        {
            details.Add(message);
            log(message);
        }

        foreach (var (code, meta) in wanted)
        {
            existing.TryGetValue(code, out var old);
            if (old != null && old.Version == meta.Version && old.ContentVersionId == meta.ContentVersionId)
            {
                counts.Unchanged++;
                continue;
            }

            var path = LocalPath(meta, rawDir);
            List<Section> sections;
            try
            {
                if (!File.Exists(path))
                {
                    var bytes = await fetch(meta, ct);
                    if (bytes.Length < 2 || bytes[0] != (byte)'P' || bytes[1] != (byte)'K')
                        throw new InvalidDataException("הקובץ שהתקבל אינו קובץ Word תקין");
                    await File.WriteAllBytesAsync(path, bytes, ct);
                }
                sections = DocxParser.Parse(path);
                if (sections.Count == 0)
                    throw new InvalidDataException("לא חולצו סעיפים מהקובץ");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Keep the previous text indexed; record the failure and move on.
                counts.Failed++;
                Report($"נכשל {code}: {ex.Message}" + (old != null ? " (הנוסח הקודם נשאר באינדקס)" : ""));
                continue;
            }

            store.ReplaceDocument(meta, sections);
            if (old is null)
            {
                counts.Added++;
                Report($"נוסף {code} {meta.Name} (מהדורה {meta.Version}), {sections.Count} סעיפים");
            }
            else
            {
                counts.Updated++;
                Report($"עודכן {code} {meta.Name} (מהדורה {old.Version} ← {meta.Version}), {sections.Count} סעיפים");
            }
        }

        foreach (var (code, old) in existing)
        {
            if (wanted.ContainsKey(code))
                continue;
            store.DeleteDocument(code);
            counts.Removed++;
            Report($"הוסר {code} {old.Name} (פג תוקף או יצא מהמדגם)");
        }

        store.LogSync(counts, details);
        return counts;
    }

    /// <summary>
    /// The content version id changes with every new file, so a new edition never reuses an old
    /// download, and re-running costs nothing.
    /// </summary>
    public static string LocalPath(DocMeta meta, string rawDir) =>
        Path.Combine(rawDir, $"{meta.CodeText}.{meta.ContentVersionId}.docx");

    public static async Task<SyncCounts> SyncFromSiteAsync(TakamConfig config, IndexStore store, Action<string>? log = null, CancellationToken ct = default)
    {
        log ??= Console.WriteLine;
        using var api = new TakamSiteApi(config);
        var (plan, warnings) = await Catalog.BuildPlanAsync(api, config, ct);
        foreach (var w in warnings)
            log("אזהרה: " + w);
        return await SyncAsync(store, plan, api.DownloadAsync, config.RawDir, log, ct);
    }
}
