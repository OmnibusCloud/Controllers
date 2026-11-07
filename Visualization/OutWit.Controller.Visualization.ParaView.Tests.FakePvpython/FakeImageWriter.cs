using System.Buffers.Binary;
using System.IO.Compression;

namespace OutWit.Controller.Visualization.ParaView.Tests.FakePvpython;

/// <summary>
/// Writes minimal but well-formed images without any imaging dependency: a solid PNG (RGB or RGBA)
/// with correct CRCs and zlib-compressed scanlines, and a JPEG consisting of SOI + a SOF0 frame header
/// + EOI (enough for header-level validation; no scan data).
/// </summary>
internal static class FakeImageWriter
{
    #region Fields

    private static readonly uint[] CRC_TABLE = BuildCrcTable();

    #endregion

    #region Functions

    public static void WritePng(string path, int width, int height, bool alpha, byte shade)
    {
        var channels = alpha ? 4 : 3;
        var raw = new byte[(width * channels + 1) * height];
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            raw[offset++] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                raw[offset++] = shade;
                raw[offset++] = (byte)(255 - shade);
                raw[offset++] = 96;
                if (alpha)
                    raw[offset++] = 200;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw);

        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = (byte)(alpha ? 6 : 2);
        header[10] = 0;
        header[11] = 0;
        header[12] = 0;
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
    }

    public static void WriteJpegHeaderOnly(string path, int width, int height)
    {
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        output.Write([0xFF, 0xD8]);

        // APP0 JFIF segment.
        var app0 = new byte[] { 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 };
        WriteSegment(output, 0xE0, app0);

        // SOF0: precision, height, width, 3 components.
        var sof = new byte[15];
        sof[0] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(1, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(3, 2), (ushort)width);
        sof[5] = 3;
        sof[6] = 1; sof[7] = 0x22; sof[8] = 0;
        sof[9] = 2; sof[10] = 0x11; sof[11] = 1;
        sof[12] = 3; sof[13] = 0x11; sof[14] = 1;
        WriteSegment(output, 0xC0, sof);

        output.Write([0xFF, 0xD9]);
    }

    #endregion

    #region Tools

    private static void WriteSegment(Stream output, byte marker, byte[] payload)
    {
        output.Write([0xFF, marker]);
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
        output.Write(length);
        output.Write(payload);
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in first)
            crc = CRC_TABLE[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in second)
            crc = CRC_TABLE[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }

        return table;
    }

    #endregion
}
