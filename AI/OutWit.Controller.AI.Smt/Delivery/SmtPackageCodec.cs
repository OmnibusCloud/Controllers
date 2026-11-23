using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// The format of an Smt package blob: an eight-byte header (magic, kind, version, two zero bytes)
/// followed by a Brotli stream of frames, each the length of a MemoryPack image (four bytes, low
/// byte first) and the image. A package of problems holds one frame a problem
/// (<see cref="Model.SmtProblemData"/>); a package of results holds one frame a row
/// (<see cref="Model.SmtResultData"/>). Problems travel to a node and rows travel back in this
/// form, as blobs, so the messages that assign the work and report it carry references only. Both
/// ends run the same module, so the format is the controller's own.
/// <para>
/// Nothing is read without a bound: a frame longer than its kind may be, more frames than the
/// reference names, or more bytes in all than a package may unpack to are refused before they are
/// held, and a frame's buffer grows with the bytes that arrive, not with the length its head
/// claims.
/// </para>
/// </summary>
internal static class SmtPackageCodec
{
    #region Constants

    /// <summary>File name of a package of problems.</summary>
    public const string PROBLEMS_FILE_NAME = "smt-problems.wsp";

    /// <summary>File name of a package of results.</summary>
    public const string RESULTS_FILE_NAME = "smt-results.wsp";

    /// <summary>The length of a package's header.</summary>
    public const int HEADER_LENGTH = 8;

    /// <summary>The kind byte of a package of problems.</summary>
    public const byte KIND_PROBLEMS = 1;

    /// <summary>The kind byte of a package of results.</summary>
    public const byte KIND_RESULTS = 2;

    /// <summary>The version of the format, for both kinds.</summary>
    public const byte VERSION = 1;

    /// <summary>The length of a frame's head: the length of the image that follows.</summary>
    public const int FRAME_HEAD_LENGTH = sizeof(int);

    /// <summary>Brotli quality 4: text shrinks several times at hundreds of megabytes a second.</summary>
    public const int COMPRESSION_QUALITY = 4;

    /// <summary>The most problems a node takes in one package (a batch of the long tail is small: a node never waits on one problem longer than its hard cap while its other sessions go on).</summary>
    public const int MAX_PACKAGE_PROBLEMS = 256;

    /// <summary>
    /// The most bytes one problem's frame may hold: a problem at the ceilings of a submitted
    /// problem (its canonical text, 256 terms of values and 256 of projection, a goal, each at its
    /// ceiling) at three bytes a character.
    /// </summary>
    public const int MAX_PROBLEM_FRAME_BYTES = 128 << 20;

    /// <summary>The most bytes a package of problems may unpack to.</summary>
    public const long MAX_PROBLEMS_IMAGE_BYTES = 256L << 20;

    /// <summary>The most bytes a package of problems may be as a blob: what it unpacks to, and room for a stream that did not shrink.</summary>
    public const long MAX_PROBLEMS_BLOB_BYTES = MAX_PROBLEMS_IMAGE_BYTES + (1L << 20);

    /// <summary>
    /// The most bytes one row's frame may hold: everything a session may write (its output cap)
    /// at three bytes a character, and room for the rest of the row.
    /// </summary>
    public const int MAX_RESULT_FRAME_BYTES = 64 << 20;

    private const int FIRST_FRAME_BUFFER_BYTES = 64 * 1024;

    private static readonly byte[] MAGIC = "WSPK"u8.ToArray();

    private static readonly UTF8Encoding TEXT = new(encoderShouldEmitUTF8Identifier: false);

    #endregion

    #region Functions

    /// <summary>Writes a package's header.</summary>
    /// <param name="stream">The stream, at its start.</param>
    /// <param name="kind">The kind byte.</param>
    public static void WriteHeader(Stream stream, byte kind)
    {
        stream.Write(MAGIC);
        stream.WriteByte(kind);
        stream.WriteByte(VERSION);
        stream.WriteByte(0);
        stream.WriteByte(0);
    }

    /// <summary>Reads a package's header and holds it to a kind and to the version this module reads.</summary>
    /// <param name="stream">The stream, at its start.</param>
    /// <param name="kind">The kind the package must be of.</param>
    /// <exception cref="InvalidDataException">The stream is not a package of that kind and version.</exception>
    public static void ReadHeader(Stream stream, byte kind)
    {
        Span<byte> header = stackalloc byte[HEADER_LENGTH];
        if (stream.ReadAtLeast(header, HEADER_LENGTH, throwOnEndOfStream: false) < HEADER_LENGTH || !header[..MAGIC.Length].SequenceEqual(MAGIC))
            throw new InvalidDataException("The blob is not an Smt package.");

        if (header[MAGIC.Length] != kind)
            throw new InvalidDataException($"The package is of kind {header[MAGIC.Length]}, not {kind}.");

        if (header[MAGIC.Length + 1] != VERSION)
            throw new InvalidDataException($"The package is of version {header[MAGIC.Length + 1]}; this module reads version {VERSION}.");

        if (header[MAGIC.Length + 2] != 0 || header[MAGIC.Length + 3] != 0)
            throw new InvalidDataException("The package's header is not one this module writes.");
    }

