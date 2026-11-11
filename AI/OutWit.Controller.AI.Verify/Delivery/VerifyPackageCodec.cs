using System.IO.Compression;
using System.Security.Cryptography;
using MemoryPack;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// The format of a package blob: an eight-byte header (magic, kind, version) followed by the
/// Brotli-compressed MemoryPack image of the package's batches. Tasks travel to a node and
/// results travel back in this form, as blobs, so the messages that assign work and report it
/// carry references only. The format is the controller's own: both ends run the same module.
/// </summary>
public static class VerifyPackageCodec
{
    #region Constants

    /// <summary>File name of a task package blob.</summary>
    public const string TASKS_FILE_NAME = "verify-tasks.wvp";

    /// <summary>File name of a result package blob.</summary>
    public const string RESULTS_FILE_NAME = "verify-results.wvp";

    private const int HEADER_LENGTH = 8;

    private const byte KIND_TASKS = 1;

    private const byte KIND_RESULTS = 2;

    private const byte VERSION = 1;

    /// <summary>Brotli quality 4: text-heavy packages shrink several times at hundreds of megabytes per second.</summary>
    private const int COMPRESSION_QUALITY = 4;

    private static readonly byte[] MAGIC = "WVPK"u8.ToArray();

    #endregion

    #region Functions

    /// <summary>Writes a task package.</summary>
    /// <param name="batches">The task batches of the package, in order.</param>
    /// <returns>The blob's bytes.</returns>
    public static byte[] WriteTasks(IReadOnlyList<VerifyTaskBatchData> batches)
    {
        return Write(KIND_TASKS, MemoryPackSerializer.Serialize(batches.ToArray()));
    }

    /// <summary>Reads a task package.</summary>
    /// <param name="bytes">The blob's bytes.</param>
    /// <returns>The task batches, in order.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a task package of a known version.</exception>
    public static List<VerifyTaskBatchData> ReadTasks(byte[] bytes)
    {
        var batches = MemoryPackSerializer.Deserialize<VerifyTaskBatchData[]>(Read(KIND_TASKS, bytes));

        return batches?.ToList() ?? throw new InvalidDataException("The task package holds no batches.");
    }

    /// <summary>Writes a result package.</summary>
    /// <param name="batches">The result batches of the package, one per task batch executed.</param>
    /// <returns>The blob's bytes.</returns>
    public static byte[] WriteResults(IReadOnlyList<VerifyResultBatchData> batches)
    {
        return Write(KIND_RESULTS, MemoryPackSerializer.Serialize(batches.ToArray()));
    }

    /// <summary>Reads a result package.</summary>
    /// <param name="bytes">The blob's bytes.</param>
    /// <returns>The result batches.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a result package of a known version.</exception>
    public static List<VerifyResultBatchData> ReadResults(byte[] bytes)
    {
        var batches = MemoryPackSerializer.Deserialize<VerifyResultBatchData[]>(Read(KIND_RESULTS, bytes));

        return batches?.ToList() ?? throw new InvalidDataException("The result package holds no batches.");
    }

    /// <summary>The identity of a package blob: SHA-256 of its bytes, lowercase hex.</summary>
    /// <param name="bytes">The blob's bytes.</param>
    /// <returns>The hash.</returns>
    public static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Checks a downloaded package against the hash its reference carries.</summary>
    /// <param name="bytes">The blob's bytes.</param>
    /// <param name="expected">The hash from the reference; empty skips the check.</param>
    /// <param name="what">What the blob is, for the message.</param>
    /// <exception cref="InvalidDataException">The bytes hash to something else.</exception>
    public static void EnsureHash(byte[] bytes, string expected, string what)
    {
        if (expected.Length == 0)
            return;

        var actual = Hash(bytes);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"The {what} hashes to {actual}, not to the {expected} its reference names.");
    }

    private static byte[] Write(byte kind, byte[] image)
    {
        using var stream = new MemoryStream(HEADER_LENGTH + image.Length / 3);

        stream.Write(MAGIC);
        stream.WriteByte(kind);
        stream.WriteByte(VERSION);
        stream.WriteByte(0);
        stream.WriteByte(0);

        using (var brotli = new BrotliStream(stream, new BrotliCompressionOptions { Quality = COMPRESSION_QUALITY }, leaveOpen: true))
            brotli.Write(image);

        return stream.ToArray();
    }

    private static byte[] Read(byte kind, byte[] bytes)
    {
        if (bytes.Length < HEADER_LENGTH || !bytes.AsSpan(0, MAGIC.Length).SequenceEqual(MAGIC))
            throw new InvalidDataException("The blob is not a Verify package.");

        if (bytes[MAGIC.Length] != kind)
            throw new InvalidDataException($"The package is of kind {bytes[MAGIC.Length]}, expected {kind}.");

        if (bytes[MAGIC.Length + 1] != VERSION)
            throw new InvalidDataException($"The package is of version {bytes[MAGIC.Length + 1]}; this module reads version {VERSION}.");

        using var source = new MemoryStream(bytes, HEADER_LENGTH, bytes.Length - HEADER_LENGTH, writable: false);
        using var brotli = new BrotliStream(source, CompressionMode.Decompress);
        using var image = new MemoryStream();
        brotli.CopyTo(image);

        return image.ToArray();
    }

    #endregion
}
