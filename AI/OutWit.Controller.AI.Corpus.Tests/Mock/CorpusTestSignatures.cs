using OutWit.Controller.AI.Corpus.Dedup;

namespace OutWit.Controller.AI.Corpus.Tests.Mock;

/// <summary>
/// Signatures made by hand for the tests of the near-duplicate merge: whoever writes a shard's
/// signatures blob chooses its values, so the merge is tested with values no signer gives.
/// <see cref="Any"/> gives values with nothing in common; <see cref="WithKeysOfEqualHalves"/> gives
/// every document, in every band, a band key of its own whose high and low halves are the same.
/// </summary>
internal static class CorpusTestSignatures
{
    #region Constants

    // The fold of a band's values into its key (MinHashSigner.BandKey): FNV-1a over 64-bit values.
    private const ulong FNV_OFFSET = 14695981039346656037UL;
    private const ulong FNV_PRIME = 1099511628211UL;

    #endregion

    #region Functions

    /// <summary>A shard of signatures that have nothing in common.</summary>
    public static CorpusDedupShard Any(int shardIndex, int documents, int hashes, int seed)
    {
        var random = new Random(seed);
        var values = new ulong[(long)documents * hashes];
        for (var index = 0; index < values.Length; index++)
            values[index] = (ulong)random.NextInt64();

        return new CorpusDedupShard(shardIndex, Guid.NewGuid(), hashes, values);
    }

    /// <summary>
    /// A shard in which every document has, in every band, a band key of its own with two equal
    /// halves. The rows of a band are any values but the last, which is the one that folds the
    /// band to the key that was chosen for it.
    /// </summary>
    public static CorpusDedupShard WithKeysOfEqualHalves(int shardIndex, int documents, int hashes, int bands, int seed)
    {
        var rows = hashes / bands;
        var inverse = Inverse(FNV_PRIME);
        var random = new Random(seed);
        var values = new ulong[(long)documents * hashes];

        for (var document = 0; document < documents; document++)
        {
            for (var band = 0; band < bands; band++)
            {
                var at = (long)document * hashes + band * rows;
                var fold = FNV_OFFSET ^ (ulong)band;
                for (var row = 0; row < rows - 1; row++)
                {
                    var value = (ulong)random.NextInt64();
                    values[at + row] = value;
                    fold ^= value;
                    fold *= FNV_PRIME;
                }

                values[at + rows - 1] = fold ^ (KeyOfEqualHalves(document, band) * inverse);
            }
        }

        return new CorpusDedupShard(shardIndex, Guid.NewGuid(), hashes, values);
    }

    /// <summary>The key <see cref="WithKeysOfEqualHalves"/> gives a document in a band: no two documents of a shard share one.</summary>
    public static ulong KeyOfEqualHalves(int document, int band)
    {
        var half = (ulong)unchecked((uint)document * 2654435761u + (uint)band + 1);

        return (half << 32) | half;
    }

    #endregion

    #region Tools

    /// <summary>The inverse of an odd number modulo 2^64 (Newton's iteration).</summary>
    private static ulong Inverse(ulong odd)
    {
        var inverse = odd;
        for (var step = 0; step < 6; step++)
            inverse *= 2 - odd * inverse;

        return inverse;
    }

    #endregion
}
