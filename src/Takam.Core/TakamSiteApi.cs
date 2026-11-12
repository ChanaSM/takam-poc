using System.Text.Json.Nodes;

namespace Takam.Core;

/// <summary>Thin client for the public JSON API behind takam.mof.gov.il. All calls are unauthenticated GETs.</summary>
public sealed class TakamSiteApi : IDisposable
{
    private readonly TakamConfig _config;
    private readonly HttpClient _http;

    public TakamSiteApi(TakamConfig config, HttpClient? http = null)
    {
        _config = config;
        _http = http ?? new HttpClient { Timeout = config.HttpTimeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(config.UserAgent);
    }

    /// <summary>Every document on the site (instructions, messages, forms, tenders): about 1,100 rows.</summary>
    public Task<JsonArray> ListDocumentsAsync(CancellationToken ct = default) => GetArrayAsync("/api/Document", ct);

    /// <summary>All editions of one document.</summary>
    public Task<JsonArray> ListVersionsAsync(string sfid, CancellationToken ct = default) =>
        GetArrayAsync($"/api/DocumentVersion/{sfid}", ct);

    /// <summary>Messages and forms linked to an instruction.</summary>
    public Task<JsonArray> ListRelatedAsync(string sfid, CancellationToken ct = default) =>
        GetArrayAsync($"/api/DocumentVersion/RelatedDocumentId/{sfid}", ct);

    public string DownloadUrl(string contentVersionId, string codeText) =>
        $"{_config.BaseUrl}/api/DocumentSF/DownloadDocument/{contentVersionId}/{codeText}";

    /// <summary>Downloads one file. The server labels it application/octetstream; it is a .docx (ZIP).</summary>
    public async Task<byte[]> DownloadAsync(DocMeta meta, CancellationToken ct = default)
    {
        await Task.Delay(_config.DownloadDelay, ct);
        return await _http.GetByteArrayAsync(DownloadUrl(meta.ContentVersionId, meta.CodeText), ct);
    }

    private async Task<JsonArray> GetArrayAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(_config.BaseUrl + path, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonNode.Parse(json) as JsonArray ?? new JsonArray();
    }

    public void Dispose() => _http.Dispose();
}
