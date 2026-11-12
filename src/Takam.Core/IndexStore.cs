using System.Text.Encodings.Web;
using System.Text.Json;

namespace Takam.Core;

/// <summary>
/// Keeps the indexed documents in one JSON file (data/index.json). The sample is a few hundred
/// sections, so the whole index fits comfortably in memory and in a readable file.
/// Every change is written to a temporary file first and then moved into place, so a crash
/// never leaves a half-written index.
/// </summary>
public sealed class IndexStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep Hebrew readable in the file
    };

    private readonly string _path;
    private readonly object _lock = new();
    private IndexData _data;
    private DateTime _loadedWriteTime;

    public IndexStore(string path)
    {
        _path = path;
        _data = Load(path, out _loadedWriteTime);
    }

    public string Path => _path;

    /// <summary>The current contents. Reloads first if another process (the CLI) changed the file.</summary>
    public IndexData Snapshot()
    {
        lock (_lock)
        {
            var writeTime = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
            if (writeTime != _loadedWriteTime)
                _data = Load(_path, out _loadedWriteTime);
            return _data;
        }
    }

    public IReadOnlyDictionary<string, DocMeta> IndexedDocuments() =>
        Snapshot().Documents.ToDictionary(d => d.Meta.Code, d => d.Meta);

    /// <summary>Puts a document's new sections in place of its old ones, in one write.</summary>
    public void ReplaceDocument(DocMeta meta, List<Section> sections)
    {
        Mutate(data =>
        {
            data.Documents.RemoveAll(d => d.Meta.Code == meta.Code);
            data.Documents.Add(new StoredDocument(meta, sections, DateTime.Now.ToString("s")));
        });
    }

    public void DeleteDocument(string code) => Mutate(data => data.Documents.RemoveAll(d => d.Meta.Code == code));

    public void LogSync(SyncCounts counts, IEnumerable<string> details) =>
        Mutate(data => data.SyncLog.Add(new SyncLogEntry(
            DateTime.Now.ToString("s"), counts.Added, counts.Updated, counts.Removed,
            counts.Unchanged, counts.Failed, string.Join("\n", details))));

    public SyncLogEntry? LastSync() => Snapshot().SyncLog.LastOrDefault();

    public IndexStats Stats()
    {
        var docs = Snapshot().Documents;
        return new IndexStats(
            docs.Count,
            docs.Count(d => d.Meta.DocType == "Instruction"),
            docs.Count(d => d.Meta.DocType == "Message"),
            docs.Sum(d => d.Sections.Count));
    }

    public SectionView? GetSection(string key)
    {
        var parts = key.Split('|', 2);
        if (parts.Length != 2)
            return null;
        var doc = Snapshot().Documents.FirstOrDefault(d => d.Meta.Code == parts[0]);
        var s = doc?.Sections.FirstOrDefault(x => x.Number == parts[1]);
        if (doc is null || s is null)
            return null;
        var m = doc.Meta;
        return new SectionView(key, m.Code, s.Number, s.Title, s.Body, m.Name, m.DocType, m.Version, m.ValidFrom, m.SourceUrl, m.ParentCode);
    }

    private void Mutate(Action<IndexData> change)
    {
        lock (_lock)
        {
            var data = Snapshot();
            change(data);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
            _loadedWriteTime = File.GetLastWriteTimeUtc(_path);
        }
    }

    private static IndexData Load(string path, out DateTime writeTime)
    {
        if (!File.Exists(path))
        {
            writeTime = DateTime.MinValue;
            return new IndexData();
        }
        writeTime = File.GetLastWriteTimeUtc(path);
        return JsonSerializer.Deserialize<IndexData>(File.ReadAllText(path), JsonOptions) ?? new IndexData();
    }
}
