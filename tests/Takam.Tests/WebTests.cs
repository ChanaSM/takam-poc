using Takam.Web;

namespace Takam.Tests;

/// <summary>The service behind the web endpoints. The endpoints themselves are one line each.</summary>
public static class WebTests
{
    private static async Task<SearchService> Service() => new((await Fixtures.IndexedStore()).Store);

    [Test]
    public static async Task Search_response_has_full_partial_and_site_results()
    {
        var r = (await Service()).Search("חילוט ערבות");
        Check.True(r.Results.Any(h => h.Code == "7.4.1") && r.Results.Any(h => h.Code == "7.3.3"), "both instructions in full results");
        Check.True(r.PartialResults.Count > 0, "has partial results");
        Check.Equal("7.3.3", string.Join(",", r.SiteStyleMatches), "site-style matches");
    }

    [Test]
    public static async Task Section_opens_with_source_link()
    {
        var service = await Service();
        var key = service.Search("352").Results[0].SectionKey;
        var section = service.Section(key);
        Check.True(section != null, "section found");
        Check.Contains("352", section!.Body, "body");
        Check.Contains("DownloadDocument", section.SourceUrl, "source link");
    }

    [Test]
    public static async Task Missing_section_is_null()
    {
        Check.True((await Service()).Section("9.9.9|1.1") is null, "no such section");
    }

    [Test]
    public static async Task Empty_query_returns_nothing()
    {
        Check.Equal(0, (await Service()).Search("   ").Results.Count, "result count");
    }
}
