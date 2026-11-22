using System.Text.Json;
using OutWit.Controller.AI.Corpus.Dedup;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Reports;

/// <summary>
/// The dedup document: the merge's parameters (the threshold, the bands, and the hash count and
/// shingle size of the signatures it merged) and totals, per shard the keep mask (a bitset,
/// base64, bit i = document i of the shard's product, 1 = keep), the clusters of two or
/// more as (shard, ordinal) pairs — listed up to a cap, the masks carrying the complete
/// decision — and the completed shards the merge left out because their signatures blob was not
/// what the report says of it (<c>excluded</c>, each with a fixed reason): such a shard has no
/// mask, and its documents are in no cluster. Schema <c>corpus-dedup/2</c> (<c>/1</c> merged
/// whatever a blob held, and had no <c>excluded</c>).
/// </summary>
public static class CorpusDedupWriter
{
    #region Constants

    /// <summary>The document schema name.</summary>
    public const string SCHEMA = "corpus-dedup/2";

    /// <summary>Clusters listed at most.</summary>
    public const int MAX_CLUSTERS_LISTED = 100_000;

    #endregion

    #region Functions

    /// <summary>Writes the document of a merge over signatures made with the default parameters.</summary>
    /// <param name="result">The merge's decision.</param>
    /// <returns>UTF-8 JSON.</returns>
    public static byte[] Write(CorpusDedupResult result)
    {
        return Write(result, MinHashParameters.DEFAULT);
    }

    /// <summary>Writes the document.</summary>
    /// <param name="result">The merge's decision.</param>
    /// <param name="parameters">The MinHash parameters of the signatures it merged.</param>
    /// <returns>UTF-8 JSON.</returns>
    public static byte[] Write(CorpusDedupResult result, MinHashParameters parameters)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", SCHEMA);
            writer.WriteString("controller", ControllerBuildInfo.VERSION);

            writer.WriteStartObject("summary");
            writer.WriteNumber("threshold", result.Threshold);
            writer.WriteNumber("bands", result.Bands);
            writer.WriteNumber("hashes", parameters.Hashes);
            writer.WriteNumber("shingle_words", parameters.ShingleWords);
            writer.WriteNumber("shards", result.Shards.Count);
            writer.WriteNumber("excluded", result.Excluded.Count);
            writer.WriteNumber("documents", result.Documents);
            writer.WriteNumber("duplicates", result.Duplicates);
            writer.WriteNumber("kept", result.Documents - result.Duplicates);
            writer.WriteNumber("clusters", result.Clusters.Count);
            writer.WriteNumber("clusters_listed", Math.Min(result.Clusters.Count, MAX_CLUSTERS_LISTED));
            writer.WriteEndObject();

            writer.WriteStartArray("shards");
            foreach (var shard in result.Shards)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", shard.ShardIndex);
                writer.WriteString("signatures_blob", shard.SignaturesBlobId == Guid.Empty ? "" : shard.SignaturesBlobId.ToString("D"));
                writer.WriteNumber("documents", shard.Documents);
                writer.WriteNumber("duplicates", shard.Duplicates);
                writer.WriteString("keep", Convert.ToBase64String(ToBitset(shard.Keep)));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("excluded");
            foreach (var exclusion in result.Excluded)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", exclusion.Index);
                writer.WriteString("signatures_blob", exclusion.SignaturesBlobId == Guid.Empty ? "" : exclusion.SignaturesBlobId.ToString("D"));
                writer.WriteString("reason", exclusion.Reason);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("clusters");
            foreach (var cluster in result.Clusters.Take(MAX_CLUSTERS_LISTED))
            {
                writer.WriteStartArray();
                foreach (var document in cluster)
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(document.ShardIndex);
                    writer.WriteNumberValue(document.Ordinal);
                    writer.WriteEndArray();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>Packs a keep mask into a bitset, bit i of byte i/8 (least significant first).</summary>
    /// <param name="keep">The mask.</param>
    /// <returns>The bitset bytes.</returns>
    public static byte[] ToBitset(bool[] keep)
    {
        var bytes = new byte[(keep.Length + 7) / 8];
        for (var i = 0; i < keep.Length; i++)
        {
            if (keep[i])
                bytes[i / 8] |= (byte)(1 << (i % 8));
        }

        return bytes;
    }

    /// <summary>Unpacks a bitset into a keep mask of the given length.</summary>
    /// <param name="bitset">The bitset bytes.</param>
    /// <param name="documents">The mask length.</param>
    /// <returns>The mask.</returns>
    public static bool[] FromBitset(byte[] bitset, int documents)
    {
        var keep = new bool[documents];
        for (var i = 0; i < documents; i++)
            keep[i] = i / 8 < bitset.Length && (bitset[i / 8] & (1 << (i % 8))) != 0;

        return keep;
    }

    #endregion
}
