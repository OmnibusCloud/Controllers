using System.Collections.Frozen;
using OutWit.Controller.AI.Corpus.Quality;

namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// The rule data of a model's languages, one <see cref="LanguageRules"/> per code, and the quality
/// rules a document of each language is judged by. A language pack (WITLID2) carries its own;
/// a WITLID1 model carries none and gets <see cref="Witlid1"/>, the per-language tables of
/// Corpus 0.1.14 frozen as data, so its products do not move. A code the set does not hold —
/// "" when the language stage is off — is judged by <see cref="QualityRules.Spaced"/>.
/// </summary>
public sealed class LanguageRuleSet
{
    #region Constants

    private const string WITLID1_RESOURCE = "OutWit.Controller.AI.Corpus.Language.witlid1-rules.json";

    #endregion

    #region Fields

    private static readonly Lazy<LanguageRuleSet> m_witlid1 = new(LoadWitlid1);

    private readonly FrozenDictionary<string, LanguageRules> m_languages;

    #endregion

    #region Constructors

    /// <summary>Creates a rule set.</summary>
    /// <param name="languages">One rule data per language code.</param>
    /// <exception cref="ArgumentException">A code appears twice.</exception>
    public LanguageRuleSet(IEnumerable<LanguageRules> languages)
    {
        Languages = languages.OrderBy(language => language.Code, StringComparer.Ordinal).ToList();
        if (Languages.Select(language => language.Code).Distinct(StringComparer.Ordinal).Count() != Languages.Count)
            throw new ArgumentException("A language has rule data twice.", nameof(languages));

        m_languages = Languages.ToFrozenDictionary(language => language.Code, StringComparer.Ordinal);
    }

    #endregion

    #region Functions

    /// <summary>The rule data of a language.</summary>
    /// <param name="code">The language code.</param>
    /// <param name="rules">The rule data, when the set holds the code.</param>
    /// <returns>True when it does.</returns>
    public bool TryGet(string code, out LanguageRules rules)
    {
        return m_languages.TryGetValue(code, out rules!);
    }

    /// <summary>The quality rules of a document in a language: the language's, or <see cref="QualityRules.Spaced"/> for a code the set does not hold.</summary>
    /// <param name="code">The language code ("" when the language stage is off).</param>
    /// <returns>The rules (the same instance for the same code).</returns>
    public QualityRules For(string code)
    {
        return m_languages.TryGetValue(code, out var rules) ? rules.Quality : QualityRules.Spaced;
    }

    private static LanguageRuleSet LoadWitlid1()
    {
        using var stream = typeof(LanguageRuleSet).Assembly.GetManifestResourceStream(WITLID1_RESOURCE)
                           ?? throw new InvalidOperationException($"The controller is missing its resource {WITLID1_RESOURCE}.");
        using var reader = new StreamReader(stream);
        return new LanguageRuleSet(LanguageRulesJson.ReadSet(reader.ReadToEnd()));
    }

    #endregion

    #region Properties

    /// <summary>The rule data a WITLID1 model gets: Corpus 0.1.14's per-language tables (stop words for 17 codes, the scripts written without spaces, FineWeb-2's Japanese thresholds), frozen.</summary>
    public static LanguageRuleSet Witlid1 => m_witlid1.Value;

    /// <summary>The languages' rule data in code order.</summary>
    public IReadOnlyList<LanguageRules> Languages { get; }

    #endregion
}
