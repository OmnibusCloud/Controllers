using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The pages of the generated crawl: a few hundred pages of sites that do not exist, in the
/// sixteen languages the reference language pack supports and four it knows and does not
/// support, written from the sentences of <see cref="CrawlSynthLanguage"/>. They are what the
/// tests' shards hold in the place of real crawl records: no page of anybody else is in the
/// repository.
/// <para>
/// The pages are made to give every stage of the pipeline its work. Most are articles: a menu,
/// a heading, paragraphs, links to other pages, a foot, as a page of the web has them. The rest
/// are the pages a crawl is full of and a corpus does not want — a page too thin to keep, a page
/// of links, a page that repeats itself, a list of bullet points, a page of teasers, a page of
/// hash tags, a page in two languages, a page nobody labelled, an empty page, a template left
/// unfilled, a placeholder text, a near copy of another page — and a page with the letters whose
/// lower case depends on who is asked.
/// </para>
/// <para>
/// A function of <see cref="SEED"/>, the language files and this code, by integer arithmetic
/// only: the same pages on every platform.
/// </para>
/// </summary>
public static class CrawlSynth
{
    #region Constants

    /// <summary>The seed of the committed shards. Another seed is another crawl: every pin of the generated shards moves with it.</summary>
    public const ulong SEED = 2026_11_18;

    /// <summary>The languages of the reference pack's supported profiles.</summary>
    public static readonly IReadOnlyList<string> SUPPORTED = ["ces", "deu", "eng", "fas", "fra", "ita", "jpn", "nld", "pol", "por", "rus", "spa", "tur", "ukr", "vie", "zho"];

    /// <summary>Languages the reference pack knows and does not support.</summary>
    public static readonly IReadOnlyList<string> UNSUPPORTED = ["dan", "ell", "fin", "swe"];

    /// <summary>An article: the page a corpus wants.</summary>
    public const string KIND_ARTICLE = "article";

    /// <summary>A sentence or two under a menu: too little text.</summary>
    public const string KIND_THIN = "thin";

    /// <summary>Links and nothing else: no line of prose.</summary>
    public const string KIND_LINKS = "links";

    /// <summary>One paragraph, again and again.</summary>
    public const string KIND_REPEATED = "repeated";

    /// <summary>A list of bullet points.</summary>
    public const string KIND_BULLETS = "bullets";

    /// <summary>Teasers that end in an ellipsis.</summary>
    public const string KIND_TEASERS = "teasers";

    /// <summary>Sentences full of hash tags.</summary>
    public const string KIND_HASHES = "hashes";

    /// <summary>A page in two languages.</summary>
    public const string KIND_MIXED = "mixed";

    /// <summary>An article no classifier labelled.</summary>
    public const string KIND_UNLABELLED = "unlabelled";

    /// <summary>A page without a word.</summary>
    public const string KIND_EMPTY = "empty";

    /// <summary>A page of a few figures: too few letters to tell a language by.</summary>
    public const string KIND_STUB = "stub";

    /// <summary>An article with a template's placeholders left in it.</summary>
    public const string KIND_TEMPLATE = "template";

    /// <summary>An article with a placeholder text left in it.</summary>
    public const string KIND_LOREM = "lorem";

    /// <summary>An article with letters whose lower case a platform's library and the Unicode table disagree on.</summary>
    public const string KIND_EDGE = "edge";

    /// <summary>A near copy of an earlier article, under another address.</summary>
    public const string KIND_TWIN = "twin";

    private const string COOKIE_NOTICE = "This site uses cookies to remember the language you read it in.";
    private const string SCRIPT_NOTICE = "Please enable JavaScript to see the map of the valley.";
    private const string TEMPLATE_LINE = "The page builder left its template behind, and every heading shows {title} instead of a name, signed by {author} on {date}.";
    private const string LOREM_LINE = "Under the photograph the printer left the words lorem ipsum where the caption should have been.";
    private const string EDGE_LINE = "The enamel sign over the old depot still reads ԤАРК ǅungla ͿΟΤΑ ϴΕΟΣ in tall painted letters.";
    private const string HASH_TAGS = " #valley #harbour #notes";
    private const string BULLET = "• ";
    private const string ELLIPSIS = "…";

