using Takam.Core;

namespace Takam.Tests;

public static class SearchTests
{
    private static HashSet<(string, string)> Found(IEnumerable<SearchHit> hits) => hits.Select(h => (h.Code, h.Number)).ToHashSet();

    [Test]
    public static async Task Finds_text_the_site_search_misses()
    {
        var engine = await Fixtures.Engine();
        var found = Found(engine.Search("חילוט ערבות"));
        Check.True(found.Contains(("7.4.1", "4.14")), "7.4.1 §4.14 is found (body text only, no keywords mention it)");
        Check.True(found.Contains(("7.3.3", "2.5")), "7.3.3 §2.5 is found");
        Check.Equal("7.3.3", string.Join(",", engine.SiteStyleSearch("חילוט ערבות")), "site-style search");
    }

    [Test]
    public static async Task Snippet_highlights_matched_words()
    {
        var hit = (await Fixtures.Engine()).Search("חילוט ערבות")[0];
        Check.Contains("<mark>", hit.SnippetHtml, "snippet");
    }

    [Test]
    public static async Task Tariff_tables_are_searchable()
    {
        var hits = (await Fixtures.Engine()).Search("352");
        Check.True(hits.Count > 0, "has results");
        Check.Equal("HOD.8.1.2.1", hits[0].Code, "first result");
    }

    [Test]
    public static async Task Plural_and_prefix_forms_match()
    {
        var found = Found((await Fixtures.Engine()).Search("המרצים"));
        Check.True(found.Contains(("8.1.2", "2.8")), "'המרצים' finds 8.1.2 §2.8, which says 'מרצה'");
    }

    [Test]
    public static async Task Stem_matches_only_known_endings()
    {
        var codes = (await Fixtures.Engine()).Search("מעונות").Select(h => h.Code).ToHashSet();
        Check.True(codes.Contains("HOD.8.1.2.1"), "finds 'ומעונות' in the tariff message (prefix letter in the text)");
        Check.True(!codes.Contains("7.3.3"), "does not match 'מעוניין' in 7.3.3");
    }

    [Test]
    public static void Final_letters_match_regular_letters()
    {
        var meta = Fixtures.Meta("9.9.9", 1, "בדיקה");
        var data = new IndexData { Documents = [new StoredDocument(meta, [new Section("1.1", "x", "הקמת מעון יום", 0)], "")] };
        Check.Equal(1, new SearchEngine(data).Search("מעונות").Count, "'מעונות' finds 'מעון'");
    }

    [Test]
    public static void Filler_words_are_dropped_from_the_query()
    {
        Check.Equal("כיבוד", string.Join(",", SearchEngine.QueryWords("אני רוצה כיבוד")), "content words");
        Check.Equal("מה,זה", string.Join(",", SearchEngine.QueryWords("מה זה")), "a query of only filler keeps its words");
    }

    [Test]
    public static async Task Natural_question_finds_the_section_despite_filler_words()
    {
        var found = Found((await Fixtures.Engine()).Search("אני רוצה כיבוד לישיבה"));
        Check.True(found.Contains(("15.4.3", "2.8")), "15.4.3 §2.8 is a full match once 'אני רוצה' is ignored");
    }

    [Test]
    public static async Task Multi_word_query_ranks_the_right_clause_first()
    {
        var hit = (await Fixtures.Engine()).Search("מרצה עובד מדינה בשעות העבודה")[0];
        Check.Equal(("8.1.2", "2.8"), (hit.Code, hit.Number), "first result");
    }

    [Test]
    public static async Task Unknown_term_returns_empty_list()
    {
        Check.Equal(0, (await Fixtures.Engine()).Search("קוואנטים").Count, "result count");
    }

    [Test]
    public static async Task Special_characters_do_not_break_search()
    {
        var engine = await Fixtures.Engine();
        foreach (var q in new[] { "\"", "*", "(", "תכ\"ם", "AND OR NOT", "a", "", "<script>" })
            engine.Search(q);
    }

    [Test]
    public static void Snippet_is_html_escaped()
    {
        var meta = Fixtures.Meta("9.9.9", 1, "בדיקה");
        var data = new IndexData { Documents = [new StoredDocument(meta, [new Section("1.1", "x", "<script>בדיקתהזרקה</script>", 0)], "")] };
        var hit = new SearchEngine(data).Search("בדיקתהזרקה")[0];
        Check.NotContains("<script>", hit.SnippetHtml, "snippet");
        Check.Contains("&lt;script&gt;", hit.SnippetHtml, "snippet");
    }

    [Test]
    public static async Task Partial_results_list_sections_with_some_of_the_words()
    {
        var engine = await Fixtures.Engine();
        var full = engine.Search("חילוט ערבות");
        var fullKeys = full.Select(h => h.SectionKey).ToHashSet();
        var partial = engine.SearchPartial("חילוט ערבות", fullKeys, limit: 50);
        Check.True(partial.Count > 0, "has partial results");
        Check.True(!partial.Any(h => fullKeys.Contains(h.SectionKey)), "no overlap with full results");
        Check.True(Found(partial).Contains(("7.4.1", "4.13")), "7.4.1 §4.13 mentions only 'ערבות'");
    }

    [Test]
    public static async Task Partial_results_show_several_documents()
    {
        var engine = await Fixtures.Engine();
        var full = engine.Search("חילוט ערבות").Select(h => h.SectionKey).ToHashSet();
        var codes = engine.SearchPartial("חילוט ערבות", full).Select(h => h.Code).ToList();
        Check.True(codes.GroupBy(c => c).All(g => g.Count() <= 3), "at most 3 per document");
        Check.True(codes.Contains("7.4.1"), "7.4.1 appears");
    }

    [Test]
    public static async Task Partial_results_need_more_than_one_word()
    {
        Check.Equal(0, (await Fixtures.Engine()).SearchPartial("ערבות", new HashSet<string>()).Count, "result count");
    }
}
