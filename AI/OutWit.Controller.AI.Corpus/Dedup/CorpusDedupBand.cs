using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// The merge's pass over one band: every document is held against the lowest document that has
/// its band key, and joins that document's cluster when the two are similar enough. The documents
/// of a key are found by sorting the band's keys, not by a table keyed with them: a node wrote the
/// signatures and so chose the keys, and a sort costs the same whatever the keys are. Both halves
/// of the pass read the token at every document, so a job that was stopped is left at the next
/// document and not at the next band. Twelve bytes a document, used again for every band.
/// </summary>
internal sealed class CorpusDedupBand
{
    #region Fields

    private readonly ulong[] m_keys;
    private readonly int[] m_order;

    #endregion

    #region Constructors

    /// <summary>Prepares the pass for a merge.</summary>
    /// <param name="documents">The documents of the merge.</param>
    public CorpusDedupBand(int documents)
    {
        m_keys = new ulong[documents];
        m_order = new int[documents];
    }

    #endregion

    #region Functions

    /// <summary>The first half of the pass: reads every document's key of the band and sorts the documents by it.</summary>
    /// <param name="documents">The documents of the merge; as many as the pass was prepared for.</param>
    /// <param name="band">The band, from 0.</param>
    /// <param name="rows">Rows per band: the signature length over the band count.</param>
    /// <param name="cancellationToken">Stops the reading at the next document.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public void Read(CorpusDedupDocuments documents, int band, int rows, CancellationToken cancellationToken)
    {
        for (var document = 0; document < m_keys.Length; document++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            m_keys[document] = MinHashSigner.BandKey(documents.Signature(document), band, rows);
            m_order[document] = document;
        }

        // By key: the documents of one key stand together, in no stated order.
        Array.Sort(m_keys, m_order);
    }

    /// <summary>
    /// The second half of the pass: every document of a key is held against the lowest document
    /// of that key, and the two become one cluster when they are similar enough.
    /// </summary>
    /// <param name="documents">The documents of the merge, as they were read.</param>
    /// <param name="clusters">The clusters found so far; receives the pairs of this band.</param>
    /// <param name="threshold">The Jaccard estimate a pair needs to be one cluster.</param>
    /// <param name="cancellationToken">Stops the joining at the next document.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public void Join(CorpusDedupDocuments documents, CorpusDedupClusters clusters, double threshold, CancellationToken cancellationToken)
    {
        var start = 0;
        while (start < m_keys.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The documents of one key, and the lowest of them: the one the others are held against.
            var end = start + 1;
            var first = m_order[start];
            while (end < m_keys.Length && m_keys[end] == m_keys[start])
            {
                first = Math.Min(first, m_order[end]);
                end++;
            }

            for (var at = start; at < end && end - start > 1; at++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var document = m_order[at];
                if (document == first || clusters.Lowest(first) == clusters.Lowest(document))
                    continue;

                if (MinHashSigner.Similarity(documents.Signature(first), documents.Signature(document)) >= threshold)
                    clusters.Join(first, document);
            }

            start = end;
        }
    }

    #endregion
}
