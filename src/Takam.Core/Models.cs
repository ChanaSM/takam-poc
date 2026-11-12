namespace Takam.Core;

public sealed record FormLink(string Name, string Url);

/// <summary>One document that should be in the index: an instruction or a linked message.</summary>
public sealed record DocMeta
{
    /// <summary>'8.1.2' for an instruction, 'HOD.8.1.2.1' for a message.</summary>
    public required string Code { get; init; }
    /// <summary>The site's download code: 'H.8.1.2' or 'HOD.8.1.2.1'.</summary>
    public required string CodeText { get; init; }
    /// <summary>'Instruction' or 'Message'.</summary>
    public required string DocType { get; init; }
    public required string Name { get; init; }
    public string Sfid { get; init; } = "";
    public string Chapter { get; init; } = "";
    public string Subchapter { get; init; } = "";
    public int Version { get; init; }
    public string ValidFrom { get; init; } = "";
    public string ContentVersionId { get; init; } = "";
    public string ParentCode { get; init; } = "";
    public string Keywords { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public List<FormLink> Forms { get; init; } = [];
}

/// <summary>A numbered section of a document: '4.14', 'מבוא', 'נספח א', 'טבלה 1'.</summary>
public sealed record Section(string Number, string Title, string Body, int Position);

/// <summary>One search result. SectionKey is "code|number" and opens the full section.</summary>
public sealed record SearchHit(
    string SectionKey, string Code, string DocName, string DocType,
    string Number, string Title, string SnippetHtml, double Score);

public sealed record SectionView(
    string Key, string Code, string Number, string Title, string Body,
    string DocName, string DocType, int Version, string ValidFrom, string SourceUrl, string ParentCode);

/// <summary>A document as stored in the index file: its metadata and its sections.</summary>
public sealed record StoredDocument(DocMeta Meta, List<Section> Sections, string IndexedAt);

/// <summary>Everything that is persisted: the indexed documents and the history of update runs.</summary>
public sealed class IndexData
{
    public List<StoredDocument> Documents { get; set; } = [];
    public List<SyncLogEntry> SyncLog { get; set; } = [];
}

public sealed record SyncLogEntry(string RunAt, int Added, int Updated, int Removed, int Unchanged, int Failed, string Details);

public sealed record IndexStats(int Documents, int Instructions, int Messages, int Sections);

public sealed class SyncCounts
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Removed { get; set; }
    public int Unchanged { get; set; }
    public int Failed { get; set; }

    public override string ToString() =>
        $"נוספו {Added}, עודכנו {Updated}, הוסרו {Removed}, ללא שינוי {Unchanged}, נכשלו {Failed}";
}
