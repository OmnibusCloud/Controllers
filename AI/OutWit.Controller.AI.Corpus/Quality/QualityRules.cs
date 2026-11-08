namespace OutWit.Controller.AI.Corpus.Quality;

/// <summary>
/// The thresholds of the quality filter — the Gopher rule set as the starting point, every
/// value a parameter of the pipeline contract. Two shapes: <see cref="Spaced"/> for scripts
/// that separate words with spaces, <see cref="Unspaced"/> for scripts that do not (Chinese,
/// Japanese, Thai, ...), where a "word" is a letter and the word-shape rules do not apply.
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

    /// <summary>Rules for space-separated scripts.</summary>
    public static readonly QualityRules Spaced = new();

    /// <summary>Rules for scripts without word spacing: a word is a letter; no word-shape rules.</summary>
    public static readonly QualityRules Unspaced = new()
    {
        MinWords = 100,
        MaxWords = 1_000_000,
        MinMeanWordLength = 0,
        MaxMeanWordLength = double.MaxValue,
        MinAlphabeticWordRatio = 0
    };

    #endregion

    #region Functions

    /// <summary>
    /// The rules for a language code.
    /// </summary>
    /// <param name="languageCode">An ISO 639-3 code, or "" when unknown.</param>
    /// <returns><see cref="Unspaced"/> for scripts without word spacing, else <see cref="Spaced"/>.</returns>
    public static QualityRules ForLanguage(string languageCode)
    {
        return UNSPACED_LANGUAGES.Contains(languageCode) ? Unspaced : Spaced;
    }

    #endregion

    #region Properties

    /// <summary>Fewest words a document may have.</summary>
    public int MinWords { get; init; } = 50;

    /// <summary>Most words a document may have.</summary>
    public int MaxWords { get; init; } = 100_000;

    /// <summary>Lowest mean word length.</summary>
    public double MinMeanWordLength { get; init; } = 3;

    /// <summary>Highest mean word length.</summary>
    public double MaxMeanWordLength { get; init; } = 10;

    /// <summary>Most hash marks and ellipses per word.</summary>
    public double MaxSymbolToWordRatio { get; init; } = 0.1;

    /// <summary>Most lines (by count) that may start with a bullet.</summary>
    public double MaxBulletLineRatio { get; init; } = 0.9;

    /// <summary>Most lines (by count) that may end with an ellipsis.</summary>
    public double MaxEllipsisLineRatio { get; init; } = 0.3;

    /// <summary>Fewest words that must carry a letter.</summary>
    public double MinAlphabeticWordRatio { get; init; } = 0.8;

    /// <summary>Most lines (by count) that repeat an earlier line.</summary>
    public double MaxDuplicateLineRatio { get; init; } = 0.3;

    /// <summary>Most characters (by count) in lines that repeat an earlier line.</summary>
    public double MaxDuplicateLineCharRatio { get; init; } = 0.2;

    /// <summary>Most characters the single most frequent word n-gram may cover, per n = 2, 3, 4.</summary>
    public IReadOnlyList<double> MaxTopNgramCharRatio { get; init; } = [0.20, 0.18, 0.16];

    /// <summary>Most characters that repeated word n-grams may cover, per n = 5 .. 10.</summary>
    public IReadOnlyList<double> MaxDuplicateNgramCharRatio { get; init; } = [0.15, 0.14, 0.13, 0.12, 0.11, 0.10];

    #endregion
}
