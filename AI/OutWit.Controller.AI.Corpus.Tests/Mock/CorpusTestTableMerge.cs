using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// The near-duplicate merge as the controller decided it up to 0.7.0, kept here as the measure of
/// what a merge must keep: for each band a table from band key to the first document that had it,
/// every later document of the key compared with that one. The controller's merge finds the same
/// pairs another way; the tests hold its decision to this one, mask for mask and cluster for
/// cluster.
/// </summary>
internal static class CorpusTestTableMerge
{
    #region Functions

    /// <summary>Runs the merge.</summary>
    /// <param name="shards">The shards, any order.</param>
    /// <param name="threshold">The Jaccard estimate a pair needs.</param>
    /// <param name="bands">LSH bands; they divide the signature length.</param>
    /// <returns>The decision.</returns>
    public static CorpusDedupResult Run(IReadOnlyList<CorpusDedupShard> shards, double threshold = CorpusDeduplicator.DEFAULT_THRESHOLD, int bands = CorpusDeduplicator.DEFAULT_BANDS)
    {
        var ordered = shards.OrderBy(shard => shard.ShardIndex).ToList();
        var hashes = ordered.Count > 0 ? ordered[0].Hashes : MinHashSigner.HASHES;
        var rows = hashes / bands;
        var total = ordered.Sum(shard => shard.Documents);

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

        var parent = Enumerable.Range(0, total).ToArray();
        var firstByKey = new Dictionary<ulong, int>();
        for (var band = 0; band < bands; band++)
        {
            firstByKey.Clear();
            for (var document = 0; document < total; document++)
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

                if (MinHashSigner.Similarity(ordered[shardOf[first]].Signature(ordinalOf[first]), signature) >= threshold)
                    Union(parent, first, document);
            }
        }

        var members = new SortedDictionary<int, List<int>>();
        for (var document = 0; document < total; document++)
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

        var results = new List<CorpusDedupShardResult>(ordered.Count);
        var offset = 0;
        foreach (var shard in ordered)
        {
            results.Add(new CorpusDedupShardResult(shard.ShardIndex, shard.SignaturesBlobId, keep.AsSpan(offset, shard.Documents).ToArray()));
            offset += shard.Documents;
        }

        return new CorpusDedupResult(threshold, bands, total, results, clusters);
    }

    #endregion

    #region Tools

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

        if (rootA < rootB)
            parent[rootB] = rootA;
        else
            parent[rootA] = rootB;
    }

    #endregion
}
