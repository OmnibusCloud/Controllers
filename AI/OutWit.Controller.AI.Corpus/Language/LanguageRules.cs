using System.Collections.Frozen;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// The rule data of one language: its script (which decides what a word is), its stop words
/// and how many distinct ones a document must hold, and the quality thresholds that differ from
/// its script's defaults. Data, not code: a language pack carries one per language, and the
/// quality stages read the language's <see cref="Quality"/> rules from it. Canonical by
/// construction — stop words distinct and in ordinal order, thresholds in name order — so the
/// same data always serializes to the same bytes.
/// </summary>
public sealed class LanguageRules
{
    #region Constants

    /// <summary>Distinct stop words a document must hold when a language has a list and does not say otherwise (Gopher; FineWeb-2's <c>min_stop_words=2</c>).</summary>
    public const int DEFAULT_MIN_STOP_WORDS = 2;

    #endregion

    #region Constructors

    /// <summary>Creates a language's rule data.</summary>
    /// <param name="code">The language code (ISO 639-3, as the language profile names it).</param>
    /// <param name="script">The ISO 15924 script code (e.g. <c>Latn</c>, <c>Jpan</c>).</param>
    /// <param name="stopWords">The stop words, matched as whole words (order and repeats do not matter).</param>
    /// <param name="minStopWords">Distinct stop words a document must hold (0: the rule is off).</param>
    /// <param name="overrides">Thresholds that differ from the script's defaults, by <see cref="QualityRuleOverrides"/> name.</param>
    /// <exception cref="ArgumentException">A value is malformed: an empty code, a script that is not an ISO 15924 code, a stop word with whitespace, a count out of range, an unknown threshold.</exception>
    public LanguageRules(string code, string script, IEnumerable<string> stopWords, int minStopWords, IEnumerable<KeyValuePair<string, IReadOnlyList<double>>> overrides)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Any(char.IsWhiteSpace))
            throw new ArgumentException("A language code is a non-empty word.", nameof(code));
        if (!LanguageScripts.IsWellFormed(script))
            throw new ArgumentException($"{code}: '{script}' is not an ISO 15924 script code (four letters, e.g. Latn).", nameof(script));

        var words = stopWords.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (words.Any(word => word.Length == 0 || word.Any(char.IsWhiteSpace)))
            throw new ArgumentException($"{code}: a stop word is a word: not empty, no whitespace.", nameof(stopWords));
        if (minStopWords < 0 || minStopWords > words.Count)
            throw new ArgumentException($"{code}: min_stop_words {minStopWords} is not between 0 and the {words.Count} stop words.", nameof(minStopWords));

        var thresholds = overrides.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        if (thresholds.Select(pair => pair.Key).Distinct(StringComparer.Ordinal).Count() != thresholds.Count)
            throw new ArgumentException($"{code}: a threshold is set twice.", nameof(overrides));

        Code = code;
        Script = script;
        StopWords = words;
        MinStopWords = minStopWords;
        Overrides = thresholds.Select(pair => new KeyValuePair<string, IReadOnlyList<double>>(pair.Key, [.. pair.Value])).ToList();

        try
        {
            Quality = Build();
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException($"{code}: {exception.Message}", nameof(overrides), exception);
        }
    }

    #endregion

    #region Functions

    /// <summary>The script's own defaults: <see cref="QualityRules.Unspaced"/> for a script written without spaces, else <see cref="QualityRules.Spaced"/>.</summary>
    /// <param name="script">The ISO 15924 code.</param>
    /// <returns>The base rules.</returns>
    public static QualityRules ScriptDefaults(string script)
    {
        return LanguageScripts.IsUnspaced(script) ? QualityRules.Unspaced : QualityRules.Spaced;
    }

    /// <summary>Whether another language's rule data is the same, value by value.</summary>
    /// <param name="other">The other rule data.</param>
    /// <returns>True when code, script, stop words, the minimum and every threshold are equal.</returns>
    public bool SameAs(LanguageRules other)
    {
        return Code == other.Code
               && Script == other.Script
               && MinStopWords == other.MinStopWords
               && StopWords.SequenceEqual(other.StopWords, StringComparer.Ordinal)
               && Overrides.Count == other.Overrides.Count
               && Overrides.Zip(other.Overrides).All(pair => pair.First.Key == pair.Second.Key && pair.First.Value.SequenceEqual(pair.Second.Value));
    }

    private QualityRules Build()
    {
        var rules = ScriptDefaults(Script);
        foreach (var (key, values) in Overrides)
            rules = QualityRuleOverrides.Apply(rules, key, values);

        if (StopWords.Count > 0 && MinStopWords > 0)
            rules = rules with { StopWords = StopWords.ToFrozenSet(StringComparer.Ordinal), MinStopWords = MinStopWords };

        return rules;
    }

    #endregion

    #region Properties

    /// <summary>The language code.</summary>
    public string Code { get; }

    /// <summary>The ISO 15924 script code.</summary>
    public string Script { get; }

    /// <summary>The stop words, distinct, in ordinal order.</summary>
    public IReadOnlyList<string> StopWords { get; }

    /// <summary>Distinct stop words a document must hold (0: the rule is off).</summary>
    public int MinStopWords { get; }

    /// <summary>The thresholds that differ from the script's defaults, in name order.</summary>
    public IReadOnlyList<KeyValuePair<string, IReadOnlyList<double>>> Overrides { get; }

    /// <summary>The quality rules of a document in this language: the script's defaults, the overrides, the stop words.</summary>
    public QualityRules Quality { get; }

    #endregion
}
