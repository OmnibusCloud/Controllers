using System.Buffers.Binary;
using System.Text;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The signatures blob: an 8-byte magic, the hashes-per-signature count (int32 LE), then one
/// signature per product document in product order (uint64 LE each). Fixed layout, no
/// per-document framing. With the default hash count and shingle size (128, 5) the magic is
/// <c>WITSIG1\n</c> and document i's signature starts at 12 + i × 8 × hashes: the format of Corpus
/// 0.2.x, byte for byte. With any other the magic is <c>WITSIG2\n</c> and the shingle size (int32 LE)
/// follows the hash count, so the blob says how it was made: document i then starts at
/// 16 + i × 8 × hashes. The band layout is the merge's and does not touch the blob.
/// </summary>
public static class ShardSignatureWriter
{
    #region Constants

    /// <summary>The magic of a blob made with the default parameters (128 hashes, 5-word shingles).</summary>
    public static readonly byte[] MAGIC = Encoding.ASCII.GetBytes("WITSIG1\n");

    /// <summary>The magic of a blob made with other parameters: the shingle size follows the hash count.</summary>
    public static readonly byte[] MAGIC_PARAMETERS = Encoding.ASCII.GetBytes("WITSIG2\n");

    /// <summary>Bytes before the first signature of a <c>WITSIG1</c> blob.</summary>
    public const int HEADER_SIZE = 12;

    /// <summary>Bytes before the first signature of a <c>WITSIG2</c> blob.</summary>
    public const int HEADER_SIZE_PARAMETERS = 16;

    #endregion

    #region Functions

    /// <summary>Writes the header of a blob made with the default parameters.</summary>
    /// <param name="destination">The blob stream.</param>
    public static void WriteHeader(Stream destination)
    {
        WriteHeader(destination, MinHashParameters.DEFAULT);
    }

    /// <summary>Writes the header: <c>WITSIG1</c> for the default parameters, <c>WITSIG2</c> with the shingle size for any other.</summary>
    /// <param name="destination">The blob stream.</param>
    /// <param name="parameters">The parameters the signatures are made with.</param>
    public static void WriteHeader(Stream destination, MinHashParameters parameters)
    {
        Span<byte> value = stackalloc byte[4];
        var standard = IsStandard(parameters);

        destination.Write(standard ? MAGIC : MAGIC_PARAMETERS);
        BinaryPrimitives.WriteInt32LittleEndian(value, parameters.Hashes);
        destination.Write(value);
        if (standard)
            return;

        BinaryPrimitives.WriteInt32LittleEndian(value, parameters.ShingleWords);
        destination.Write(value);
    }

    /// <summary>The bytes of a blob's header for the given parameters.</summary>
    /// <param name="parameters">The parameters.</param>
    /// <returns><see cref="HEADER_SIZE"/> or <see cref="HEADER_SIZE_PARAMETERS"/>.</returns>
    public static int HeaderSize(MinHashParameters parameters)
    {
        return IsStandard(parameters) ? HEADER_SIZE : HEADER_SIZE_PARAMETERS;
    }

    /// <summary>Appends one signature.</summary>
    /// <param name="destination">The blob stream.</param>
    /// <param name="signature">The MinHash signature.</param>
    public static void Write(Stream destination, ReadOnlySpan<ulong> signature)
    {
        destination.Write(MinHashSigner.ToBytes(signature));
    }

    /// <summary>Reads a blob's header: how its signatures were made and where they start.</summary>
    /// <param name="bytes">The blob (at least its header).</param>
    /// <returns>The hash count, the shingle size and the header's size.</returns>
    /// <exception cref="FormatException">The magic or the header is wrong.</exception>
    public static (int Hashes, int ShingleWords, int HeaderSize) ReadHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= HEADER_SIZE && bytes[..MAGIC.Length].SequenceEqual(MAGIC))
            return (BinaryPrimitives.ReadInt32LittleEndian(bytes[MAGIC.Length..]), MinHashSigner.SHINGLE_WORDS, HEADER_SIZE);

        if (bytes.Length >= HEADER_SIZE_PARAMETERS && bytes[..MAGIC_PARAMETERS.Length].SequenceEqual(MAGIC_PARAMETERS))
            return (BinaryPrimitives.ReadInt32LittleEndian(bytes[MAGIC_PARAMETERS.Length..]), BinaryPrimitives.ReadInt32LittleEndian(bytes[(MAGIC_PARAMETERS.Length + 4)..]), HEADER_SIZE_PARAMETERS);

        throw new FormatException("Not a Corpus signatures blob.");
    }

    /// <summary>Reads a whole signatures blob back.</summary>
    /// <param name="bytes">The blob.</param>
    /// <returns>The signatures in product order.</returns>
    /// <exception cref="FormatException">The magic or the layout is wrong.</exception>
    public static List<ulong[]> Read(ReadOnlySpan<byte> bytes)
    {
        var (hashes, shingleWords, header) = ReadHeader(bytes);
        if (hashes <= 0 || hashes > MinHashSigner.MAX_HASHES || shingleWords <= 0 || (bytes.Length - header) % (hashes * 8) != 0)
            throw new FormatException("The signatures blob is truncated.");

        var result = new List<ulong[]>((bytes.Length - header) / (hashes * 8));
        for (var offset = header; offset < bytes.Length; offset += hashes * 8)
        {
            var signature = new ulong[hashes];
            for (var i = 0; i < hashes; i++)
                signature[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(offset + i * 8)..]);
            result.Add(signature);
        }

        return result;
    }

    /// <summary>Whether a blob made with the parameters has the <c>WITSIG1</c> layout (the defaults' hash count and shingle size).</summary>
    private static bool IsStandard(MinHashParameters parameters)
    {
        return parameters.Hashes == MinHashSigner.HASHES && parameters.ShingleWords == MinHashSigner.SHINGLE_WORDS;
    }

    #endregion
}
