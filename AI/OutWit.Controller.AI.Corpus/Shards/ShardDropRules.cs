using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Every rule a shard's statistics can count a drop under, with the stage that applies it, in the
/// order a document meets them: what turns the drop histogram into a funnel. The extraction rules
/// count records (and PDFs) that gave no document; every other rule counts documents, so the
/// documents extracted less the language, line and quality drops are the documents out.
/// </summary>
public static class ShardDropRules
{
    #region Constants

    /// <summary>Extraction: a record that gives no document (an HTTP response that is not a page, a PDF that gives no text).</summary>
    public const string STAGE_EXTRACT = "extract";

    /// <summary>The language stage: too little text, a language the pack does not support, a language the run does not keep.</summary>
    public const string STAGE_LANGUAGE = "language";

    /// <summary>The line stage: C4's curly-bracket rule, and a document left with no line of prose.</summary>
    public const string STAGE_LINES = "lines";

    /// <summary>The quality filter: the Gopher rules, the stop words, lorem ipsum.</summary>
    public const string STAGE_QUALITY = "quality";

    /// <summary>The stages, in the order a document meets them.</summary>
    public static readonly IReadOnlyList<string> STAGES = [STAGE_EXTRACT, STAGE_LANGUAGE, STAGE_LINES, STAGE_QUALITY];

    /// <summary>Every rule with its stage, in the order a document meets them.</summary>
    public static readonly IReadOnlyList<(string Rule, string Stage)> ALL = Build();

    private static readonly IReadOnlyDictionary<string, string> STAGE_BY_RULE = ALL.ToDictionary(pair => pair.Rule, pair => pair.Stage, StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>The stage that applies a rule; a rule this controller does not know (a newer one's) counts with the quality filter.</summary>
    /// <param name="rule">The rule's name.</param>
    /// <returns>One of <see cref="STAGES"/>.</returns>
    public static string StageOf(string rule)
    {
        return STAGE_BY_RULE.TryGetValue(rule, out var stage) ? stage : STAGE_QUALITY;
    }

    private static List<(string Rule, string Stage)> Build()
    {
        var rules = new List<(string, string)>
        {
            (ShardProcessor.DROP_HTTP_UNPARSABLE, STAGE_EXTRACT),
            (ShardProcessor.DROP_HTTP_STATUS, STAGE_EXTRACT),
            (ShardProcessor.DROP_NOT_HTML, STAGE_EXTRACT),
            (ShardProcessor.DROP_HTML_REFUSED, STAGE_EXTRACT),
            (ShardProcessor.DROP_EMPTY_TEXT, STAGE_EXTRACT)
        };

        rules.AddRange(ShardPdfStage.DROPS.Select(rule => (rule, STAGE_EXTRACT)));
        rules.Add((ShardProcessor.DROP_LANGUAGE_UNKNOWN, STAGE_LANGUAGE));
        rules.Add((ShardProcessor.DROP_LANGUAGE_UNSUPPORTED, STAGE_LANGUAGE));
        rules.Add((ShardProcessor.DROP_LANGUAGE_NOT_TARGET, STAGE_LANGUAGE));
        rules.Add((ShardProcessor.DROP_CURLY_BRACKET, STAGE_LINES));
        rules.Add((ShardProcessor.DROP_NO_PROSE_LINES, STAGE_LINES));
        rules.AddRange(QualityFilter.RULES.Select(rule => (rule, STAGE_QUALITY)));
        return rules;
    }

    #endregion
}
