using System.Text;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Text;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// Measures a language pack against the crawl's own labels on files it was neither trained nor
/// calibrated on: per label, what the pack does (<see cref="LanguagePackTally"/>) next to what the
/// same profiles do without the unsupported test. A label of a supported language measures recall
/// and the confusion between supported languages; any other label measures how much of a
/// language the pack does not support it calls unknown instead of mislabelling. With judging on,
/// each document also goes through the line stage and the quality filter under its nearest
/// supported language, so the counts say what happens to text that would reach the product.
/// Documents are read the way the pipeline reads a WET record.
/// </summary>
public sealed class LanguagePackEvaluator
{
    #region Constants

    /// <summary>The key under which documents with several labels whose main one is supported are counted.</summary>
    public const string MIXED_SUPPORTED = "(mixed, main supported)";

    /// <summary>The key under which documents with several labels whose main one is not supported are counted.</summary>
    public const string MIXED_OTHER = "(mixed, main other)";

    private const string RECORD_CONVERSION = "conversion";

    #endregion

    #region Fields

    private readonly LanguageModel m_pack;
    private readonly LanguageIdentifier m_identifier;
    private readonly bool m_judge;
    private readonly bool[] m_supported;
    private readonly SortedDictionary<string, LanguagePackTally> m_labels = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    /// <summary>Creates an evaluator.</summary>
    /// <param name="pack">The pack (a WITLID1 model works too: it has no unsupported test).</param>
    /// <param name="judge">Whether to run the line stage and the quality filter on every document.</param>
    public LanguagePackEvaluator(LanguageModel pack, bool judge)
    {
        m_pack = pack;
        m_identifier = new LanguageIdentifier(pack);
        m_judge = judge;
        m_supported = pack.Profiles.Select(profile => pack.Languages.Contains(profile.Code, StringComparer.Ordinal)).ToArray();
    }

    #endregion

    #region Functions

    /// <summary>Counts one labelled document.</summary>
    /// <param name="label">The crawl's label (one code, or several separated by commas, the main one first).</param>
    /// <param name="text">The text as the pipeline reads it.</param>
    public void Add(string label, string text)
    {
        var guess = m_identifier.Identify(text);
        var nearest = NearestSupported(text);

        var kept = false;
        long keptChars = 0;
        if (m_judge && nearest.Length > 0)
        {
            var rules = m_pack.Rules.For(nearest);
            var content = LineCleaner.Clean(text, rules, out _);
            if (content.Length > 0 && QualityFilter.Judge(content, rules).Passed)
            {
                kept = true;
                keptChars = content.Length;
            }
        }

        var key = label.Contains(',') ? (IsSupported(label.Split(',')[0]) ? MIXED_SUPPORTED : MIXED_OTHER) : label;
        if (!m_labels.TryGetValue(key, out var tally))
        {
            tally = new LanguagePackTally();
            m_labels[key] = tally;
        }

        tally.Count(guess, nearest, kept, keptChars);
    }

    /// <summary>Counts the labelled documents of a WET file.</summary>
    /// <param name="wetPath">The WET file.</param>
    /// <returns>Documents counted.</returns>
    public int AddWet(string wetPath)
    {
        var added = 0;
        using var reader = WarcReader.Open(wetPath);
        foreach (var record in reader.ReadRecords())
        {
            var label = record.IdentifiedLanguage;
            if (record.Type != RECORD_CONVERSION || string.IsNullOrEmpty(label))
                continue;

            var text = HtmlTextExtractor.CollapseLines(Encoding.UTF8.GetString(record.Payload.Span));
            if (text.Length == 0)
                continue;

            Add(label, text);
            added++;
        }

        return added;
    }

    /// <summary>Adds another evaluator's counts (the same pack over other files).</summary>
    /// <param name="other">The other evaluator.</param>
    public void Add(LanguagePackEvaluator other)
    {
        foreach (var (label, tally) in other.m_labels)
        {
            if (!m_labels.TryGetValue(label, out var mine))
            {
                mine = new LanguagePackTally();
                m_labels[label] = mine;
            }

            mine.Add(tally);
        }
    }

    /// <summary>Whether a code is a language the pack labels documents with.</summary>
    /// <param name="code">The code.</param>
    /// <returns>True for a supported language.</returns>
    public bool IsSupported(string code)
    {
        return m_pack.Languages.Contains(code, StringComparer.Ordinal);
    }

    private string NearestSupported(string text)
    {
        var distances = m_identifier.Measure(text, out _);
        if (distances == null)
            return "";

        var best = -1;
        for (var i = 0; i < distances.Length; i++)
        {
            if (m_supported[i] && (best < 0 || distances[i] < distances[best]))
                best = i;
        }

        return best < 0 ? "" : m_pack.Profiles[best].Code;
    }

    #endregion

    #region Properties

    /// <summary>The tallies by label (single labels by code, mixed labels under the two mixed keys), in ordinal order.</summary>
    public IReadOnlyDictionary<string, LanguagePackTally> Labels => m_labels;

    #endregion
}
