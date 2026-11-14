namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>
/// The language model the controller is proven with. A model is a job's input, not part of the
/// controller: a lab trains one with <c>corpus-assets</c> or uses this one, and pins it by hash
/// in the manifest. This is the 16-language model trained from two WET files of
/// CC-MAIN-2026-43, committed with the controller's tests and used by every fleet proof; the
/// tests hold these values to the file.
/// </summary>
public static class CorpusReferenceLanguageModel
{
    #region Constants

    /// <summary>The model file format the controller reads: its first string.</summary>
    public const string FORMAT = "WITLID1";

    /// <summary>The identification method behind the format.</summary>
    public const string METHOD = "character n-gram profiles, n = 1 to 4, ranked distance (Cavnar-Trenkle)";

    /// <summary>Lower-case hex SHA-256 of the reference model's bytes.</summary>
    public const string SHA256 = "9670e262a8453661e47f0a4e838e0970fff561e20b3865b383227b01ba25558f";

    /// <summary>The crawl the reference model was trained from.</summary>
    public const string TRAINED_FROM = "CC-MAIN-2026-43";

    /// <summary>The reference model's languages (ISO 639-3), in code order: what <c>target_languages</c> may name with it.</summary>
    public static readonly IReadOnlyList<string> LANGUAGES =
    [
        "ces", "deu", "eng", "fas", "fra", "ita", "jpn", "nld", "pol", "por", "rus", "spa", "tur", "ukr", "vie", "zho"
    ];

    #endregion
}
