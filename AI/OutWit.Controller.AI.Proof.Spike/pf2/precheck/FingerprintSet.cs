namespace OutWit.Controller.AI.Proof.Spike.Precheck;

/// <summary>
/// A set of 64-bit fingerprints in many small open-addressing tables, so that growing one table
/// never needs a second copy of the whole set.
/// </summary>
public sealed class FingerprintSet
{
    #region Constants

    private const int SHARD_BITS = 12;

    private const int INITIAL_CAPACITY = 1024;

    #endregion

    #region Fields

    private readonly ulong[][] m_shards = new ulong[1 << SHARD_BITS][];

    private readonly int[] m_counts = new int[1 << SHARD_BITS];

    #endregion

    #region Constructors

    /// <summary>
    /// Creates an empty set.
    /// </summary>
    public FingerprintSet()
    {
        for (var i = 0; i < m_shards.Length; i++)
            m_shards[i] = new ulong[INITIAL_CAPACITY];
    }

    #endregion

    #region Functions

    /// <summary>
    /// Adds a fingerprint.
    /// </summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>False when it was there already.</returns>
    public bool Add(ulong fingerprint)
    {
        if (fingerprint == 0)
            fingerprint = 1;
        var shard = (int)(fingerprint >> (64 - SHARD_BITS));
        var table = m_shards[shard];
        if ((m_counts[shard] + 1) * 10L > table.Length * 7L)
        {
            table = Grow(shard);
        }
        var mask = table.Length - 1;
        var slot = (int)(fingerprint & (ulong)mask);
        while (true)
        {
            var value = table[slot];
            if (value == 0)
            {
                table[slot] = fingerprint;
                m_counts[shard]++;
                Count++;
                return true;
            }
            if (value == fingerprint)
                return false;
            slot = (slot + 1) & mask;
        }
    }

    private ulong[] Grow(int shard)
    {
        var old = m_shards[shard];
        var table = new ulong[old.Length * 2];
        var mask = table.Length - 1;
        foreach (var value in old)
        {
            if (value == 0)
                continue;
            var slot = (int)(value & (ulong)mask);
            while (table[slot] != 0)
                slot = (slot + 1) & mask;
            table[slot] = value;
        }
        m_shards[shard] = table;
        return table;
    }

    #endregion

    #region Properties

    /// <summary>
    /// How many fingerprints the set holds.
    /// </summary>
    public long Count { get; private set; }

    /// <summary>
    /// The bytes the tables take.
    /// </summary>
    public long Bytes
    {
        get
        {
            long total = 0;
            foreach (var table in m_shards)
                total += table.LongLength * sizeof(ulong);
            return total;
        }
    }

    #endregion
}
