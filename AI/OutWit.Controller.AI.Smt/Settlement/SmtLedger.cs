using System.Buffers.Binary;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The settled run as one blob of the host's own: the four documents a lab reads (the results,
/// the report, the provenance, the integrity), and the full models of the rows whose models the
/// results cut short, made in one pass over what the nodes returned. The activities that hand a
/// document to the job take it out of here; none of them reads a node's row again, and none
/// decides anything. The format is the host's alone: an eight-byte header, then each section as
/// an eight-byte length and its bytes. Every section grows with the run, so each is written from
/// a file and handed out to a file, never through memory.
/// </summary>
internal static class SmtLedger
{
    #region Constants

    /// <summary>File name of a ledger blob.</summary>
    public const string FILE_NAME = "smt-ledger.wsl";

    /// <summary>The results: one JSON line a row of the problem set.</summary>
    public const int RESULTS = 0;

    /// <summary>The report.</summary>
    public const int REPORT = 1;

    /// <summary>The provenance document.</summary>
    public const int PROVENANCE = 2;

    /// <summary>The integrity document.</summary>
    public const int INTEGRITY = 3;

    /// <summary>The full models of the rows whose models the results cut short: one JSON line such a row. The host's; no activity hands it to a lab.</summary>
    public const int MODELS = 4;

    private const int SECTIONS = 5;
    private const int HEADER_LENGTH = 8;
    private const byte VERSION = 1;
    private const int COPY_BUFFER_BYTES = 81920;

    private static readonly byte[] MAGIC = "WSLG"u8.ToArray();

    #endregion

    #region Functions

    /// <summary>Writes a ledger from the files its sections were written to: every section is copied, none is held.</summary>
    /// <param name="path">The file to write (made new).</param>
    /// <param name="sections">The files of the sections, in the order of the section numbers.</param>
    public static void Write(string path, IReadOnlyList<string> sections)
    {
        if (sections.Count != SECTIONS)
            throw new ArgumentException($"a ledger has {SECTIONS} sections", nameof(sections));

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, COPY_BUFFER_BYTES);
        file.Write(MAGIC);
        file.WriteByte(VERSION);
        file.Write([0, 0, 0]);

        foreach (var section in sections)
        {
            using var document = new FileStream(section, FileMode.Open, FileAccess.Read, FileShare.Read, COPY_BUFFER_BYTES, FileOptions.SequentialScan);
            WriteLength(file, document.Length);
            document.CopyTo(file, COPY_BUFFER_BYTES);
        }
    }

    /// <summary>Reads one section of a ledger into memory: for a section known to be small, and for tests.</summary>
    /// <param name="path">The ledger's local file.</param>
    /// <param name="section">Which section.</param>
    /// <returns>The section's bytes.</returns>
    /// <exception cref="InvalidDataException">The file is not a ledger.</exception>
    public static byte[] Read(string path, int section)
    {
        using var file = Open(path, section, out var length);
        if (length > Array.MaxLength)
            throw new InvalidDataException("A section of the ledger is larger than memory holds in one piece.");

        var bytes = new byte[length];
        file.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>Copies one section of a ledger to a file of its own.</summary>
    /// <param name="path">The ledger's local file.</param>
    /// <param name="section">Which section.</param>
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
                throw new InvalidDataException("The ledger ends inside a section.");

            target.Write(buffer, 0, read);
            length -= read;
        }
    }

    /// <summary>Opens a ledger's stream on one of its sections: the stream stands at the section's first byte.</summary>
    /// <param name="path">The ledger's local file.</param>
    /// <param name="section">Which section.</param>
    /// <param name="length">The section's length.</param>
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
                throw new InvalidDataException("The blob is not an Smt ledger of this version.");

            for (var current = 0; ; current++)
            {
                length = ReadLength(file);
                if (length < 0 || length > file.Length - file.Position)
                    throw new InvalidDataException("The ledger ends inside a section.");

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

    #endregion

    #region Tools

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
            throw new InvalidDataException("The ledger ends inside a section.");

        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    #endregion
}
