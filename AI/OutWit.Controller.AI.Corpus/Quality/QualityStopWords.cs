using System.Collections.Frozen;

namespace OutWit.Controller.AI.Corpus.Quality;

/// <summary>
/// The stop words of the quality filter's stop-word rule, per language (ISO 639-3): Gopher's
/// eight for English (Rae et al. 2021, appendix A.1.1), and for the other languages of the
/// committed language model the lists FineWeb-2 tuned per language (huggingface/fineweb-2,
/// <c>configs/{language}_{script}.yml</c>, Apache-2.0). A document must hold two distinct ones.
/// On Common Crawl the rule mostly catches pages in a language the identifier does not know
/// (Finnish labelled Dutch, Indonesian labelled Turkish, Arabic labelled Persian, ...), which
/// carry none of the labelled language's function words.
/// </summary>
public static class QualityStopWords
{
    #region Constants

    /// <summary>Fewest distinct stop words a document must hold (Gopher; FineWeb-2's <c>min_stop_words=2</c>).</summary>
    public const int MIN_STOP_WORDS = 2;

    private static readonly FrozenDictionary<string, FrozenSet<string>> TABLE = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["eng"] = ["the", "be", "to", "of", "and", "that", "have", "with"], // Gopher
        ["ces"] = ["v", "a", "se", "na", "je", "z", "s", "do"], // ces_Latn
        ["deu"] = ["der", "und", "die", "in", "von", "im", "den", "des", "mit", "das", "er", "dem", "als", "wurde", "für"], // deu_Latn
        ["fas"] = ["در", "و", "،", "به", "از", "که", "است", "این", "را", "با", "یک"], // fas_Arab
        ["fra"] = ["de", "la", "le", "et", "à", "en", "l'", "des", "du", "les", "est", "d'", "un", "une", "il", "dans", "par", "au"], // fra_Latn
        ["ita"] = ["di", "e", "il", "la", "in", "a", "del", "che", "è", "della", "un", "per", "nel", "l'", "con", "da", "i", "una"], // ita_Latn
        ["jpn"] = ["の", "に", "を", "は", "た", "て", "が", "と", "で", "年", "し", "・", "月", "れ", "さ"], // jpn_Jpan
        ["nld"] = ["de", "van", "in", "het", "een", "en", "is", "op", "voor", "met", "door", "zijn", "werd", "hij", "uit"], // nld_Latn
        ["pol"] = ["w", "i", "na", "z", "do", "się", "roku", "od", "a", "jest", "przez", "po", "jako", "oraz", "o", "został", "był", "nie"], // pol_Latn
        ["por"] = ["de", "a", "e", "o", "em", "do", "da", "que", "um", "no", "uma", "com", "para", "na", "é", "foi"], // por_Latn
        ["rus"] = ["в", "и", "на", "с", "года", "по", "году", "из", "был", "к", "не", "от", "что", "за", "для", "его", "как", "а", "он", "также", "до", "после"], // rus_Cyrl
        ["spa"] = ["de", "la", "en", "el", "y", "a", "que", "del", "los", "se", "por", "un", "con", "una", "las", "su"], // spa_Latn
        ["tur"] = ["ve", "bir", "olarak", "bu", "ile", "için", "olan", "da", "de", "tarafından", "yılında", "sonra", "en", "daha", "ilk", "the"], // tur_Latn
        ["ukr"] = ["у", "в", "на", "і", "з", "та", "до", "року", "за", "що"], // ukr_Cyrl
        ["vie"] = ["là", "và", "của", "một", "được", "trong", "năm", "các", "có", "loài", "này", "đã", "người"], // vie_Latn
        ["zho"] = ["的", "年", "在", "月", "是", "和", "日", "了", "於", "·", "為", "有", "被", "人", "中", "为", "他", "與", "後", "也", "而", "由"], // cmn_Hani
        ["cmn"] = ["的", "年", "在", "月", "是", "和", "日", "了", "於", "·", "為", "有", "被", "人", "中", "为", "他", "與", "後", "也", "而", "由"] // cmn_Hani
    }.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenSet(StringComparer.Ordinal), StringComparer.Ordinal);


    #endregion

    #region Functions

    /// <summary>The stop words of a language.</summary>
    /// <param name="languageCode">An ISO 639-3 code.</param>
    /// <param name="stopWords">The words, when the language has a list.</param>
    /// <returns>True when it has one; a language without a list is not checked.</returns>
    public static bool TryGet(string languageCode, out IReadOnlySet<string> stopWords)
    {
        if (TABLE.TryGetValue(languageCode, out var set))
        {
            stopWords = set;
            return true;
        }

        stopWords = FrozenSet<string>.Empty;
        return false;
    }

    #endregion

    #region Properties

    /// <summary>The languages with a list.</summary>
    public static IEnumerable<string> Languages => TABLE.Keys;

    #endregion
}
