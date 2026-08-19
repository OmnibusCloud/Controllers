using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OutWit.Controller.Visualization.ParaView.Tests.Utils;

/// <summary>
/// Minimal well-formed PNG / JPEG-header writers for output-validation tests (no imaging dependency).
/// </summary>
internal static class TestImages
{
    #region Functions

    public static byte[] Png(int width, int height, bool alpha)
    {
        var channels = alpha ? 4 : 3;
        var raw = new byte[(width * channels + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var offset = y * (width * channels + 1);
            raw[offset] = 0;
            for (var i = 1; i < width * channels + 1; i++)
                raw[offset + i] = (byte)(i * 7);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw);

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = (byte)(alpha ? 6 : 2);
        Chunk(output, "IHDR", header);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    public static byte[] JpegHeaderOnly(int width, int height)
    {
        using var output = new MemoryStream();
        output.Write([0xFF, 0xD8]);
        Segment(output, 0xE0, [0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);

        var sof = new byte[15];
        sof[0] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(1, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(sof.AsSpan(3, 2), (ushort)width);
        sof[5] = 3;
        sof[6] = 1; sof[7] = 0x22; sof[8] = 0;
        sof[9] = 2; sof[10] = 0x11; sof[11] = 1;
        sof[12] = 3; sof[13] = 0x11; sof[14] = 1;
        Segment(output, 0xC0, sof);

        output.Write([0xFF, 0xD9]);
        return output.ToArray();
    }

    #endregion

    #region Tools

    private static void Segment(Stream output, byte marker, byte[] payload)
    {
        output.Write([0xFF, marker]);
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
        output.Write(length);
        output.Write(payload);
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes, data));
        output.Write(crc);
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in first.Concat(second))
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return crc ^ 0xFFFFFFFFu;
    }

    #endregion
}
