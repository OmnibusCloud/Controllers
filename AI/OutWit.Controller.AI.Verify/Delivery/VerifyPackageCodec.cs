using System.IO.Compression;
using System.Security.Cryptography;
using MemoryPack;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// The format of a package blob: an eight-byte header (magic, kind, version) followed by a
/// Brotli stream. Tasks travel to a node and results travel back in this form, as blobs, so the
/// messages that assign work and report it carry references only. The format is the
/// controller's own: both ends run the same module.
/// <para>
/// A task package (version 1) holds the MemoryPack image of its batches. A result package
/// (version 2) is a stream of frames a node writes as its tasks finish and the server reads one
/// at a time (<see cref="VerifyResultPackageWriter"/>, <see cref="VerifyResultPackageReader"/>).
/// Nothing is decompressed without a bound: a blob that unpacks to more than its kind may hold
/// is refused, by whoever wrote it.
/// </para>
/// </summary>
public static class VerifyPackageCodec
{
    #region Constants

    /// <summary>File name of a task package blob.</summary>
    public const string TASKS_FILE_NAME = "verify-tasks.wvp";

    /// <summary>File name of a result package blob.</summary>
    public const string RESULTS_FILE_NAME = "verify-results.wvp";

    /// <summary>The length of a package's header.</summary>
    public const int HEADER_LENGTH = 8;

    /// <summary>The kind byte of a task package.</summary>
    public const byte KIND_TASKS = 1;

    /// <summary>The kind byte of a result package.</summary>
    public const byte KIND_RESULTS = 2;

    /// <summary>The version of the task package format.</summary>
    public const byte TASKS_VERSION = 1;

    /// <summary>The version of the result package format: frames, written and read as a stream.</summary>
    public const byte RESULTS_VERSION = 2;

    /// <summary>
    /// The most bytes a task package may unpack to. A package holds at most
    /// <see cref="VerifyPackagePlanner.MAX_PACKAGE_BYTES"/> characters of task text; a character
    /// is at most four bytes in the image, and the rest is the image's own structure.
    /// </summary>
    public const long MAX_TASKS_IMAGE_BYTES = 4 * VerifyPackagePlanner.MAX_PACKAGE_BYTES + (16L << 20);

    /// <summary>Brotli quality 4: text-heavy packages shrink several times at hundreds of megabytes per second.</summary>
    public const int COMPRESSION_QUALITY = 4;

    private const int COPY_BUFFER_BYTES = 81920;

    private static readonly byte[] MAGIC = "WVPK"u8.ToArray();

    #endregion

    #region Functions

    /// <summary>Writes a task package.</summary>
    /// <param name="batches">The task batches of the package, in order.</param>
    /// <returns>The blob's bytes.</returns>
    public static byte[] WriteTasks(IReadOnlyList<VerifyTaskBatchData> batches)
    {
        var image = MemoryPackSerializer.Serialize(batches.ToArray());

        using var stream = new MemoryStream(HEADER_LENGTH + image.Length / 3);
        WriteHeader(stream, KIND_TASKS, TASKS_VERSION);

        using (var brotli = new BrotliStream(stream, new BrotliCompressionOptions { Quality = COMPRESSION_QUALITY }, leaveOpen: true))
            brotli.Write(image);

        return stream.ToArray();
    }

    /// <summary>Reads a task package.</summary>
    /// <param name="bytes">The blob's bytes.</param>
    /// <returns>The task batches, in order.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a task package of a known version, or unpack to more than a task package may hold.</exception>
    public static List<VerifyTaskBatchData> ReadTasks(byte[] bytes)
    {
        using var source = new MemoryStream(bytes, writable: false);
        ReadHeader(source, KIND_TASKS, TASKS_VERSION);

        using var brotli = new BrotliStream(source, CompressionMode.Decompress);
        var image = ReadBounded(brotli, MAX_TASKS_IMAGE_BYTES);

        try
        {
            var batches = MemoryPackSerializer.Deserialize<VerifyTaskBatchData[]>(image);
            return batches?.ToList() ?? throw new InvalidDataException("The task package holds no batches.");
        }
        catch (Exception exception) when (exception is not (InvalidDataException or OutOfMemoryException))
        {
            // Whatever the decoder throws, of any type: the package cannot be read.
            throw new InvalidDataException("The task package cannot be read.");
        }
    }

    /// <summary>The identity of a package blob: SHA-256 of its bytes, lowercase hex.</summary>
    /// <param name="bytes">The blob's bytes.</param>
    /// <returns>The hash.</returns>
    public static string Hash(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>The identity of a package blob in a file: SHA-256 of its bytes, lowercase hex.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hash.</returns>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Checks a downloaded package against the hash its reference carries.</summary>
    /// <param name="bytes">The blob's bytes.</param>
    /// <param name="expected">The hash from the reference.</param>
    /// <param name="what">What the blob is, for the message.</param>
    /// <exception cref="InvalidDataException">The bytes hash to something else, or the reference names no hash.</exception>
    public static void EnsureHash(byte[] bytes, string expected, string what)
    {
        if (!string.Equals(Hash(bytes), expected, StringComparison.Ordinal))
            throw new InvalidDataException($"The {what} is not the one its reference names (the SHA-256 differs).");
    }

    /// <summary>Writes a package's header.</summary>
    /// <param name="stream">The stream, at its start.</param>
    /// <param name="kind">The kind byte.</param>
    /// <param name="version">The version byte.</param>
    public static void WriteHeader(Stream stream, byte kind, byte version)
    {
        stream.Write(MAGIC);
        stream.WriteByte(kind);
        stream.WriteByte(version);
        stream.WriteByte(0);
        stream.WriteByte(0);
    }

    /// <summary>Reads a package's header and holds it to a kind and a version.</summary>
    /// <param name="stream">The stream, at its start.</param>
    /// <param name="kind">The kind the package must be of.</param>
    /// <param name="version">The version this module reads.</param>
    /// <exception cref="InvalidDataException">The stream is not a package of that kind and version.</exception>
    public static void ReadHeader(Stream stream, byte kind, byte version)
    {
        Span<byte> header = stackalloc byte[HEADER_LENGTH];
        if (stream.ReadAtLeast(header, HEADER_LENGTH, throwOnEndOfStream: false) < HEADER_LENGTH || !header[..MAGIC.Length].SequenceEqual(MAGIC))
            throw new InvalidDataException("The blob is not a Verify package.");

        if (header[MAGIC.Length] != kind)
            throw new InvalidDataException($"The package is of kind {header[MAGIC.Length]}, expected {kind}.");

        if (header[MAGIC.Length + 1] != version)
            throw new InvalidDataException($"The package is of version {header[MAGIC.Length + 1]}; this module reads version {version}.");
    }

    /// <summary>
    /// Reads a stream to its end, and refuses it when it holds more than a bound: nothing is
    /// allocated for bytes that are not there, and nothing past the bound is kept.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="maxBytes">The most bytes the stream may give.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="InvalidDataException">The stream holds more than the bound, or is damaged.</exception>
    public static byte[] ReadBounded(Stream stream, long maxBytes)
    {
        using var image = new MemoryStream();
        var buffer = new byte[COPY_BUFFER_BYTES];

        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (image.Length + read > maxBytes)
                    throw new InvalidDataException($"The package unpacks to more than the {maxBytes} bytes it may hold.");

                image.Write(buffer, 0, read);
            }
        }
        catch (InvalidOperationException)
        {
            // Brotli's word for a stream that is not one.
            throw new InvalidDataException("The package's stream is damaged.");
        }

        return image.ToArray();
    }

    #endregion
}
