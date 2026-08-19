using System.Buffers.Binary;
using OutWit.Controller.Visualization.ParaView.Model;

namespace OutWit.Controller.Visualization.ParaView.Output;

/// <summary>
/// Header-level facts about a rendered image, read without decoding pixels: the format the file
/// signature claims, its dimensions, and (PNG) whether it carries an alpha channel.
/// </summary>
/// <param name="Format">Format identified by signature.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="HasAlpha">True for PNG color types with an alpha channel; false for JPEG.</param>
public sealed record ParaViewImageInfo(ParaViewImageFormat Format, int Width, int Height, bool HasAlpha)
{
    #region Constants

    private static readonly byte[] PNG_SIGNATURE = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private const int MAX_JPEG_SCAN_BYTES = 4 * 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>
    /// Reads the header of an image file.
    /// </summary>
    /// <param name="path">Image path.</param>
    /// <returns>The info, or null when the file is neither a PNG nor a JPEG with a readable header.</returns>
    public static ParaViewImageInfo? TryRead(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return TryRead(stream);
    }

    /// <summary>
    /// Reads the header of an image stream.
    /// </summary>
    /// <param name="stream">Readable, seekable stream positioned at the start.</param>
    /// <returns>The info, or null when unrecognized.</returns>
    public static ParaViewImageInfo? TryRead(Stream stream)
    {
        Span<byte> head = stackalloc byte[32];
        var read = ReadFully(stream, head);
        if (read < 4)
            return null;

        if (read >= 24 && head[..8].SequenceEqual(PNG_SIGNATURE))
            return ReadPng(head);

        if (head[0] == 0xFF && head[1] == 0xD8)
        {
            stream.Position = 2;
            return ReadJpeg(stream);
        }

        return null;
    }

    #endregion

    #region Tools

    private static ParaViewImageInfo? ReadPng(ReadOnlySpan<byte> head)
    {
        // Signature (8) + IHDR length (4) + "IHDR" (4) + width (4) + height (4) + bit depth (1) + color type (1)
        if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R')
            return null;

        var width = BinaryPrimitives.ReadInt32BigEndian(head[16..20]);
        var height = BinaryPrimitives.ReadInt32BigEndian(head[20..24]);
        if (width <= 0 || height <= 0)
            return null;

        var hasAlpha = false;
        if (head.Length > 25)
        {
            var colorType = head[25];
            hasAlpha = colorType is 4 or 6;
        }

        return new ParaViewImageInfo(ParaViewImageFormat.Png, width, height, hasAlpha);
    }

    private static ParaViewImageInfo? ReadJpeg(Stream stream)
    {
        Span<byte> header = stackalloc byte[9];
        long scanned = 2;

        while (scanned < MAX_JPEG_SCAN_BYTES)
        {
            var markerStart = stream.ReadByte();
            if (markerStart < 0)
                return null;

            scanned++;
            if (markerStart != 0xFF)
                continue;

            int marker;
            do
            {
                marker = stream.ReadByte();
                scanned++;
            }
            while (marker == 0xFF);

            if (marker < 0)
                return null;

            // Standalone markers without a length.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
                continue;

            if (ReadFully(stream, header[..2]) < 2)
                return null;

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(header[..2]);
            if (segmentLength < 2)
                return null;

            // SOF0..SOF15 except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (ReadFully(stream, header[..5]) < 5)
                    return null;

                var height = BinaryPrimitives.ReadUInt16BigEndian(header[1..3]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(header[3..5]);
                if (width == 0 || height == 0)
                    return null;

                return new ParaViewImageInfo(ParaViewImageFormat.Jpeg, width, height, false);
            }

            if (marker == 0xDA)
                return null;

            stream.Seek(segmentLength - 2, SeekOrigin.Current);
            scanned += segmentLength - 2;
        }

        return null;
    }

    private static int ReadFully(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read <= 0)
                break;

            total += read;
        }

        return total;
    }

    #endregion
}
