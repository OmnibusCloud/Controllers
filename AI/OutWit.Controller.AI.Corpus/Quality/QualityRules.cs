using System.Collections.Frozen;

namespace OutWit.Controller.AI.Corpus.Quality;

/// <summary>
/// The thresholds of the quality filter, each with its source: the Gopher rule set (Rae et al.
/// 2021, "Scaling Language Models", appendix A.1.1 and table A1) as DataTrove implements it,
/// with FineWeb-2's per-language values (huggingface/fineweb-2, <c>configs/*.yml</c>) where
/// Gopher's English thresholds do not carry over: scripts written without word spacing. Every
/// value is part of the pipeline contract. Three shapes: <see cref="Spaced"/> for scripts that
/// separate words with spaces, <see cref="Unspaced"/> for scripts that do not (Chinese, Thai,
/// ...), where a "word" is a letter and the word-shape rules do not apply, and
/// <see cref="Japanese"/>; <see cref="ForLanguage"/> adds the language's stop words
/// (<see cref="QualityStopWords"/>).
/// </summary>
public sealed record QualityRules
{
    #region Constants

    private static readonly HashSet<string> UNSPACED_LANGUAGES = new(StringComparer.Ordinal)
    {
        "zho", "cmn", "yue", "jpn", "tha", "khm", "lao", "mya", "bod"
    };

    #endregion

    #region Fields

    /// <summary>Rules for space-separated scripts: Gopher's thresholds.</summary>
    public static readonly QualityRules Spaced = new();

    /// <summary>
    /// Rules for scripts without word spacing: a word is a letter (a run of letters of a spaced
    /// script inside such text, a Latin brand name say, is one word); no word-shape rules; the
    /// repetition thresholds of FineWeb-2's Chinese (<c>cmn_Hani.yml</c>), the largest language
    /// of these scripts. Gopher's thresholds are for English words: over letters, a repetition
    /// rule failed 47 % of the Japanese and 26 % of the Chinese pages of four Common Crawl WET
    /// files (21-23 % of the pages elsewhere), most of them prose that names one product or
    /// place again and again.
    /// </summary>
    public static readonly QualityRules Unspaced = new()
    {
        CountsLetters = true,
        // Ours, not a reference's: about Gopher's 50 words at a Chinese or Japanese word's one
        // to two characters.
        MinWords = 100,
        MaxWords = 1_000_000,
        MinMeanWordLength = 0,
        MaxMeanWordLength = double.MaxValue,
        MinAlphabeticWordRatio = 0,
        MaxDuplicateLineRatio = 0.287,
        MaxTopNgramCharRatio = [0.256, 0.201, 0.171],
        MaxDuplicateNgramCharRatio = [0.198, 0.182, 0.167, 0.154, 0.14, 0.127]
    };

    /// <summary>Rules for Japanese: <see cref="Unspaced"/> with FineWeb-2's Japanese repetition thresholds (<c>jpn_Jpan.yml</c>).</summary>
    public static readonly QualityRules Japanese = Unspaced with
    {
        MaxDuplicateLineRatio = 0.328,
        MaxTopNgramCharRatio = [0.239, 0.196, 0.172],
        MaxDuplicateNgramCharRatio = [0.243, 0.225, 0.207, 0.19, 0.175, 0.159]
    };

    // The rules of each language with a stop-word list: its script's rules and the list.
    private static readonly FrozenDictionary<string, QualityRules> BY_LANGUAGE = QualityStopWords.Languages
        .ToFrozenDictionary(code => code, code => Base(code) with { StopWords = StopWordsOf(code), MinStopWords = QualityStopWords.MIN_STOP_WORDS }, StringComparer.Ordinal);

    /// <summary>Rules for English: <see cref="Spaced"/> and Gopher's stop-word rule.</summary>
    public static readonly QualityRules English = BY_LANGUAGE["eng"];

    #endregion

    #region Functions

    /// <summary>
    /// The rules for a language code: its script's thresholds (<see cref="Japanese"/>,
    /// <see cref="Unspaced"/> for the other scripts without word spacing, else
    /// <see cref="Spaced"/>) with the language's stop words when <see cref="QualityStopWords"/>
    /// has a list for it.
    /// </summary>
    /// <param name="languageCode">An ISO 639-3 code, or "" when unknown.</param>
    /// <returns>The rules (the same instance for the same code).</returns>
    public static QualityRules ForLanguage(string languageCode)
    {
        return BY_LANGUAGE.TryGetValue(languageCode, out var rules) ? rules : Base(languageCode);
    }

    private static QualityRules Base(string languageCode)
    {
        if (languageCode == "jpn")
            return Japanese;

        return UNSPACED_LANGUAGES.Contains(languageCode) ? Unspaced : Spaced;
    }

    private static IReadOnlySet<string> StopWordsOf(string languageCode)
    {
        QualityStopWords.TryGet(languageCode, out var stopWords);
        return stopWords;
    }

    #endregion

    #region Properties

    /// <summary>Whether a "word" is a letter (scripts without word spacing) rather than a whitespace-separated token.</summary>
    public bool CountsLetters { get; init; }

    /// <summary>Fewest words a document may have (Gopher: 50).</summary>
    public int MinWords { get; init; } = 50;

    /// <summary>Most words a document may have (Gopher: 100,000).</summary>
    public int MaxWords { get; init; } = 100_000;

    /// <summary>Lowest mean word length (Gopher: 3 characters).</summary>
    public double MinMeanWordLength { get; init; } = 3;

    /// <summary>Highest mean word length (Gopher: 10 characters).</summary>
    public double MaxMeanWordLength { get; init; } = 10;

    /// <summary>Most hash marks per word, and separately most ellipses per word (Gopher: 0.1 "for either").</summary>
    public double MaxSymbolToWordRatio { get; init; } = 0.1;

    /// <summary>Most lines (by count) that may start with a bullet (Gopher: 90 %).</summary>
    public double MaxBulletLineRatio { get; init; } = 0.9;

    /// <summary>Most lines (by count) that may end with an ellipsis (Gopher: 30 %).</summary>
    public double MaxEllipsisLineRatio { get; init; } = 0.3;

    /// <summary>Fewest words that must carry a letter (Gopher: 80 %).</summary>
    public double MinAlphabeticWordRatio { get; init; } = 0.8;

    /// <summary>Most lines (by count) that repeat an earlier line (Gopher, table A1: 0.30).</summary>
    public double MaxDuplicateLineRatio { get; init; } = 0.3;

    /// <summary>Most characters, of the whole text, in lines that repeat an earlier line (Gopher, table A1: 0.20).</summary>
    public double MaxDuplicateLineCharRatio { get; init; } = 0.2;

    /// <summary>Most characters the most frequent word n-gram may cover, per n = 2, 3, 4 (Gopher, table A1: 0.20, 0.18, 0.16).</summary>
    public IReadOnlyList<double> MaxTopNgramCharRatio { get; init; } = [0.20, 0.18, 0.16];

    /// <summary>Most characters that repeats of word n-grams may cover, per n = 5 .. 10 (Gopher, table A1: 0.15 .. 0.10).</summary>
    public IReadOnlyList<double> MaxDuplicateNgramCharRatio { get; init; } = [0.15, 0.14, 0.13, 0.12, 0.11, 0.10];

    /// <summary>The language's stop words, matched as whole words, case and all.</summary>
    public IReadOnlySet<string> StopWords { get; init; } = FrozenSet<string>.Empty;

    /// <summary>Fewest distinct stop words a document must contain (Gopher: two; 0: the rule is off).</summary>
    public int MinStopWords { get; init; }

    #endregion
}
