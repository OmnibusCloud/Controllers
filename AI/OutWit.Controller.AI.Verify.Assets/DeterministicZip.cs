using System.Buffers.Binary;
using System.Text;

namespace OutWit.Controller.AI.Verify.Assets;

/// <summary>
/// A zip writer whose output depends on the entries alone: stored (no compression — the
/// release archive compresses the whole set, and CPython's zipimport then reads entries
/// without inflating), names sorted ordinally, every timestamp 1980-01-01 00:00, no extra
/// fields, no comments, no attributes. The same files give the same bytes on any machine,
/// which is what lets the layer be pinned by one hash.
/// </summary>
internal static class DeterministicZip
{
    #region Constants

    private const uint LOCAL_HEADER = 0x04034B50;
    private const uint CENTRAL_HEADER = 0x02014B50;
    private const uint END_OF_CENTRAL = 0x06054B50;
    private const ushort VERSION = 20;
    private const ushort DOS_DATE_1980_01_01 = 0x0021;

    #endregion

    #region Fields

    private static readonly uint[] s_crcTable = BuildCrcTable();

    #endregion

    #region Functions

    /// <summary>
    /// Writes the entries as one zip file.
    /// </summary>
    /// <param name="zipPath">The file to write (replaced).</param>
    /// <param name="entries">Entry name (forward slashes) and bytes; written in ordinal name order.</param>
    /// <exception cref="InvalidOperationException">Too many entries or too much data for a plain (non-ZIP64) archive.</exception>
    public static void Write(string zipPath, IReadOnlyList<ZipEntryBytes> entries)
    {
        var ordered = entries.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToList();
        if (ordered.Count > ushort.MaxValue)
            throw new InvalidOperationException($"{ordered.Count} entries: a plain zip holds at most {ushort.MaxValue}");

        using var stream = File.Create(zipPath);
        var central = new MemoryStream();

        foreach (var entry in ordered)
        {
            var name = Encoding.ASCII.GetBytes(entry.Name);
            if (name.Length != entry.Name.Length)
                throw new InvalidOperationException($"entry name is not ASCII: {entry.Name}");

            var crc = Crc32(entry.Bytes);
            var offset = checked((uint)stream.Position);

            WriteUInt32(stream, LOCAL_HEADER);
            WriteUInt16(stream, VERSION);
            WriteUInt16(stream, 0);              // flags
            WriteUInt16(stream, 0);              // stored
            WriteUInt16(stream, 0);              // time 00:00
            WriteUInt16(stream, DOS_DATE_1980_01_01);
            WriteUInt32(stream, crc);
            WriteUInt32(stream, (uint)entry.Bytes.Length);
            WriteUInt32(stream, (uint)entry.Bytes.Length);
            WriteUInt16(stream, (ushort)name.Length);
            WriteUInt16(stream, 0);              // extra
            stream.Write(name);
            stream.Write(entry.Bytes);

            WriteUInt32(central, CENTRAL_HEADER);
            WriteUInt16(central, VERSION);       // made by (MS-DOS attributes)
            WriteUInt16(central, VERSION);
            WriteUInt16(central, 0);
            WriteUInt16(central, 0);
            WriteUInt16(central, 0);
            WriteUInt16(central, DOS_DATE_1980_01_01);
            WriteUInt32(central, crc);
            WriteUInt32(central, (uint)entry.Bytes.Length);
            WriteUInt32(central, (uint)entry.Bytes.Length);
            WriteUInt16(central, (ushort)name.Length);
            WriteUInt16(central, 0);             // extra
            WriteUInt16(central, 0);             // comment
            WriteUInt16(central, 0);             // disk
            WriteUInt16(central, 0);             // internal attributes
            WriteUInt32(central, 0);             // external attributes
            WriteUInt32(central, offset);
            central.Write(name);
        }

        var centralOffset = checked((uint)stream.Position);
        central.Position = 0;
        central.CopyTo(stream);

        WriteUInt32(stream, END_OF_CENTRAL);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, (ushort)ordered.Count);
        WriteUInt16(stream, (ushort)ordered.Count);
        WriteUInt32(stream, (uint)central.Length);
        WriteUInt32(stream, centralOffset);
        WriteUInt16(stream, 0);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
            crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }

        return table;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    #endregion
}

/// <summary>One zip entry: its name inside the archive (forward slashes) and its bytes.</summary>
internal sealed record ZipEntryBytes(string Name, byte[] Bytes);
