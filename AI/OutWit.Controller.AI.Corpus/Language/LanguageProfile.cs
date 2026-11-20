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
    private readonly ulong[] m_packedKeys;
    private readonly int[] m_packedRanks;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a profile.
    /// </summary>
    /// <param name="code">The language code (ISO 639-3, as Common Crawl labels documents).</param>
    /// <param name="grams">The n-grams in frequency order, most frequent first.</param>
    public LanguageProfile(string code, IReadOnlyList<string> grams)
    {
        // Sized by the n-grams that are there (a reader holds their number to its limits first),
        // never by a count a file states.
        Code = code;
        Grams = grams;
        m_ranks = new Dictionary<string, int>(grams.Count, StringComparer.Ordinal);
        var packed = new List<(ulong Key, int Rank)>(grams.Count);
        for (var i = 0; i < grams.Count; i++)
        {
            m_ranks.TryAdd(grams[i], i);
            if (grams[i].Length is > 0 and <= LanguageNgrams.MAX_N)
                packed.Add((LanguageNgrams.Pack(grams[i]), i));
        }

        // The packed n-grams are the pack author's choice, and a table keyed by them can be made
        // to put them all in one chain: they are sorted instead, the first rank of a key kept.
        packed.Sort();
        var keys = new List<ulong>(packed.Count);
        var ranks = new List<int>(packed.Count);
        foreach (var (key, rank) in packed)
        {
            if (keys.Count > 0 && keys[^1] == key)
                continue;

            keys.Add(key);
            ranks.Add(rank);
        }

        m_packedKeys = keys.ToArray();
        m_packedRanks = ranks.ToArray();
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

    /// <summary>The rank of a packed n-gram (<see cref="LanguageNgrams.Pack(ReadOnlySpan{char})"/>), or -1 when absent.</summary>
    /// <param name="key">The packed n-gram.</param>
    /// <returns>The zero-based rank, or -1.</returns>
    public int RankOf(ulong key)
    {
        var at = Array.BinarySearch(m_packedKeys, key);
        return at >= 0 ? m_packedRanks[at] : -1;
    }

    #endregion

    #region Properties

    /// <summary>The language code.</summary>
    public string Code { get; }

    /// <summary>The n-grams in frequency order.</summary>
    public IReadOnlyList<string> Grams { get; }

    #endregion
}
