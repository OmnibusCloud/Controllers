using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// The host-side merge: near-duplicate documents across every shard of a run, found by
/// locality-sensitive hashing over the MinHash signatures the nodes produced. Documents are
/// numbered in (shard, ordinal) order; for each band, a document whose band key was seen
/// before is compared with the first document that had it and joins its cluster when the
/// Jaccard estimate reaches the threshold. The cluster's representative is its lowest
/// document, so the kept set is a function of the signatures alone — the same run gives the
/// same masks on any host.
/// </summary>
public sealed class CorpusDeduplicator
{
    #region Constants

    /// <summary>The Jaccard estimate a pair needs to be one cluster.</summary>
    public const double DEFAULT_THRESHOLD = 0.8;

    /// <summary>LSH bands over the 128-hash signature: 16 bands of 8 rows catch a 0.8-similar pair with probability 0.97 and a 0.5-similar one with 0.06 (the comparison then rejects it).</summary>
    public const int DEFAULT_BANDS = MinHashSigner.BANDS;

    /// <summary>Most documents one merge holds in memory with 128-hash signatures (about 1 KB each); with another hash count, as many as fit <see cref="MAX_SIGNATURE_VALUES"/>.</summary>
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
    /// <param name="cancellationToken">Stops between bands.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="InvalidOperationException">More documents than one merge holds.</exception>
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

        // Document number to its shard and its ordinal there.
        var shardOf = new int[total];
        var ordinalOf = new int[total];
        var next = 0;
        for (var shard = 0; shard < ordered.Count; shard++)
        {
            for (var ordinal = 0; ordinal < ordered[shard].Documents; ordinal++)
            {
                shardOf[next] = shard;
                ordinalOf[next] = ordinal;
                next++;
            }
        }

        var parent = new int[total];
        for (var i = 0; i < parent.Length; i++)
            parent[i] = i;

        var firstByKey = new Dictionary<ulong, int>();
        for (var band = 0; band < m_bands; band++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            firstByKey.Clear();

            for (var document = 0; document < parent.Length; document++)
            {
                var signature = ordered[shardOf[document]].Signature(ordinalOf[document]);
                var key = MinHashSigner.BandKey(signature, band, rows);
                if (!firstByKey.TryGetValue(key, out var first))
                {
                    firstByKey[key] = document;
                    continue;
                }

                if (Find(parent, first) == Find(parent, document))
                    continue;

                if (MinHashSigner.Similarity(ordered[shardOf[first]].Signature(ordinalOf[first]), signature) >= m_threshold)
                    Union(parent, first, document);
            }
        }

        var members = new Dictionary<int, List<int>>();
        for (var document = 0; document < parent.Length; document++)
        {
            var root = Find(parent, document);
            if (!members.TryGetValue(root, out var list))
                members[root] = list = [];
            list.Add(document);
        }

        var keep = new bool[total];
        var clusters = new List<IReadOnlyList<CorpusDocumentRef>>();
        foreach (var list in members.Values)
        {
            list.Sort();
            keep[list[0]] = true;
            if (list.Count > 1)
                clusters.Add(list.Select(document => new CorpusDocumentRef(ordered[shardOf[document]].ShardIndex, ordinalOf[document])).ToList());
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

    /// <summary>The most documents one merge holds with signatures of the given length.</summary>
    /// <param name="hashes">Hashes per signature.</param>
    /// <returns><see cref="MAX_DOCUMENTS"/> at 128 hashes; as many as fit <see cref="MAX_SIGNATURE_VALUES"/> at another count.</returns>
    public static long Capacity(int hashes)
    {
        return MAX_SIGNATURE_VALUES / Math.Max(1, hashes);
    }

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }

        return x;
    }

    private static void Union(int[] parent, int a, int b)
    {
        var rootA = Find(parent, a);
        var rootB = Find(parent, b);
        if (rootA == rootB)
            return;

        // The lower root wins so the representative is the lowest document of the cluster.
        if (rootA < rootB)
            parent[rootB] = rootA;
        else
            parent[rootA] = rootB;
    }

    #endregion
}
