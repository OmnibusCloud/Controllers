using System.Text;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// One language of the generated pages: the menu labels of a site and the sentences its pages
/// are made of, read from <c>Fixtures/synth/&lt;code&gt;.txt</c> — text written for these tests,
/// none of it taken from a crawl. Most languages bring whole sentences; English brings the four
/// parts of one (an opener, a subject, a predicate, a closer), so that its many pages do not
/// repeat one another.
/// </summary>
public sealed class CrawlSynthLanguage
{
    #region Constants

    /// <summary>The folder of the language files, under the fixtures folder.</summary>
    public const string FOLDER = "synth";

    private const char BYTE_ORDER_MARK = (char)0xFEFF;

    private const string SECTION_LABEL = "label";
    private const string SECTION_SENTENCE = "sentence";
    private const string SECTION_OPENER = "opener";
    private const string SECTION_SUBJECT = "subject";
    private const string SECTION_PREDICATE = "predicate";
    private const string SECTION_CLOSER = "closer";

    #endregion

    #region Constructors

    private CrawlSynthLanguage(string code, IReadOnlyDictionary<string, List<string>> sections)
    {
        Code = code;
        Labels = Section(sections, SECTION_LABEL);
        Sentences = Section(sections, SECTION_SENTENCE);
        Openers = Section(sections, SECTION_OPENER);
        Subjects = Section(sections, SECTION_SUBJECT);
        Predicates = Section(sections, SECTION_PREDICATE);
        Closers = Section(sections, SECTION_CLOSER);

        if (Labels.Count == 0 || Capacity == 0)
            throw new InvalidDataException($"The language file of '{code}' holds no labels or no sentences.");
    }

    #endregion

    #region Functions

    /// <summary>Reads a language file.</summary>
    /// <param name="fixtures">The fixtures folder.</param>
    /// <param name="code">The language's ISO 639-3 code: the file's name.</param>
    /// <returns>The language.</returns>
    public static CrawlSynthLanguage Load(string fixtures, string code)
    {
        var sections = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        List<string>? current = null;

        // Line by line, whatever a checkout made of the line ends.
        foreach (var raw in File.ReadAllText(Path.Combine(fixtures, FOLDER, code + ".txt"), Encoding.UTF8).Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t', BYTE_ORDER_MARK);
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                current = [];
                sections[line[1..^1]] = current;
                continue;
            }

            (current ?? throw new InvalidDataException($"The language file of '{code}' has text before its first section.")).Add(line);
        }

        return new CrawlSynthLanguage(code, sections);
    }

    /// <summary>So many different sentences of the language.</summary>
    /// <param name="random">The generator's sequence.</param>
    /// <param name="count">How many; no more than <see cref="Capacity"/> are returned.</param>
    /// <returns>The sentences, each ending as a sentence of the language ends.</returns>
    public List<string> Compose(CrawlSynthRandom random, int count)
    {
        if (Sentences.Count > 0)
            return random.Draw(Sentences, count);

        // No part twice in one page: a page must not repeat itself.
        count = Math.Min(count, Capacity);
        var openers = random.Draw(Openers, count);
        var subjects = random.Draw(Subjects, count);
        var predicates = random.Draw(Predicates, count);
        var closers = random.Draw(Closers, count);

        return Enumerable.Range(0, count).Select(index => $"{openers[index]} {subjects[index]} {predicates[index]} {closers[index]}.").ToList();
    }

    private static IReadOnlyList<string> Section(IReadOnlyDictionary<string, List<string>> sections, string name)
    {
        return sections.TryGetValue(name, out var lines) ? lines : [];
    }

    #endregion

    #region Properties

    /// <summary>The ISO 639-3 code, as Common Crawl labels a document.</summary>
    public string Code { get; }

    /// <summary>The labels of a site's menu.</summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>Whole sentences (every language but English).</summary>
    public IReadOnlyList<string> Sentences { get; }

    /// <summary>How a composed sentence starts (English).</summary>
    public IReadOnlyList<string> Openers { get; }

    /// <summary>Who it is about (English).</summary>
    public IReadOnlyList<string> Subjects { get; }

    /// <summary>What they do (English).</summary>
    public IReadOnlyList<string> Predicates { get; }

    /// <summary>How it ends (English).</summary>
    public IReadOnlyList<string> Closers { get; }

    /// <summary>The most different sentences one page can have.</summary>
    public int Capacity => Sentences.Count > 0 ? Sentences.Count : Math.Min(Math.Min(Openers.Count, Subjects.Count), Math.Min(Predicates.Count, Closers.Count));

    /// <summary>Whether the language writes its words without spaces between them.</summary>
    public bool Unspaced => Code is "jpn" or "zho";

    #endregion
}
