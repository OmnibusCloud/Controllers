using OutWit.Controller.AI.Corpus.Language;

namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>
/// The language pack the controller is proven with. A pack is a job's input, not part of the
/// controller: a lab builds one with <c>corpus-assets build-language-pack</c> or uses this one,
/// and pins it by hash in the manifest. This is the 16-language pack built from CC-MAIN-2026-43
/// WET files (the same 16 profiles as the 0.1.x reference model, 23 more languages it knows and
/// calls unsupported, limits calibrated at a recall of 0.999), committed with the controller's
/// tests; the tests hold these values to the file. The 0.1.x model (WITLID1) is still read and
/// still gives the products it gave.
/// </summary>
public static class CorpusReferenceLanguageModel
{
    #region Constants

    /// <summary>The format of the reference pack: its first string.</summary>
    public const string FORMAT = LanguageModelFormat.WITLID2;

    /// <summary>The identification method behind the format.</summary>
    public const string METHOD = "character n-gram profiles, n = 1 to 4, ranked distance (Cavnar-Trenkle); unknown when the nearest profile is a language the pack does not support or the distance is past the language's calibrated limit";

    /// <summary>The reference pack's name.</summary>
    public const string NAME = "cc-2026-43-16";

    /// <summary>Lower-case hex SHA-256 of the reference pack's bytes.</summary>
    public const string SHA256 = "74c6c348b897998ca947e286cd673f41d20e9efc3c242befc575a852d8d8dbd3";

    /// <summary>Lower-case hex SHA-256 of the 0.1.x reference model (WITLID1), which a manifest may still pin.</summary>
    public const string WITLID1_SHA256 = "9670e262a8453661e47f0a4e838e0970fff561e20b3865b383227b01ba25558f";

    /// <summary>The crawl the reference pack was built from.</summary>
    public const string TRAINED_FROM = "CC-MAIN-2026-43";

    /// <summary>The reference pack's supported languages (ISO 639-3), in code order: what <c>target_languages</c> may name with it.</summary>
    public static readonly IReadOnlyList<string> LANGUAGES =
    [
        "ces", "deu", "eng", "fas", "fra", "ita", "jpn", "nld", "pol", "por", "rus", "spa", "tur", "ukr", "vie", "zho"
    ];

    /// <summary>The languages the reference pack knows and does not support: a document in one of them is unknown (<c>language_unsupported</c>), not mislabelled.</summary>
    public static readonly IReadOnlyList<string> KNOWN_LANGUAGES =
    [
        "ara", "aze", "ben", "bul", "cat", "dan", "ell", "est", "fin", "heb", "hrv", "hun", "ind", "kor", "lav", "lit", "nor", "ron", "slk", "slv", "srp", "swe", "tha"
    ];

    #endregion
}
