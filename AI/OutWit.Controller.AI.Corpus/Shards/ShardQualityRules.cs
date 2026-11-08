using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Turns a pipeline's quality thresholds into the filter's rule set: the language picks the
/// base (spaced or unspaced script), a non-zero threshold in the pipeline overrides the base.
/// </summary>
public static class ShardQualityRules
{
    #region Functions

    /// <summary>Rules for one document.</summary>
    /// <param name="languageCode">The document's language ("" when the stage is off).</param>
    /// <param name="overrides">The pipeline's thresholds (null = the defaults).</param>
    /// <returns>The rules.</returns>
    public static QualityRules Resolve(string languageCode, CorpusQualityData? overrides)
    {
        var rules = QualityRules.ForLanguage(languageCode);
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
            MaxDuplicateLineCharRatio = overrides.MaxDuplicateLineCharRatio > 0 ? overrides.MaxDuplicateLineCharRatio : rules.MaxDuplicateLineCharRatio
        };
    }

    #endregion
}
