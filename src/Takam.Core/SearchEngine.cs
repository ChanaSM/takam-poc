using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Takam.Core;

/// <summary>
/// Full-text search over section text, built in memory from the index file.
///
/// Ranking is BM25, the standard ranking formula of search engines (and of SQLite FTS5), with
/// section titles weighted four times more than body text.
///
/// Hebrew attaches prefixes (ה, ו, ב, ל, מ, ש, כ) and suffixes (ים, ות, ה, ת) to words. Each query
/// word is expanded into a few forms: the word itself, the word without one leading prefix letter,
/// and a stem without a plural or feminine ending ("מרצה" → "מרצ*"). A stem form matches indexed
/// words made of the stem plus one known ending only ("מרצים" yes, "מעוניין" for "מעונ*" no).
/// Indexed words also get a second entry without their leading prefix letter, so "ומעונות" in the
/// text is found by "מעונות". Final letters (ם ן ץ ף ך) are folded to their regular forms.
/// A section matches a query word if it contains any of its forms.
/// </summary>
public sealed partial class SearchEngine
{
    private const string PrefixLetters = "הובלמשכ";
    // Folded spelling: final letters are stored as regular letters, so "ים" is "ימ".
    private static readonly string[] Suffixes = ["ימ", "ות", "ה", "ת"];
    /// <summary>Endings a stem form may take: plural, feminine, construct state and their combinations.</summary>
    private static readonly HashSet<string> Endings = new(StringComparer.Ordinal)
        { "", "ימ", "ות", "ה", "ת", "י", "ית", "יות", "יימ", "ו", "ותי", "תו", "תה" };
    private const int MinStem = 2;
    private const int PrefixStripMinLength = 5; // short words like "שעות" start with a "prefix" letter by chance
    private const double TitleWeight = 4.0, BodyWeight = 1.0, K1 = 1.2, B = 0.75;
    private const int SnippetTokens = 24;

    [GeneratedRegex(@"[\w֐-׿]+")]
    private static partial Regex WordRegex();

    private readonly List<Entry> _entries = [];
    private readonly Dictionary<string, List<(int Entry, int TitleTf, int BodyTf)>> _postings = new(StringComparer.Ordinal);
    private readonly string[] _vocabulary;
    private readonly double _avgTitle, _avgBody;
    private readonly List<DocMeta> _documents;

    public SearchEngine(IndexData data)
    {
        _documents = data.Documents.Select(d => d.Meta).ToList();
        foreach (var doc in data.Documents)
        {
            foreach (var s in doc.Sections)
            {
                var titleTokens = Tokenize(s.Title).Select(t => t.Token).ToList();
                var bodyTokens = Tokenize(s.Body).Select(t => t.Token).ToList();
                var index = _entries.Count;
                _entries.Add(new Entry(doc.Meta, s, titleTokens.Count, bodyTokens.Count));
                var tf = new Dictionary<string, (int T, int B)>(StringComparer.Ordinal);
                foreach (var t in titleTokens.SelectMany(IndexTerms)) tf[t] = (tf.GetValueOrDefault(t).T + 1, tf.GetValueOrDefault(t).B);
                foreach (var t in bodyTokens.SelectMany(IndexTerms)) tf[t] = (tf.GetValueOrDefault(t).T, tf.GetValueOrDefault(t).B + 1);
                foreach (var (term, counts) in tf)
                {
                    if (!_postings.TryGetValue(term, out var list))
                        _postings[term] = list = [];
                    list.Add((index, counts.T, counts.B));
                }
            }
        }
        _vocabulary = _postings.Keys.Order(StringComparer.Ordinal).ToArray();
        _avgTitle = _entries.Count == 0 ? 1 : Math.Max(1, _entries.Average(e => e.TitleLength));
        _avgBody = _entries.Count == 0 ? 1 : Math.Max(1, _entries.Average(e => e.BodyLength));
    }

    /// <summary>Sections that contain every word of the query, best first.</summary>
    public List<SearchHit> Search(string query, int limit = 30) =>
        Run(query, requireAll: true).Take(limit).ToList();

