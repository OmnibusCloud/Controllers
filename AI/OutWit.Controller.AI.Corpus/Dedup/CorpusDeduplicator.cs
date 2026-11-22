using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// The host-side merge: near-duplicate documents across every shard of a run, found by
/// locality-sensitive hashing over the MinHash signatures the nodes produced. Documents are
/// numbered in (shard, ordinal) order; for each band, a document is compared with the lowest
/// document that has its band key and joins its cluster when the Jaccard estimate reaches the
/// threshold. The cluster's representative is its lowest document, so the kept set is a
/// function of the signatures alone — the same run gives the same masks on any host.
/// <para>
/// A node wrote the signatures, so nothing here costs more for one set of signatures than for
/// another: a band's keys are sorted (<see cref="CorpusDedupBand"/>), the clusters are gathered
/// in arrays, and a merge holds a bounded number of documents whatever the length of their
/// signatures. Beside the signatures a document costs the merge under 48 bytes.
/// </para>
/// </summary>
public sealed class CorpusDeduplicator
{
    #region Constants

    /// <summary>The Jaccard estimate a pair needs to be one cluster.</summary>
    public const double DEFAULT_THRESHOLD = 0.8;

    /// <summary>LSH bands over the 128-hash signature: 16 bands of 8 rows catch a 0.8-similar pair with probability 0.97 and a 0.5-similar one with 0.06 (the comparison then rejects it).</summary>
    public const int DEFAULT_BANDS = MinHashSigner.BANDS;

    /// <summary>Most documents one merge holds, whatever the length of their signatures: the merge's own tables grow with the documents, not with the values. With signatures longer than 128 hashes (about 1 KB each), as many as fit <see cref="MAX_SIGNATURE_VALUES"/>.</summary>
    public const int MAX_DOCUMENTS = 4_000_000;

    /// <summary>Most signature values one merge holds in memory (8 bytes each): 512 million, about 4 GB.</summary>
    public const long MAX_SIGNATURE_VALUES = (long)MAX_DOCUMENTS * MinHashSigner.HASHES;

    #endregion

    #region Fields

    private readonly double m_threshold;
    private readonly int m_bands;

    #endregion

    #region Constructors

    /// <summary>Creates a merge.</summary>
    /// <param name="threshold">The Jaccard estimate a pair needs (0 = the default, 0.8).</param>
    /// <param name="bands">LSH bands; must divide the signature length (0 = the default, 16).</param>
    public CorpusDeduplicator(double threshold = 0, int bands = 0)
    {
        m_threshold = threshold > 0 ? Math.Min(threshold, 1) : DEFAULT_THRESHOLD;
        m_bands = bands > 0 ? bands : DEFAULT_BANDS;
    }

    #endregion

    #region Functions

    /// <summary>Runs the merge over the shards' signatures.</summary>
    /// <param name="shards">The shards, any order; sorted by index inside.</param>
    /// <param name="cancellationToken">Stops the merge at the next document.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="InvalidOperationException">More documents than one merge holds.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="ArgumentException">The shards' signatures differ in length, or the bands do not divide it.</exception>
    public CorpusDedupResult Run(IReadOnlyList<CorpusDedupShard> shards, CancellationToken cancellationToken = default)
    {
        var ordered = shards.OrderBy(shard => shard.ShardIndex).ToList();
        var hashes = ordered.Count > 0 ? ordered[0].Hashes : MinHashSigner.HASHES;
        if (ordered.Any(shard => shard.Hashes != hashes))
            throw new ArgumentException("the shards' signatures have one length", nameof(shards));

        var total = ordered.Sum(shard => (long)shard.Documents);
        var capacity = Capacity(hashes);
        if (total > capacity)
            throw new InvalidOperationException($"{total} documents exceed the merge's capacity of {capacity}.");

        if (total > 0 && (hashes % m_bands != 0))
            throw new ArgumentException("bands must divide the signature length");

        var rows = hashes / m_bands;

        // Before the merge's own tables are made: a job that was stopped needs none.
        cancellationToken.ThrowIfCancellationRequested();

        var documents = new CorpusDedupDocuments(ordered);
        var sets = new CorpusDedupClusters(documents.Count);

        var pass = new CorpusDedupBand(documents.Count);
        for (var band = 0; band < m_bands; band++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            pass.Read(documents, band, rows, cancellationToken);
            pass.Join(documents, sets, m_threshold, cancellationToken);
        }

        // A cluster is known by its lowest document and the documents are walked from the lowest:
        // a cluster is met at the document that is kept, and its members come in order.
        var sizes = new int[documents.Count];
        for (var document = 0; document < sizes.Length; document++)
            sizes[sets.Lowest(document)]++;

        var keep = new bool[documents.Count];
        var clusterOf = new int[documents.Count];
        var clusters = new List<List<CorpusDocumentRef>>();
        for (var document = 0; document < keep.Length; document++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var lowest = sets.Lowest(document);
            if (lowest == document)
            {
                keep[document] = true;
                if (sizes[document] > 1)
                {
                    clusterOf[document] = clusters.Count;
                    clusters.Add(new List<CorpusDocumentRef>(sizes[document]));
                }
            }

            if (sizes[lowest] > 1)
                clusters[clusterOf[lowest]].Add(documents.Ref(document));
        }

        clusters.Sort((a, b) => a[0].CompareTo(b[0]));

        var shardResults = new List<CorpusDedupShardResult>(ordered.Count);
        var offset = 0;
        foreach (var shard in ordered)
        {
            var mask = new bool[shard.Documents];
            Array.Copy(keep, offset, mask, 0, mask.Length);
            shardResults.Add(new CorpusDedupShardResult(shard.ShardIndex, shard.SignaturesBlobId, mask));
            offset += mask.Length;
        }

        return new CorpusDedupResult(m_threshold, m_bands, (int)total, shardResults, clusters);
    }

    /// <summary>
    /// The most documents one merge holds with signatures of the given length: counted in
    /// documents and in signature values, and the smaller of the two.
    /// </summary>
    /// <param name="hashes">Hashes per signature.</param>
    /// <returns><see cref="MAX_DOCUMENTS"/> up to 128 hashes; as many as fit <see cref="MAX_SIGNATURE_VALUES"/> above that.</returns>
    public static long Capacity(int hashes)
    {
        return Math.Min(MAX_DOCUMENTS, MAX_SIGNATURE_VALUES / Math.Max(1, hashes));
    }

    #endregion
}
