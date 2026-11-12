using Takam.Core;

namespace Takam.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute;

public sealed class AssertionException(string message) : Exception(message);

/// <summary>The few assertions the tests need.</summary>
public static class Check
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new AssertionException(message);
    }

    public static void Equal<T>(T expected, T actual, string what = "value")
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"{what}: expected '{expected}', got '{actual}'");
    }

    public static void Contains(string needle, string haystack, string what = "text")
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
            throw new AssertionException($"{what} should contain '{needle}' but was: {Short(haystack)}");
    }

    public static void NotContains(string needle, string haystack, string what = "text")
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
            throw new AssertionException($"{what} should not contain '{needle}': {Short(haystack)}");
    }

    private static string Short(string s) => s.Length <= 200 ? s : s[..200] + "…";
}

/// <summary>Real files from the site, and an index built from them with no network.</summary>
public static class Fixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string File(string codeText) => Path.Combine(Dir, codeText + ".docx");

    public static DocMeta Meta(string code, int version, string name, string keywords = "", string parent = "", string? cv = null)
    {
        var isMessage = code.StartsWith("HOD.", StringComparison.Ordinal);
        return new DocMeta
        {
            Code = code,
            CodeText = isMessage ? code : "H." + code,
            DocType = isMessage ? "Message" : "Instruction",
            Name = name,
            Version = version,
            ContentVersionId = cv ?? $"cv-{code}-{version}",
            Keywords = keywords,
            ParentCode = parent,
            SourceUrl = $"https://takam.mof.gov.il/api/DocumentSF/DownloadDocument/cv/{code}",
        };
    }

    public static readonly IReadOnlyList<DocMeta> Sample =
    [
        Meta("8.1.2", 9, "הרצאות, שירותי הדרכה, מבחנים ובחינות"),
        Meta("HOD.8.1.2.1", 11, "תעריפי הדרכה, מבחנים ובחינות", parent: "8.1.2"),
        Meta("7.4.1", 8, "ניהול ההליך המכרזי", "פרסום מכרז פומבי, ביטול מכרז"),
        Meta("2.6.1", 1, "תשלום שכר מרצים"),
        Meta("15.4.3", 6, "רכש שירותי הסעדה"),
        // Real keywords from the site: this is why the site's own search finds 7.3.3.
        Meta("7.3.3", 18, "ערבויות וביטחונות", "ערבות מכרז, ערבות ביצוע, חילוט ערבות, הוראת קיזוז"),
    ];

    public static Task<byte[]> Fetch(DocMeta meta, CancellationToken ct) => System.IO.File.ReadAllBytesAsync(File(meta.CodeText), ct);

    public static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "takam-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A fresh index in a temp folder, built from the fixture files.</summary>
    public static async Task<(IndexStore Store, string Dir)> IndexedStore()
    {
        var dir = NewTempDir();
        var store = new IndexStore(Path.Combine(dir, "index.json"));
        await SyncService.SyncAsync(store, Sample, Fetch, Path.Combine(dir, "raw"), _ => { });
        return (store, dir);
    }

    public static async Task<SearchEngine> Engine() => new((await IndexedStore()).Store.Snapshot());
}