    /// <summary>
    /// Sections that contain only some of the words, shown below the full matches so a multi-word
    /// query never dead-ends. At most <paramref name="perDocument"/> per document, so one long
    /// instruction cannot fill the list.
    /// </summary>
    public List<SearchHit> SearchPartial(string query, ISet<string> exclude, int limit = 10, int perDocument = 3)
    {
        if (QueryWords(query).Count < 2)
            return [];
        var perDoc = new Dictionary<string, int>();
        var result = new List<SearchHit>();
        foreach (var hit in Run(query, requireAll: false))
        {
            if (exclude.Contains(hit.SectionKey) || perDoc.GetValueOrDefault(hit.Code) >= perDocument)
                continue;
            perDoc[hit.Code] = perDoc.GetValueOrDefault(hit.Code) + 1;
            result.Add(hit);
            if (result.Count >= limit)
                break;
        }
        return result;
    }

    /// <summary>
    /// What the original site would find among the same documents: a substring match on the
    /// document name and its manually entered keywords only, never the body text.
    /// </summary>
    public List<string> SiteStyleSearch(string query)
    {
        var q = query.Trim();
        if (q.Length == 0)
            return [];
        return _documents
            .Where(d => d.Name.Contains(q, StringComparison.Ordinal) || d.Keywords.Contains(q, StringComparison.Ordinal))
            .Select(d => d.Code)
            .ToList();
    }

    public static List<string> QueryWords(string query) =>
        WordRegex().Matches(query).Select(m => Normalize(m.Value)).Where(w => w.Length >= 2).ToList();

    /// <summary>Terms stored for one word of the text: the word, and the word without a leading prefix letter.</summary>
    private static IEnumerable<string> IndexTerms(string token)
    {
        yield return token;
        if (HasPrefixLetter(token))
            yield return token[1..];
    }

    private static bool HasPrefixLetter(string word) =>
        word.Length >= PrefixStripMinLength && PrefixLetters.Contains(word[0]);

    /// <summary>Forms to try for one query word. A form ending in '*' is a stem that takes one of the known endings.</summary>
    public static SortedSet<string> WordForms(string word)
    {
        var forms = new SortedSet<string>(StringComparer.Ordinal) { word };
        var bases = new List<string> { word };
        if (HasPrefixLetter(word))
        {
            bases.Add(word[1..]);
            forms.Add(word[1..]);
        }
        foreach (var b in bases)
        {
            var suffix = Suffixes.FirstOrDefault(s => b.EndsWith(s, StringComparison.Ordinal) && b.Length - s.Length >= MinStem + 1);
            if (suffix != null)
                forms.Add(b[..^suffix.Length] + "*");
            else if (b.Length >= MinStem + 1)
                forms.Add(b + "*");
        }
        return forms;
    }

    private IEnumerable<SearchHit> Run(string query, bool requireAll)
    {
        var words = QueryWords(query);
        if (words.Count == 0)
            return [];

        // For each query word: the set of index terms it matches (exact forms plus prefix expansions).
        var groups = words.Select(w => ExpandToTerms(WordForms(w))).ToList();
        var scores = new Dictionary<int, double>();
        var matchedGroups = new Dictionary<int, int>();
        var n = _entries.Count;

        foreach (var terms in groups)
        {
            var seenInGroup = new HashSet<int>();
            foreach (var term in terms)
            {
                var postings = _postings[term];
                var idf = Math.Log((n - postings.Count + 0.5) / (postings.Count + 0.5) + 1);
                foreach (var (entry, titleTf, bodyTf) in postings)
                {
                    var e = _entries[entry];
                    var score = idf * (TitleWeight * Bm25Tf(titleTf, e.TitleLength, _avgTitle)
                                       + BodyWeight * Bm25Tf(bodyTf, e.BodyLength, _avgBody));
                    scores[entry] = scores.GetValueOrDefault(entry) + score;
                    if (seenInGroup.Add(entry))
                        matchedGroups[entry] = matchedGroups.GetValueOrDefault(entry) + 1;
                }
            }
        }

        var allTerms = groups.SelectMany(g => g).ToHashSet(StringComparer.Ordinal);
        return scores
            .Where(kv => !requireAll || matchedGroups[kv.Key] == groups.Count)
            .OrderByDescending(kv => kv.Value)
            .Select(kv =>
            {
                var e = _entries[kv.Key];
                return new SearchHit($"{e.Meta.Code}|{e.Section.Number}", e.Meta.Code, e.Meta.Name, e.Meta.DocType,
                    e.Section.Number, e.Section.Title, Snippet(e.Section.Body, allTerms), kv.Value);
            });
    }