    /// <summary>Opens the compressed stream of frames that follows a header.</summary>
    /// <param name="stream">The stream, past its header.</param>
    /// <returns>A Brotli stream that writes; the caller disposes it, and it leaves the stream open.</returns>
    public static BrotliStream Compressing(Stream stream)
    {
        return new BrotliStream(stream, new BrotliCompressionOptions { Quality = COMPRESSION_QUALITY }, leaveOpen: true);
    }

    /// <summary>Writes one frame.</summary>
    /// <param name="stream">The compressed stream.</param>
    /// <param name="image">The frame's MemoryPack image.</param>
    public static void WriteFrame(Stream stream, ReadOnlySpan<byte> image)
    {
        Span<byte> head = stackalloc byte[FRAME_HEAD_LENGTH];
        BinaryPrimitives.WriteInt32LittleEndian(head, image.Length);

        stream.Write(head);
        stream.Write(image);
    }

    /// <summary>
    /// Reads the next frame of a compressed stream, held to a bound before it is held. The buffer
    /// grows with the bytes that arrive, not by the length the frame's head states: a head that
    /// claims a large frame the stream does not hold costs nothing.
    /// </summary>
    /// <param name="stream">The decompressing stream.</param>
    /// <param name="maxFrameBytes">The most bytes the frame may hold.</param>
    /// <returns>The frame's image; null at the end of the stream, where a frame would start.</returns>
    /// <exception cref="InvalidDataException">The stream is damaged, ends inside a frame, or the frame is longer than the bound.</exception>
    public static byte[]? ReadFrame(Stream stream, int maxFrameBytes)
    {
        Span<byte> head = stackalloc byte[FRAME_HEAD_LENGTH];
        var got = ReadHead(stream, head);
        if (got == 0)
            return null;

        if (got < FRAME_HEAD_LENGTH)
            throw new InvalidDataException("The package ends inside a frame.");

        var length = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (length < 0 || length > maxFrameBytes)
            throw new InvalidDataException("A frame of the package is longer than a frame of its kind may be.");

        var buffer = new byte[Math.Min(length, FIRST_FRAME_BUFFER_BYTES)];
        var filled = 0;
        while (filled < length)
        {
            if (filled == buffer.Length)
                Array.Resize(ref buffer, (int)Math.Min(length, buffer.Length * 2L));

            var read = Read(stream, buffer, filled, buffer.Length - filled);
            if (read == 0)
                throw new InvalidDataException("The package ends inside a frame.");

            filled += read;
        }

        return buffer;
    }

    /// <summary>The identity of a package blob in a file: SHA-256 of its bytes, lower-case hex.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hash.</returns>
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>The SHA-256 of a text's UTF-8 bytes, lower-case hex: the name a problem's canonical text hashes to.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The hash.</returns>
    public static string HashText(string text)
    {
        return Convert.ToHexStringLower(SHA256.HashData(TEXT.GetBytes(text)));
    }

    /// <summary>The most bytes a package of results may unpack to: a frame at its bound for each row it may hold.</summary>
    /// <param name="rows">The rows it may hold.</param>
    /// <returns>Bytes.</returns>
    public static long MaxResultsImageBytes(int rows)
    {
        return Math.Max(1, rows) * (long)(MAX_RESULT_FRAME_BYTES + FRAME_HEAD_LENGTH);
    }

    #endregion

    #region Tools

    /// <summary>Fills the head of a frame, or as much of it as the stream still has; damage of the stream is the package's.</summary>
    private static int ReadHead(Stream stream, Span<byte> head)
    {
        try
        {
            return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }
        catch (InvalidOperationException)
        {
            // Brotli's word for a stream that is not one.
            throw new InvalidDataException("The package's stream is damaged.");
        }
        catch (IOException exception) when (exception is not FileNotFoundException)
        {
            throw new InvalidDataException("The package's stream is damaged.");
        }
    }

    private static int Read(Stream stream, byte[] buffer, int offset, int count)
    {
        try
        {
            return stream.Read(buffer, offset, count);
        }
        catch (InvalidOperationException)
        {
            throw new InvalidDataException("The package's stream is damaged.");
        }
        catch (IOException exception) when (exception is not FileNotFoundException)
        {
            throw new InvalidDataException("The package's stream is damaged.");
        }
    }

    #endregion
}
