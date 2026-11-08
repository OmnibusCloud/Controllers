namespace OutWit.Controller.AI.Corpus.Language;

/// <summary>
/// The ranked character n-gram profile of one language: its most frequent n-grams (1 to 4
/// characters over normalized text) in frequency order, the classic Cavnar–Trenkle shape. A
/// text's profile is compared with each language's by the sum of rank displacements; the
/// smallest distance wins. Small, fast and bit-for-bit reproducible — no floating point.
/// </summary>
public sealed class LanguageProfile
{
    #region Fields

    private readonly Dictionary<string, int> m_ranks;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a profile.
    /// </summary>
    /// <param name="code">The language code (ISO 639-3, as Common Crawl labels documents).</param>
    /// <param name="grams">The n-grams in frequency order, most frequent first.</param>
    public LanguageProfile(string code, IReadOnlyList<string> grams)
    {
        Code = code;
        Grams = grams;
        m_ranks = new Dictionary<string, int>(grams.Count, StringComparer.Ordinal);
        for (var i = 0; i < grams.Count; i++)
            m_ranks.TryAdd(grams[i], i);
    }

    #endregion

    #region Functions

    /// <summary>
    /// The rank of an n-gram in this profile, or -1 when absent.
    /// </summary>
    /// <param name="gram">The n-gram.</param>
    /// <returns>The zero-based rank, or -1.</returns>
    public int RankOf(string gram)
    {
        return m_ranks.TryGetValue(gram, out var rank) ? rank : -1;
    }

    #endregion

    #region Properties

    /// <summary>The language code.</summary>
    public string Code { get; }

    /// <summary>The n-grams in frequency order.</summary>
    public IReadOnlyList<string> Grams { get; }

    #endregion
}
