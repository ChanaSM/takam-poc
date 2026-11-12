// Search screen: full-text search over section text, with the full section opening in place.
// The page is static HTML + a small script in wwwroot; this file only serves JSON.
using System.Text.Encodings.Web;
using Takam.Core;
using Takam.Web;

var builder = WebApplication.CreateBuilder(args);

// Resolved after the host is built, so tests can point it at their own data folder with
// the "Takam:DataDir" setting.
builder.Services.AddSingleton(sp =>
{
    var dataDir = sp.GetRequiredService<IConfiguration>()["Takam:DataDir"];
    var config = string.IsNullOrWhiteSpace(dataDir) ? new TakamConfig() : new TakamConfig { DataDir = dataDir };
    return new SearchService(new IndexStore(config.IndexPath));
});
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/search", (string? q, SearchService search) => search.Search(q ?? ""));

app.MapGet("/api/section", (string key, SearchService search) =>
    search.Section(key) is { } section ? Results.Ok(section) : Results.NotFound(new { error = "הסעיף לא נמצא" }));

app.MapGet("/api/status", (SearchService search) => search.Status());

app.MapGet("/api/documents", (SearchService search) => search.Documents());

app.Run();

public partial class Program { }
