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
    public const int DEFAULT_BANDS = 16;

    /// <summary>Most documents one merge holds in memory (about 1 KB each).</summary>
    public const int MAX_DOCUMENTS = 4_000_000;

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
    /// <param name="cancellationToken">Stops between shards.</param>
    /// <returns>The decision.</returns>
    /// <exception cref="InvalidOperationException">More documents than one merge holds.</exception>
    public CorpusDedupResult Run(IReadOnlyList<CorpusDedupShard> shards, CancellationToken cancellationToken = default)
    {
        var ordered = shards.OrderBy(shard => shard.ShardIndex).ToList();
        var total = ordered.Sum(shard => (long)shard.Signatures.Count);
        if (total > MAX_DOCUMENTS)
            throw new InvalidOperationException($"{total} documents exceed the merge's capacity of {MAX_DOCUMENTS}.");

        var signatures = new ulong[total][];
        var refs = new CorpusDocumentRef[total];
        var next = 0;
        foreach (var shard in ordered)
        {
            for (var ordinal = 0; ordinal < shard.Signatures.Count; ordinal++)
            {
                signatures[next] = shard.Signatures[ordinal];
                refs[next] = new CorpusDocumentRef(shard.ShardIndex, ordinal);
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

            for (var document = 0; document < signatures.Length; document++)
            {
                if (signatures[document].Length == 0)
                    continue;

                var key = MinHashSigner.BandKeys(signatures[document], m_bands)[band];
                if (!firstByKey.TryGetValue(key, out var first))
                {
                    firstByKey[key] = document;
                    continue;
                }

                if (Find(parent, first) == Find(parent, document))
                    continue;

                if (MinHashSigner.Similarity(signatures[first], signatures[document]) >= m_threshold)
                    Union(parent, first, document);
            }
        }

        var members = new Dictionary<int, List<int>>();
        for (var document = 0; document < signatures.Length; document++)
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
                clusters.Add(list.Select(document => refs[document]).ToList());
        }

        clusters.Sort((a, b) => a[0].CompareTo(b[0]));

        var shardResults = new List<CorpusDedupShardResult>(ordered.Count);
        var offset = 0;
        foreach (var shard in ordered)
        {
            var mask = new bool[shard.Signatures.Count];
            Array.Copy(keep, offset, mask, 0, mask.Length);
            shardResults.Add(new CorpusDedupShardResult(shard.ShardIndex, shard.SignaturesBlobId, mask));
            offset += mask.Length;
        }

        return new CorpusDedupResult(m_threshold, m_bands, (int)total, shardResults, clusters);
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
