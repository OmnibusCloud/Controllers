using System.Buffers.Binary;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// The settled run as one blob of the server's own: the four documents a lab reads (the verdict
/// report, the outputs, the provenance, the integrity), made in one pass over what the nodes
/// returned. The activities that hand a document to the job take it out of here; none of them
/// reads a node's result again, and none decides anything. The format is the server's alone: an
/// eight-byte header, then each document as an eight-byte length and its bytes, the outputs
/// last, since they can be as large as the run.
/// </summary>
internal static class VerifyLedger
{
    #region Constants

    /// <summary>File name of a ledger blob.</summary>
    public const string FILE_NAME = "verify-ledger.wvl";

    /// <summary>The verdict report.</summary>
    public const int REPORT = 0;

    /// <summary>The provenance document.</summary>
    public const int PROVENANCE = 1;

    /// <summary>The integrity document.</summary>
    public const int INTEGRITY = 2;

    /// <summary>The outputs, one JSON line a task.</summary>
    public const int OUTPUTS = 3;

    private const int SECTIONS = 4;
    private const int HEADER_LENGTH = 8;
    private const byte VERSION = 1;
    private const int COPY_BUFFER_BYTES = 81920;

    private static readonly byte[] MAGIC = "WVLG"u8.ToArray();

    #endregion

    #region Functions

    /// <summary>Writes a ledger.</summary>
    /// <param name="path">The file to write (made new).</param>
    /// <param name="report">The verdict report.</param>
    /// <param name="provenance">The provenance document.</param>
    /// <param name="integrity">The integrity document.</param>
    /// <param name="outputsPath">The file holding the outputs.</param>
    public static void Write(string path, byte[] report, byte[] provenance, byte[] integrity, string outputsPath)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.Write(MAGIC);
        file.WriteByte(VERSION);
        file.Write([0, 0, 0]);

        foreach (var document in new[] { report, provenance, integrity })
        {
            WriteLength(file, document.Length);
            file.Write(document);
        }

        using var outputs = new FileStream(outputsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        WriteLength(file, outputs.Length);
        outputs.CopyTo(file, COPY_BUFFER_BYTES);
    }

    /// <summary>Reads one of the small documents of a ledger.</summary>
    /// <param name="path">The ledger's local file.</param>
    /// <param name="section">Which document: <see cref="REPORT"/>, <see cref="PROVENANCE"/> or <see cref="INTEGRITY"/>.</param>
    /// <returns>The document's bytes.</returns>
    /// <exception cref="InvalidDataException">The file is not a ledger.</exception>
    public static byte[] Read(string path, int section)
    {
        using var file = Open(path, section, out var length);
        if (length > Array.MaxLength)
            throw new InvalidDataException("A document of the ledger is larger than memory holds in one piece.");

        var document = new byte[length];
        file.ReadExactly(document);
        return document;
    }

    /// <summary>Copies one document of a ledger to a file of its own: for the outputs, which may be large.</summary>
    /// <param name="path">The ledger's local file.</param>
    /// <param name="section">Which document.</param>
    /// <param name="destination">The file to write (made new).</param>
    /// <exception cref="InvalidDataException">The file is not a ledger.</exception>
    public static void CopyTo(string path, int section, string destination)
    {
        using var file = Open(path, section, out var length);
        using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[COPY_BUFFER_BYTES];
        while (length > 0)
        {
            var read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (read == 0)
                throw new InvalidDataException("The ledger ends inside a document.");

            target.Write(buffer, 0, read);
            length -= read;
        }
    }

    /// <summary>Opens a ledger's stream on one of its documents: the stream stands at the document's first byte.</summary>
    /// <param name="path">The ledger's local file.</param>
    /// <param name="section">Which document.</param>
    /// <param name="length">The document's length.</param>
    /// <returns>The stream; the caller disposes it.</returns>
    /// <exception cref="InvalidDataException">The file is not a ledger.</exception>
    public static FileStream Open(string path, int section, out long length)
    {
        if (section is < 0 or >= SECTIONS)
            throw new ArgumentOutOfRangeException(nameof(section));

        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> header = stackalloc byte[HEADER_LENGTH];
            if (file.ReadAtLeast(header, HEADER_LENGTH, throwOnEndOfStream: false) < HEADER_LENGTH
                || !header[..MAGIC.Length].SequenceEqual(MAGIC) || header[MAGIC.Length] != VERSION)
                throw new InvalidDataException("The blob is not a Verify ledger of this version.");

            for (var current = 0; ; current++)
            {
                length = ReadLength(file);
                if (length < 0 || length > file.Length - file.Position)
                    throw new InvalidDataException("The ledger ends inside a document.");

                if (current == section)
                    return file;

                file.Seek(length, SeekOrigin.Current);
            }
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static void WriteLength(Stream stream, long length)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, length);
        stream.Write(bytes);
    }

    private static long ReadLength(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        if (stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) < bytes.Length)
            throw new InvalidDataException("The ledger ends inside a document.");

        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    #endregion
}
