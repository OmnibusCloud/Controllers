namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// The documents of one merge under the numbers the merge gives them: shard after shard in the
/// order given, a shard's documents in product order. A document costs two integers here beside
/// its signature, which stays in its shard.
/// </summary>
internal sealed class CorpusDedupDocuments
{
    #region Fields

    private readonly IReadOnlyList<CorpusDedupShard> m_shards;
    private readonly int[] m_shardOf;
    private readonly int[] m_ordinalOf;

    #endregion

    #region Constructors

    /// <summary>Numbers the documents of the shards.</summary>
    /// <param name="shards">The shards, in the order their documents are numbered; together no more documents than a merge holds.</param>
    public CorpusDedupDocuments(IReadOnlyList<CorpusDedupShard> shards)
    {
        m_shards = shards;

        var total = shards.Sum(shard => shard.Documents);
        m_shardOf = new int[total];
        m_ordinalOf = new int[total];

        var next = 0;
        for (var shard = 0; shard < shards.Count; shard++)
        {
            for (var ordinal = 0; ordinal < shards[shard].Documents; ordinal++)
            {
                m_shardOf[next] = shard;
                m_ordinalOf[next] = ordinal;
                next++;
            }
        }
    }

    #endregion

    #region Functions

    /// <summary>A document's signature.</summary>
    /// <param name="document">The document's number.</param>
    /// <returns>Its values, read where its shard holds them.</returns>
    public ReadOnlySpan<ulong> Signature(int document)
    {
        return m_shards[m_shardOf[document]].Signature(m_ordinalOf[document]);
    }

    /// <summary>A document as the merge's document names it.</summary>
    /// <param name="document">The document's number.</param>
    /// <returns>Its shard's index and its ordinal in that shard's product.</returns>
    public CorpusDocumentRef Ref(int document)
    {
        return new CorpusDocumentRef(m_shards[m_shardOf[document]].ShardIndex, m_ordinalOf[document]);
    }

    #endregion

    #region Properties

    /// <summary>The documents of the merge.</summary>
    public int Count => m_shardOf.Length;

    #endregion
}
