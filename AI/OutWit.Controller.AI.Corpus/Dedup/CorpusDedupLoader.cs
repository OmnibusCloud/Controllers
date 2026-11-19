using System.Buffers.Binary;
using System.Security.Cryptography;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Dedup;

/// <summary>
/// Reads the signature blobs of a merge, each once, each held to what the report says of it
/// before a value of it is used: its length is that of one signature per document of the product
/// (asked of the file, before it is opened), it starts as a signatures blob of the run's
/// parameters, and its bytes hash to the <c>signatures_sha256</c> integrity compared. A blob that
/// fails a check is left out with a fixed reason and the merge goes on over the others: a node
/// that reported the honest hash and uploaded other signatures does not get them merged, and
/// does not stop the run. Memory is one array per shard of exactly the blob's values.
/// </summary>
public static class CorpusDedupLoader
{
    #region Constants

    private const int BUFFER_BYTES = 1 << 20;

    #endregion

    #region Functions

    /// <summary>Loads the shards of a merge.</summary>
    /// <param name="input">What the report names (already held to the merge's capacity).</param>
    /// <param name="resolve">Gives the local file of a blob.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The shards that passed, and the ones left out with the reason (the input's own exclusions included), both in index order.</returns>
    public static async Task<(List<CorpusDedupShard> Shards, List<CorpusDedupExclusion> Excluded)> LoadAsync(CorpusDedupInput input, Func<Guid, Task<string>> resolve, CancellationToken cancellationToken)
    {
        var shards = new List<CorpusDedupShard>(input.Shards.Count);
        var excluded = new List<CorpusDedupExclusion>(input.Excluded);

        foreach (var source in input.Shards)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (shard, reason) = await LoadAsync(source, resolve, cancellationToken);
            if (shard != null)
                shards.Add(shard);
            else
                excluded.Add(new CorpusDedupExclusion(source.Index, source.SignaturesBlobId, reason));
        }

        excluded.Sort((first, second) => first.Index.CompareTo(second.Index));
        return (shards, excluded);
    }

    /// <summary>Loads one shard's signatures, or says why they are not used.</summary>
    /// <param name="source">The shard as the report names it.</param>
    /// <param name="resolve">Gives the local file of a blob.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <returns>The shard and "", or null and one of <see cref="CorpusDedupExclusion.REASONS"/>.</returns>
    public static async Task<(CorpusDedupShard? Shard, string Reason)> LoadAsync(CorpusDedupSource source, Func<Guid, Task<string>> resolve, CancellationToken cancellationToken)
    {
        try
        {
            var path = await resolve(source.SignaturesBlobId);

            // The length first, from the file system: nothing is allocated for a blob of another size.
            if (new FileInfo(path).Length != source.ExpectedBytes)
                return (null, CorpusDedupExclusion.BLOB_LENGTH);

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BUFFER_BYTES, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var headerSize = ShardSignatureWriter.HeaderSize(source.Parameters);
            var header = new byte[headerSize];
            await stream.ReadExactlyAsync(header, cancellationToken);
            hash.AppendData(header);

            if (!IsHeader(header, source))
                return (null, CorpusDedupExclusion.BLOB_HEADER);

            var values = new ulong[(long)source.Documents * source.Parameters.Hashes];
            var buffer = new byte[BUFFER_BYTES];
            var filled = 0L;
            while (filled < values.Length)
            {
                var wanted = (int)Math.Min(buffer.Length, (values.Length - filled) * sizeof(ulong));
                await stream.ReadExactlyAsync(buffer.AsMemory(0, wanted), cancellationToken);
                hash.AppendData(buffer, 0, wanted);

                for (var offset = 0; offset < wanted; offset += sizeof(ulong))
                    values[filled++] = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset));
            }

            // A report before 0.3.0 names no hash: its blobs are held to their length and header only.
            if (source.SignaturesSha256.Length > 0 && !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), source.SignaturesSha256, StringComparison.Ordinal))
                return (null, CorpusDedupExclusion.BLOB_HASH);

            return (new CorpusDedupShard(source.Index, source.SignaturesBlobId, source.Parameters.Hashes, values), "");
        }
        catch (EndOfStreamException)
        {
            return (null, CorpusDedupExclusion.BLOB_LENGTH);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            // Whatever the store or the file system says of a blob a node uploaded (it is not
            // there, it will not open, the store answers in a way of its own): the blob is not
            // read, and that costs the merge this shard.
            return (null, CorpusDedupExclusion.BLOB_MISSING);
        }
    }

    private static bool IsHeader(byte[] header, CorpusDedupSource source)
    {
        try
        {
            var (hashes, shingleWords, size) = ShardSignatureWriter.ReadHeader(header);
            return size == header.Length && hashes == source.Parameters.Hashes && shingleWords == source.Parameters.ShingleWords;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    #endregion
}
