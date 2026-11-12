using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Takam.Core;

/// <summary>
/// Turns a Takam .docx into numbered sections, using only the built-in ZIP and XML libraries.
/// A .docx is a ZIP file; the text is in word/document.xml and the style definitions in word/styles.xml.
///
/// How Takam files are built (checked on instructions from 2009 to 2026):
/// - Style *names* are consistent across files; style *ids* are not, so we match on names.
/// - Section numbers are Word auto-numbering, not text. We rebuild them: a level-1 heading starts
///   chapter N, and each "סעיף רמה 2" paragraph that uses its style's own numbering is clause N.k.
///   A paragraph that overrides numbering locally (numId 0, or another list) is body text of the
///   clause above it.
/// - Everything before the first level-1 heading is the table of contents and is skipped.
/// - Tables are kept in reading order; each row becomes one line with cells joined by " | ".
/// - Messages (tariff notices) have no headings: the opening text is one section and each table is
///   its own section, titled by the paragraph just above it.
/// - Appendices titled "טבלת שינויים" (the edition change log) are skipped.
/// </summary>
public static partial class DocxParser
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly HashSet<string> Heading1Styles = ["כותרת רמה 1", "כותרת 1", "heading 1", "Heading 1"];
    private static readonly HashSet<string> ClauseStyles = ["סעיף רמה 2", "סעיף רמה 22"];
    private const string AppendixTitleStyle = "נספח - כותרת";
    private const string AppendixDescStyle = "נספח - תיאור";
    private const string ChangeLogWords = "טבלת שינויים";
    private const int TitleMaxLength = 80;

    [GeneratedRegex(@"\b(?:REF|PAGEREF|TOC|HYPERLINK|MERGEFORMAT)\b[^֐-׿\d]*?(?:\\[a-z*]\s*\S*\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex FieldCode();

    [GeneratedRegex(@"[ \t ‎‏]+")]
    private static partial Regex Spaces();

    public static List<Section> Parse(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var document = LoadXml(zip, "word/document.xml") ?? throw new InvalidDataException("word/document.xml חסר בקובץ");
        var styles = StyleTable.Load(LoadXml(zip, "word/styles.xml"));
        var body = document.Root?.Element(W + "body") ?? throw new InvalidDataException("לא נמצא גוף מסמך");

        // Only direct children, like Word's reading order. Content controls (w:sdt) hold the table of contents.
        var blocks = body.Elements().Where(e => e.Name == W + "p" || e.Name == W + "tbl").ToList();
        var hasHeadings = blocks.Any(b => b.Name == W + "p" && Heading1Styles.Contains(styles.NameOf(b)));
        return hasHeadings ? ParseInstruction(blocks, styles) : ParseMessage(blocks, styles);
    }

    public static string Clean(string text)
    {
        text = FieldCode().Replace(text, " ");
        text = Spaces().Replace(text, " ");
        return text.Trim();
    }

    private static List<Section> ParseInstruction(List<XElement> blocks, StyleTable styles)
    {
        var sections = new List<Section>();
        int chapter = 0, clause = 0;
        var chapterTitle = "";
        SectionBuilder? current = null;
        var inToc = true;
        var skipping = false;

        void Flush()
        {
            if (current is { } c && (c.Lines.Count > 0 || c.Title.Length > 0))
            {
                // A one-line clause is its own body, so it shows up in search snippets.
                var bodyText = string.Join("\n", c.Lines).Trim();
                sections.Add(new Section(c.Number, c.Title, bodyText.Length > 0 ? bodyText : c.Title, sections.Count));
            }
            current = null;
        }

        void Start(string number, string title, string? firstLine = null)
        {
            Flush();
            current = new SectionBuilder(number, title);
            if (firstLine != null)
                current.Lines.Add(firstLine);
        }

        foreach (var block in blocks)
        {
            if (block.Name == W + "tbl")
            {
                if (inToc || skipping)
                    continue;
                var tableText = TableText(block);
                if (tableText.Length == 0)
                    continue;
                if (current is null)
                    Start(chapter.ToString(), chapterTitle);
                current!.Lines.Add(tableText);
                continue;
            }

            var style = styles.NameOf(block);
            var text = Clean(ParagraphText(block));

            if (Heading1Styles.Contains(style))
            {
                inToc = false;
                if (text.Length == 0)
                    continue;
                chapter++;
                clause = 0;
                chapterTitle = text;
                skipping = false;
                Start(chapter.ToString(), text);
                continue;
            }
            if (inToc || text.Length == 0)
                continue;

            if (style == AppendixTitleStyle)
            {
                Start(text, text);
                skipping = false;
                continue;
            }
            if (style == AppendixDescStyle && current is { } a && a.Number.StartsWith("נספח", StringComparison.Ordinal))
            {
                a.Title = $"{a.Number} – {text}";
                if (text.Contains(ChangeLogWords, StringComparison.Ordinal))
                {
                    current = null;
                    skipping = true;
                }
                continue;
            }
            if (skipping)
                continue;

            if (IsNumberedClause(block, style, styles, text))
            {
                clause++;
                // A short clause is a sub-heading; a long one is a self-contained rule.
                Start($"{chapter}.{clause}", ShortTitle(text), text.Length <= TitleMaxLength ? null : text);
                continue;
            }

            if (current is null)
                Start(chapter.ToString(), chapterTitle);
            current!.Lines.Add(text);
        }
        Flush();

        // A chapter heading with nothing under it but numbered clauses is just a label.
        return sections.Where(s => s.Number.Contains('.') || s.Body != s.Title).ToList();
    }

    private static List<Section> ParseMessage(List<XElement> blocks, StyleTable styles)
    {
        var intro = new List<string>();
        var tables = new List<Section>();
        var lastParagraph = "";
        var skipping = false;

        foreach (var block in blocks)
        {
            if (block.Name == W + "p")
            {
                var style = styles.NameOf(block);
                var text = Clean(ParagraphText(block));
                if (text.Length == 0)
                    continue;
                if (style == AppendixTitleStyle)
                {
                    skipping = true; // a message's appendix is its change log
                    continue;
                }
                if (style == AppendixDescStyle && !text.Contains(ChangeLogWords, StringComparison.Ordinal))
                    skipping = false;
                if (skipping)
                    continue;
                if (tables.Count == 0)
                    intro.Add(text);
                lastParagraph = text;
            }
            else
            {
                if (skipping)
                    continue;
                var tableText = TableText(block);
                if (tableText.Length == 0)
                    continue;
                var number = $"טבלה {tables.Count + 1}";
                var title = lastParagraph.Length > 0 ? ShortTitle(lastParagraph) : number;
                tables.Add(new Section(number, title, tableText, 0));
            }
        }

        var all = new List<Section>();
        if (intro.Count > 0)
            all.Add(new Section("פתיחה", ShortTitle(intro[0]), string.Join("\n", intro), 0));
        all.AddRange(tables);
        return all.Select((s, i) => s with { Position = i }).ToList();
    }

    private static bool IsNumberedClause(XElement paragraph, string style, StyleTable styles, string text)
    {
        if (!ClauseStyles.Contains(style) || text.Length == 0)
            return false;
        var own = paragraph.Element(W + "pPr")?.Element(W + "numPr")?.Element(W + "numId")?.Attribute(W + "val")?.Value;
        return own is null || own == styles.NumIdOf(StyleTable.StyleIdOf(paragraph));
    }

    /// <summary>Visible text only: w:t runs and tabs. Field instructions (w:instrText) and deleted text are not included.</summary>
    private static string ParagraphText(XElement paragraph)
    {
        var sb = new StringBuilder();
        foreach (var e in paragraph.Descendants())
        {
            if (e.Name == W + "t")
                sb.Append(e.Value);
            else if (e.Name == W + "tab")
                sb.Append(' ');
            else if (e.Name == W + "br" || e.Name == W + "cr")
                sb.Append(' ');
        }
        return sb.ToString();
    }

    private static string TableText(XElement table)
    {
        var lines = new List<string>();
        foreach (var row in table.Elements(W + "tr"))
        {
            var cells = row.Elements(W + "tc")
                .Select(tc => Clean(string.Join(" ", tc.Descendants(W + "p").Select(ParagraphText))))
                .Where(t => t.Length > 0)
                .ToList();
            if (cells.Count > 0)
                lines.Add(string.Join(" | ", cells));
        }
        return string.Join("\n", lines);
    }

    private static string ShortTitle(string text)
    {
        text = text.Trim();
        if (text.Length <= TitleMaxLength)
            return text.TrimEnd(':');
        var cut = text[..TitleMaxLength];
        var space = cut.LastIndexOf(' ');
        return (space > 0 ? cut[..space] : cut) + "…";
    }

    private static XDocument? LoadXml(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        if (entry is null)
            return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private sealed class SectionBuilder(string number, string title)
    {
        public string Number { get; } = number;
        public string Title { get; set; } = title;
        public List<string> Lines { get; } = [];
    }

    /// <summary>Style id → name, base style, and the numbering list the style uses.</summary>
    private sealed class StyleTable
    {
        private readonly Dictionary<string, (string Name, string? BasedOn, string? NumId)> _styles = new();

        public static StyleTable Load(XDocument? stylesXml)
        {
            var table = new StyleTable();
            if (stylesXml?.Root is null)
                return table;
            foreach (var s in stylesXml.Root.Elements(W + "style"))
            {
                var id = s.Attribute(W + "styleId")?.Value;
                if (id is null)
                    continue;
                table._styles[id] = (
                    s.Element(W + "name")?.Attribute(W + "val")?.Value ?? id,
                    s.Element(W + "basedOn")?.Attribute(W + "val")?.Value,
                    s.Element(W + "pPr")?.Element(W + "numPr")?.Element(W + "numId")?.Attribute(W + "val")?.Value);
            }
            return table;
        }

        public static string? StyleIdOf(XElement paragraph) =>
            paragraph.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;

        public string NameOf(XElement paragraph)
        {
            var id = StyleIdOf(paragraph);
            return id != null && _styles.TryGetValue(id, out var s) ? s.Name : "";
        }

        /// <summary>The numbering list a style uses, inherited through its base styles.</summary>
        public string? NumIdOf(string? styleId)
        {
            for (var depth = 0; styleId != null && depth < 20; depth++)
            {
                if (!_styles.TryGetValue(styleId, out var s))
                    return null;
                if (s.NumId != null)
                    return s.NumId;
                styleId = s.BasedOn;
            }
            return null;
        }
    }
}