    private static readonly (string Host, string Name)[] SITES =
    [
        ("valley-courier", "Valley Courier"), ("harbour-notes", "Harbour Notes"), ("mill-road", "Mill Road"), ("old-bridge", "Old Bridge"),
        ("river-gazette", "River Gazette"), ("market-square", "Market Square"), ("green-ferry", "Green Ferry"), ("stone-quay", "Stone Quay"),
        ("north-meadow", "North Meadow"), ("clock-tower", "Clock Tower"), ("orchard-lane", "Orchard Lane"), ("lower-lock", "Lower Lock")
    ];

    /// <summary>The charsets a site of a language may serve its pages in, beside UTF-8.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> LEGACY = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["eng"] = ["windows-1252", "iso-8859-1"],
        ["deu"] = ["windows-1252", "iso-8859-1", "iso-8859-15"],
        ["fra"] = ["windows-1252", "iso-8859-1", "iso-8859-15"],
        ["spa"] = ["windows-1252", "iso-8859-1"],
        ["por"] = ["windows-1252", "iso-8859-1"],
        ["ita"] = ["windows-1252", "iso-8859-1"],
        ["nld"] = ["windows-1252", "iso-8859-1"],
        ["pol"] = ["windows-1250", "iso-8859-2"],
        ["ces"] = ["windows-1250", "iso-8859-2"],
        ["rus"] = ["windows-1251", "koi8-r"],
        ["ukr"] = ["windows-1251", "koi8-u"],
        ["tur"] = ["windows-1254", "iso-8859-9"],
        ["jpn"] = ["shift_jis", "euc-jp"],
        ["zho"] = ["gb2312", "gbk", "gb18030"],
        ["swe"] = ["iso-8859-1", "windows-1252"],
        ["dan"] = ["iso-8859-1", "windows-1252"],
        ["fin"] = ["iso-8859-15", "windows-1252"],
        ["ell"] = ["windows-1253", "iso-8859-7"]
    };

    #endregion

    #region Functions

    /// <summary>The pages of the crawl, in the order a shard holds them.</summary>
    /// <param name="fixtures">The fixtures folder (the language files are under it).</param>
    /// <returns>The pages.</returns>
    public static IReadOnlyList<CrawlSynthPage> Pages(string fixtures)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var languages = SUPPORTED.Concat(UNSUPPORTED).ToDictionary(code => code, code => CrawlSynthLanguage.Load(fixtures, code), StringComparer.Ordinal);
        var random = new CrawlSynthRandom(SEED);
        var plan = Plan();
        random.Shuffle(plan);

        var pages = new List<CrawlSynthPage>(plan.Count);
        var served = new Dictionary<string, int>(StringComparer.Ordinal);
        CrawlSynthPage? lastArticle = null;
        foreach (var (kind, first, second) in plan)
        {
            var page = Page(pages.Count, kind, first, second, languages, lastArticle, served, random);
            if (page.Kind == KIND_ARTICLE && page.Language == "eng")
                lastArticle = page;

            pages.Add(page);
        }

        return pages;
    }

    /// <summary>What the crawl holds: how many pages of each kind in each language, before they are put into their order.</summary>
    private static List<(string Kind, string Language, string Second)> Plan()
    {
        var plan = new List<(string, string, string)>();

        void Add(string kind, int count, params string[] languages)
        {
            for (var index = 0; index < count; index++)
                plan.Add((kind, languages.Length == 0 ? "" : languages[index % languages.Length], ""));
        }

        var supported = SUPPORTED.ToArray();
        Add(KIND_ARTICLE, 140, "eng");
        foreach (var code in SUPPORTED.Where(code => code != "eng"))
            Add(KIND_ARTICLE, 12, code);
        foreach (var code in UNSUPPORTED)
            Add(KIND_ARTICLE, 6, code);

        Add(KIND_THIN, 16, supported);
        Add(KIND_LINKS, 12, supported);
        Add(KIND_REPEATED, 8, "eng", "eng", "deu", "eng", "rus", "eng", "deu", "rus");
        Add(KIND_BULLETS, 6, "eng", "fra", "eng", "spa", "eng", "pol");
        Add(KIND_TEASERS, 4, "eng");
        Add(KIND_HASHES, 3, "eng");
        Add(KIND_UNLABELLED, 14, supported);
        Add(KIND_EMPTY, 3);
        Add(KIND_STUB, 3);
        Add(KIND_TEMPLATE, 2, "eng");
        Add(KIND_LOREM, 2, "eng");
        Add(KIND_EDGE, 2, "eng");
        Add(KIND_TWIN, 4, "eng");

        foreach (var (first, second) in new[] { ("eng", "deu"), ("fra", "eng"), ("rus", "ukr"), ("spa", "por"), ("ita", "eng"), ("nld", "deu") })
            plan.Add((KIND_MIXED, first, second));

        return plan;
    }

    private static CrawlSynthPage Page(int index, string kind, string code, string second, IReadOnlyDictionary<string, CrawlSynthLanguage> languages, CrawlSynthPage? lastArticle, Dictionary<string, int> served, CrawlSynthRandom random)
    {
        var site = random.Pick(SITES);
        var url = $"https://{site.Host}.example/{(code.Length > 0 ? code : "xx")}/{kind}-{index:D4}.html";

        if (kind == KIND_EMPTY)
            return new CrawlSynthPage { Url = url, Kind = kind, Language = "", Title = "", Blocks = [] };

        if (kind == KIND_STUB)
            return new CrawlSynthPage { Url = url, Kind = kind, Language = "", Title = "", Blocks = [(CrawlSynthRole.Paragraph, $"{random.Between(1, 28)} / {random.Between(1, 12)} / 2026")] };

        var language = languages[code];
        var body = new List<(CrawlSynthRole, string)>();
        var teasers = random.Between(5, 12);
        var title = $"{random.Pick(language.Labels)} | {site.Name}";

        switch (kind)
        {
            case KIND_THIN:
                body.Add((CrawlSynthRole.Paragraph, Join(language, language.Compose(random, random.Between(1, 2)))));
                break;

            case KIND_LINKS:
                teasers = random.Between(10, 25);
                break;

            case KIND_REPEATED:
                var refrain = Join(language, language.Compose(random, 3));
                for (var times = random.Between(6, 9); times > 0; times--)
                    body.Add((CrawlSynthRole.Paragraph, refrain));
                break;

            case KIND_BULLETS:
                body.AddRange(language.Compose(random, random.Between(12, 20)).Select(sentence => (CrawlSynthRole.Item, BULLET + sentence)));
                break;

            case KIND_TEASERS:
                body.AddRange(language.Compose(random, random.Between(10, 16)).Select(sentence => (CrawlSynthRole.Paragraph, sentence.TrimEnd('.') + ELLIPSIS)));
                break;

            case KIND_HASHES:
                body.AddRange(Paragraphs(language, language.Compose(random, random.Between(8, 14)).Select(sentence => sentence.TrimEnd('.') + HASH_TAGS + ".").ToList(), random));
                break;

            case KIND_MIXED:
                var other = languages[second];
                body.AddRange(Paragraphs(language, language.Compose(random, random.Between(4, 7)), random));
                body.AddRange(Paragraphs(other, other.Compose(random, random.Between(4, 7)), random));
                break;

            case KIND_TWIN when lastArticle != null:
                // The earlier article's text with its last paragraph written anew: a near copy.
                title = lastArticle.Title;
                var copied = lastArticle.Blocks.Where(block => block.Role == CrawlSynthRole.Paragraph).ToList();
                body.AddRange(copied.Take(copied.Count - 1));
                body.Add((CrawlSynthRole.Paragraph, Join(language, language.Compose(random, 2))));
                break;

            default:
                body.AddRange(Paragraphs(language, language.Compose(random, Length(language, random)), random));
                break;
        }

        if (kind == KIND_TEMPLATE)
            body.Insert(1, (CrawlSynthRole.Paragraph, TEMPLATE_LINE));
        if (kind == KIND_LOREM)
            body.Insert(1, (CrawlSynthRole.Paragraph, LOREM_LINE));
        if (kind == KIND_EDGE)
            body.Insert(1, (CrawlSynthRole.Paragraph, EDGE_LINE));

        var blocks = Frame(language, site.Name, body, teasers, random);
        var (charset, place) = Charset(code, title, blocks, served, random);

        return new CrawlSynthPage
        {
            Url = url,
            Kind = kind,
            Language = code,
            Label = kind switch
            {
                KIND_UNLABELLED => null,
                KIND_MIXED => $"{code},{second}",
                _ => code
            },
            Title = title,
            Blocks = blocks,
            Charset = charset,
            CharsetPlace = place,
            Unclosed = random.Next(10) == 0
        };
    }

    /// <summary>How many sentences an article has: an English one is long, one of a language with thirty sentences shorter, so that two of its articles are not one.</summary>
    private static int Length(CrawlSynthLanguage language, CrawlSynthRandom random)
    {
        if (language.Code == "eng")
            return random.Between(8, 20);

        return language.Capacity >= 30 ? random.Between(6, 14) : random.Between(6, 10);
    }

    /// <summary>The page around its body: the site's menu, a heading, the body, links to other pages, the foot.</summary>
    private static List<(CrawlSynthRole Role, string Text)> Frame(CrawlSynthLanguage language, string site, List<(CrawlSynthRole, string)> body, int teasers, CrawlSynthRandom random)
    {
        var blocks = new List<(CrawlSynthRole, string)>();
        blocks.AddRange(language.Labels.Select(label => (CrawlSynthRole.Menu, label)));
        blocks.Add((CrawlSynthRole.Heading, random.Pick(language.Labels)));
        blocks.AddRange(body);

        foreach (var sentence in language.Compose(random, teasers))
            blocks.Add((CrawlSynthRole.Teaser, Teaser(language, sentence, random)));

        // The copyright sign is not in every charset a site of these languages may use (Shift_JIS, GB2312, ISO 8859-2): such sites spell it.
        var sign = language.Code is "jpn" or "zho" or "pol" or "ces" ? "(c)" : "©";
        blocks.Add((CrawlSynthRole.Footer, $"{sign} 2026 {site}"));
        if (language.Code == "eng" && random.Next(2) == 0)
            blocks.Add((CrawlSynthRole.Footer, random.Next(2) == 0 ? COOKIE_NOTICE : SCRIPT_NOTICE));

        return blocks;
    }

    /// <summary>The start of a sentence, as a link to another page shows it: a few words, no full stop.</summary>
    private static string Teaser(CrawlSynthLanguage language, string sentence, CrawlSynthRandom random)
    {
        if (language.Unspaced)
            return sentence[..Math.Min(sentence.Length, random.Between(8, 14))].TrimEnd('、', '。', '，');

        var words = sentence.Split(' ');
        return string.Join(' ', words.Take(random.Between(3, 7))).TrimEnd('.', ',', ';', ':', '،');
    }

    /// <summary>Sentences as paragraphs of two to five.</summary>
    private static IEnumerable<(CrawlSynthRole, string)> Paragraphs(CrawlSynthLanguage language, IReadOnlyList<string> sentences, CrawlSynthRandom random)
    {
        var paragraphs = new List<(CrawlSynthRole, string)>();
        for (var start = 0; start < sentences.Count;)
        {
            var size = Math.Min(sentences.Count - start, random.Between(2, language.Code == "eng" ? 5 : 4));
            paragraphs.Add((CrawlSynthRole.Paragraph, Join(language, sentences.Skip(start).Take(size))));
            start += size;
        }

        return paragraphs;
    }

    private static string Join(CrawlSynthLanguage language, IEnumerable<string> sentences)
    {
        return string.Join(language.Unspaced ? "" : " ", sentences);
    }

    /// <summary>
    /// The charset a page is served in and where it says so. Every second page of a language
    /// that has charsets of before UTF-8 (every fourth English one) is in one of them, each in
    /// its turn, when that charset can write the page; one page in twelve is plain ASCII with
    /// numeric character references.
    /// </summary>
    private static (string Charset, string Place) Charset(string code, string title, IReadOnlyList<(CrawlSynthRole Role, string Text)> blocks, Dictionary<string, int> served, CrawlSynthRandom random)
    {
        var references = random.Next(12) == 0;
        var place = random.Next(20);
        var turn = served.GetValueOrDefault(code);
        served[code] = turn + 1;

        if (references)
            return ("us-ascii", "references");

        var every = code == "eng" ? 4 : 2;
        if (turn % every == 0 && LEGACY.TryGetValue(code, out var charsets))
        {
            var charset = charsets[turn / every % charsets.Length];
            var text = title + "\n" + string.Join('\n', blocks.Select(block => block.Text));
            var encoding = Encoding.GetEncoding(charset);
            if (encoding.GetString(encoding.GetBytes(text)) == text)
                return (charset, place < 12 ? "header" : "meta");
        }

        return ("utf-8", place < 12 ? "header" : place < 17 ? "meta" : "none");
    }

    #endregion
}