    private static double Bm25Tf(int tf, int length, double avgLength) =>
        tf == 0 ? 0 : tf * (K1 + 1) / (tf + K1 * (1 - B + B * length / avgLength));

    /// <summary>Exact forms that exist in the index, plus every indexed term that is a stem form plus a known ending.</summary>
    private HashSet<string> ExpandToTerms(IEnumerable<string> forms)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in forms)
        {
            if (!form.EndsWith('*'))
            {
                if (_postings.ContainsKey(form))
                    terms.Add(form);
                continue;
            }
            var prefix = form[..^1];
            var i = Array.BinarySearch(_vocabulary, prefix, StringComparer.Ordinal);
            for (i = i < 0 ? ~i : i; i < _vocabulary.Length && _vocabulary[i].StartsWith(prefix, StringComparison.Ordinal); i++)
                if (Endings.Contains(_vocabulary[i][prefix.Length..]))
                    terms.Add(_vocabulary[i]);
        }
        return terms;
    }

    /// <summary>About 24 words around the densest cluster of matches, HTML-escaped, matches wrapped in &lt;mark&gt;.</summary>
    private static string Snippet(string body, HashSet<string> terms)
    {
        var tokens = Tokenize(body).ToList();
        if (tokens.Count == 0)
            return "";
        var hits = tokens.Select(t => IndexTerms(t.Token).Any(terms.Contains)).ToArray();

        int bestStart = 0, bestCount = -1, window = Math.Min(SnippetTokens, tokens.Count);
        for (var start = 0; start + window <= tokens.Count; start++)
        {
            var count = 0;
            for (var j = start; j < start + window; j++)
                if (hits[j]) count++;
            if (count > bestCount)
                (bestCount, bestStart) = (count, start);
        }

        var last = tokens[bestStart + window - 1];
        var reachesEnd = bestStart + window == tokens.Count;
        var sb = new StringBuilder();
        if (bestStart > 0)
            sb.Append('…');
        // From the very start of the text when the window starts at the first word, so leading
        // punctuation such as quotes or brackets is kept.
        var cursor = bestStart == 0 ? 0 : tokens[bestStart].Start;
        for (var j = bestStart; j < bestStart + window; j++)
        {
            var t = tokens[j];
            sb.Append(WebUtility.HtmlEncode(body[cursor..t.Start]));
            var word = WebUtility.HtmlEncode(body.Substring(t.Start, t.Length));
            sb.Append(hits[j] ? $"<mark>{word}</mark>" : word);
            cursor = t.Start + t.Length;
        }
        if (reachesEnd)
            sb.Append(WebUtility.HtmlEncode(body[cursor..]));
        else
            sb.Append('…');
        return sb.ToString().Replace('\n', ' ');
    }

    private static IEnumerable<(string Token, int Start, int Length)> Tokenize(string text) =>
        WordRegex().Matches(text).Select(m => (Normalize(m.Value), m.Index, m.Length));

    /// <summary>Lower case, without Hebrew vowel points and cantillation marks, final letters folded.</summary>
    private static string Normalize(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var c in word)
            if (c < '֑' || c > 'ׇ')
                sb.Append(c switch
                {
                    'ך' => 'כ', 'ם' => 'מ', 'ן' => 'נ', 'ף' => 'פ', 'ץ' => 'צ',
                    _ => char.ToLowerInvariant(c),
                });
        return sb.ToString();
    }

    private sealed record Entry(DocMeta Meta, Section Section, int TitleLength, int BodyLength);
}
