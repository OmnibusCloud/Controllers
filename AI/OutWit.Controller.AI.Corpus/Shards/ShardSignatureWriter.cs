using System.Buffers.Binary;
using System.Text;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The signatures blob: an 8-byte magic, the hashes-per-signature count (int32 LE), then one
/// signature per product document in product order (uint64 LE each). Fixed layout, no
/// per-document framing: document i's signature starts at 12 + i × 8 × hashes.
/// </summary>
public static class ShardSignatureWriter
{
    #region Constants

    /// <summary>The file magic.</summary>
    public static readonly byte[] MAGIC = Encoding.ASCII.GetBytes("WITSIG1\n");

    /// <summary>Bytes before the first signature.</summary>
    public const int HEADER_SIZE = 12;

    #endregion

    #region Functions

    /// <summary>Writes the header.</summary>
    /// <param name="destination">The blob stream.</param>
    public static void WriteHeader(Stream destination)
    {
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, MinHashSigner.HASHES);
        destination.Write(MAGIC);
        destination.Write(count);
    }

    /// <summary>Appends one signature.</summary>
    /// <param name="destination">The blob stream.</param>
    /// <param name="signature">The MinHash signature.</param>
    public static void Write(Stream destination, ReadOnlySpan<ulong> signature)
    {
        destination.Write(MinHashSigner.ToBytes(signature));
    }

    /// <summary>Reads a whole signatures blob back.</summary>
    /// <param name="bytes">The blob.</param>
    /// <returns>The signatures in product order.</returns>
    /// <exception cref="FormatException">The magic or the layout is wrong.</exception>
    public static List<ulong[]> Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HEADER_SIZE || !bytes[..MAGIC.Length].SequenceEqual(MAGIC))
            throw new FormatException("Not a Corpus signatures blob.");

        var hashes = BinaryPrimitives.ReadInt32LittleEndian(bytes[MAGIC.Length..]);
        if (hashes <= 0 || (bytes.Length - HEADER_SIZE) % (hashes * 8) != 0)
            throw new FormatException("The signatures blob is truncated.");

        var result = new List<ulong[]>((bytes.Length - HEADER_SIZE) / (hashes * 8));
        for (var offset = HEADER_SIZE; offset < bytes.Length; offset += hashes * 8)
        {
            var signature = new ulong[hashes];
            for (var i = 0; i < hashes; i++)
                signature[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(offset + i * 8)..]);
            result.Add(signature);
        }

        return result;
    }

    #endregion
}
