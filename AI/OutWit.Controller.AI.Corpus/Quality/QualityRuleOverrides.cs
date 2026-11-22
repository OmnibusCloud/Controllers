namespace OutWit.Controller.AI.Corpus.Quality;

/// <summary>
/// The thresholds of <see cref="QualityRules"/> a language's rule data may set, by name: the ten
/// a manifest's <c>pipeline.quality</c> sets (the same names), and the two repetition vectors a
/// manifest cannot set — the most frequent 2..4-gram (<see cref="TOP_NGRAM_CHARS"/>, three
/// values) and the repeated 5..10-grams (<see cref="DUPLICATE_NGRAM_CHARS"/>, six values).
/// FineWeb-2 tunes exactly these per language. A name the controller does not know is an error,
/// never ignored: a pack that sets a rule this controller does not have is refused.
/// </summary>
public static class QualityRuleOverrides
{
    #region Constants

    /// <summary>The most characters the most frequent word 2-, 3- and 4-gram may cover.</summary>
    public const string TOP_NGRAM_CHARS = "top_ngram_chars";

    /// <summary>The most characters repeated word 5- to 10-grams may cover.</summary>
    public const string DUPLICATE_NGRAM_CHARS = "duplicate_ngram_chars";

    /// <summary>The single-valued names, as the manifest's <c>pipeline.quality</c> spells them.</summary>
    public static readonly IReadOnlyList<string> SCALAR_KEYS =
    [
        "min_words", "max_words", "min_mean_word_length", "max_mean_word_length", "max_symbol_to_word_ratio",
        "max_bullet_line_ratio", "max_ellipsis_line_ratio", "min_alphabetic_word_ratio", "max_duplicate_line_ratio", "max_duplicate_line_char_ratio"
    ];

    #endregion

    #region Functions

    /// <summary>How many values a name takes.</summary>
    /// <param name="key">The name.</param>
    /// <returns>1 for a scalar, 3 or 6 for a vector, 0 for a name the controller does not know.</returns>
    public static int ValueCount(string key)
    {
        if (key == TOP_NGRAM_CHARS)
            return 3;
        if (key == DUPLICATE_NGRAM_CHARS)
            return 6;
        return SCALAR_KEYS.Contains(key, StringComparer.Ordinal) ? 1 : 0;
    }

    /// <summary>Sets one named threshold.</summary>
    /// <param name="rules">The rules to start from.</param>
    /// <param name="key">The name.</param>
    /// <param name="values">Its values (one, or the vector).</param>
    /// <returns>The rules with the threshold set.</returns>
    /// <exception cref="ArgumentException">The name is unknown, the count is wrong, a value is negative or not finite, or a word count is not a whole number.</exception>
    public static QualityRules Apply(QualityRules rules, string key, IReadOnlyList<double> values)
    {
        var count = ValueCount(key);
        if (count == 0)
            throw new ArgumentException($"'{key}' is not a quality threshold this controller has.", nameof(key));
        if (values.Count != count)
            throw new ArgumentException($"'{key}' takes {count} value(s), not {values.Count}.", nameof(values));
        if (values.Any(value => !double.IsFinite(value) || value < 0))
            throw new ArgumentException($"'{key}' must be finite and 0 or more.", nameof(values));

        var value = values[0];
        return key switch
        {
            "min_words" => rules with { MinWords = WholeNumber(key, value) },
            "max_words" => rules with { MaxWords = WholeNumber(key, value) },
            "min_mean_word_length" => rules with { MinMeanWordLength = value },
            "max_mean_word_length" => rules with { MaxMeanWordLength = value },
            "max_symbol_to_word_ratio" => rules with { MaxSymbolToWordRatio = value },
            "max_bullet_line_ratio" => rules with { MaxBulletLineRatio = value },
            "max_ellipsis_line_ratio" => rules with { MaxEllipsisLineRatio = value },
            "min_alphabetic_word_ratio" => rules with { MinAlphabeticWordRatio = value },
            "max_duplicate_line_ratio" => rules with { MaxDuplicateLineRatio = value },
            "max_duplicate_line_char_ratio" => rules with { MaxDuplicateLineCharRatio = value },
            TOP_NGRAM_CHARS => rules with { MaxTopNgramCharRatio = [.. values] },
            _ => rules with { MaxDuplicateNgramCharRatio = [.. values] }
        };
    }

    private static int WholeNumber(string key, double value)
    {
        if (value != Math.Floor(value) || value > int.MaxValue)
            throw new ArgumentException($"'{key}' is a number of words: a whole number.", nameof(value));

        return (int)value;
    }

    #endregion
}
