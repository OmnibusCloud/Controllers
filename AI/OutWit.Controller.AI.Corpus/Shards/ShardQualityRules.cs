using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Turns a pipeline's quality thresholds into the filter's rule set: the language's rules (the
/// language model's rule data) are the base, a non-zero threshold in the pipeline overrides the
/// base, and the pipeline's switches turn optional rules on.
/// <para>
/// The most words a document may have is held to the controller's own, whoever set it: a
/// document within it is judged in full, and judging one costs memory in proportion to its words
/// (the words themselves, an index of them, a table per n-gram length). With the length opened by
/// a manifest or a language pack, one record within the record cap (8 MiB of text, about 1.4
/// million words) raised the heap by 658 MiB where a shard is budgeted 256 MiB. A manifest or a
/// pack may lower the length; past <see cref="MAX_WORDS"/> words (<see cref="MAX_LETTERS"/>
/// letters for a script without word spacing) a document fails <c>max_words</c> by a count of
/// its words, which costs no memory.
/// </para>
/// </summary>
public static class ShardQualityRules
{
    #region Constants

    /// <summary>The most words of a spaced script a document may have, whatever the thresholds say: Gopher's own, the filter's default.</summary>
    public const int MAX_WORDS = 100_000;

    /// <summary>The most letters of a script without word spacing a document may have, whatever the thresholds say: the filter's default for such scripts.</summary>
    public const int MAX_LETTERS = 1_000_000;

    #endregion

    #region Functions

    /// <summary>Rules for one document.</summary>
    /// <param name="languageRules">The rules of the document's language (<see cref="Language.LanguageRuleSet.For"/>).</param>
    /// <param name="overrides">The pipeline's thresholds and rule switches (null = the defaults).</param>
    /// <returns>The rules.</returns>
    public static QualityRules Resolve(QualityRules languageRules, CorpusQualityData? overrides)
    {
        var rules = languageRules;
        var ceiling = rules.CountsLetters ? MAX_LETTERS : MAX_WORDS;
        if (overrides == null)
            return rules.MaxWords > ceiling ? rules with { MaxWords = ceiling } : rules;

        return rules with
        {
            MinWords = overrides.MinWords > 0 ? overrides.MinWords : rules.MinWords,
            MaxWords = Math.Min(overrides.MaxWords > 0 ? overrides.MaxWords : rules.MaxWords, ceiling),
            MinMeanWordLength = overrides.MinMeanWordLength > 0 ? overrides.MinMeanWordLength : rules.MinMeanWordLength,
            MaxMeanWordLength = overrides.MaxMeanWordLength > 0 ? overrides.MaxMeanWordLength : rules.MaxMeanWordLength,
            MaxSymbolToWordRatio = overrides.MaxSymbolToWordRatio > 0 ? overrides.MaxSymbolToWordRatio : rules.MaxSymbolToWordRatio,
            MaxBulletLineRatio = overrides.MaxBulletLineRatio > 0 ? overrides.MaxBulletLineRatio : rules.MaxBulletLineRatio,
            MaxEllipsisLineRatio = overrides.MaxEllipsisLineRatio > 0 ? overrides.MaxEllipsisLineRatio : rules.MaxEllipsisLineRatio,
            MinAlphabeticWordRatio = overrides.MinAlphabeticWordRatio > 0 ? overrides.MinAlphabeticWordRatio : rules.MinAlphabeticWordRatio,
            MaxDuplicateLineRatio = overrides.MaxDuplicateLineRatio > 0 ? overrides.MaxDuplicateLineRatio : rules.MaxDuplicateLineRatio,
            MaxDuplicateLineCharRatio = overrides.MaxDuplicateLineCharRatio > 0 ? overrides.MaxDuplicateLineCharRatio : rules.MaxDuplicateLineCharRatio,
            FilterCurlyBracket = overrides.FilterCurlyBracket
        };
    }

    #endregion
}
