using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Turns a pipeline's quality thresholds into the filter's rule set: the language's rules (the
/// language model's rule data) are the base, a non-zero threshold in the pipeline overrides the
/// base, and the pipeline's switches turn optional rules on.
/// </summary>
public static class ShardQualityRules
{
    #region Functions

    /// <summary>Rules for one document.</summary>
    /// <param name="languageRules">The rules of the document's language (<see cref="Language.LanguageRuleSet.For"/>).</param>
    /// <param name="overrides">The pipeline's thresholds and rule switches (null = the defaults).</param>
    /// <returns>The rules.</returns>
    public static QualityRules Resolve(QualityRules languageRules, CorpusQualityData? overrides)
    {
        var rules = languageRules;
        if (overrides == null)
            return rules;

        return rules with
        {
            MinWords = overrides.MinWords > 0 ? overrides.MinWords : rules.MinWords,
            MaxWords = overrides.MaxWords > 0 ? overrides.MaxWords : rules.MaxWords,
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
