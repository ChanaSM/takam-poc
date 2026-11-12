using Takam.Core;

namespace Takam.Tests;

public static class ParserTests
{
    private static Dictionary<string, Section> ByNumber(string codeText) =>
        DocxParser.Parse(Fixtures.File(codeText)).ToDictionary(s => s.Number);

    [Test]
    public static void Clauses_are_numbered_like_the_source()
    {
        var s = ByNumber("H.8.1.2");
        Check.Equal("הרצאה והדרכה מקצועית", s["2.1"].Title, "2.1 title");
        Check.Equal("שעות הכנה", s["2.4"].Title, "2.4 title");
        Check.True(s["2.8"].Title.StartsWith("מרצה/מדריך שהוא עובד מדינה"), "2.8 is about a lecturer who is a state employee");
        Check.Contains("25%", s["2.8"].Body, "2.8 body");
    }

    [Test]
    public static void Tender_numbering_matches_its_table_of_contents()
    {
        var s = ByNumber("H.7.4.1");
        Check.Equal("חילוט ערבות מכרז", s["4.14"].Title, "4.14 title");
        Check.Equal("ביטול מכרז", s["4.15"].Title, "4.15 title");
        Check.True(!s.ContainsKey("4.16"), "there is no 4.16");
    }

    [Test]
    public static void Table_of_contents_and_change_log_are_skipped()
    {
        var sections = DocxParser.Parse(Fixtures.File("H.7.4.1"));
        Check.Equal("1.1", sections[0].Number, "first section");
        Check.NotContains("........", string.Join("\n", sections.Select(x => x.Title + x.Body)), "all text");
        Check.True(!sections.Any(x => x.Number.StartsWith("נספח") && x.Title.Contains("טבלת שינויים")), "no change-log appendix");
    }

    [Test]
    public static void No_word_field_codes_leak_into_text()
    {
        foreach (var name in new[] { "H.8.1.2", "H.7.4.1", "H.15.4.3", "H.2.6.1", "H.7.3.3", "HOD.8.1.2.1" })
            foreach (var s in DocxParser.Parse(Fixtures.File(name)))
                foreach (var code in new[] { "REF _Ref", "PAGEREF", "MERGEFORMAT" })
                    Check.NotContains(code, s.Body, $"{name} §{s.Number}");
    }

    [Test]
    public static void Message_tables_become_their_own_sections()
    {
        var s = ByNumber("HOD.8.1.2.1");
        Check.Equal("פתיחה,טבלה 1,טבלה 2,טבלה 3", string.Join(",", s.Keys), "sections");
        Check.Contains("הרצאות", s["טבלה 1"].Title, "table 1 title");
        Check.Contains("352", s["טבלה 1"].Body, "table 1 body");
        Check.Contains("קבוצה 1", s["טבלה 1"].Body, "table 1 body");
        Check.Contains(" | ", s["טבלה 1"].Body, "table 1 body");
    }

    [Test]
    public static void Old_2009_instruction_still_parses()
    {
        var s = ByNumber("H.2.6.1");
        Check.True(s.ContainsKey("2.4"), "has 2.4");
        Check.True(s.Values.Any(x => x.Title.Contains("עוסק מורשה")), "mentions a licensed dealer");
    }
}
