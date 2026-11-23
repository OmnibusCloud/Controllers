using System.IO.Compression;
using MemoryPack;
using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// Reads a package of results: one row at a time, each frame held to its bound before it is
/// decoded. A node wrote the package, so nothing in it is believed: the stream is unpacked in
/// pieces and stops at the first frame longer than a row may be, at more rows than the task
/// package had problems, and at more bytes in all than that many rows may hold. A blob of a few
/// kilobytes that unpacks to a gigabyte is refused after the bound, not after the gigabyte.
/// Whatever the decoder throws is a frame that cannot be read, whatever its type.
/// <para>
/// What a row says is not looked at here: whether its words are a row's words, its index one of
/// the package's, its ticket the package's and its digest its own is for whoever reads the rows.
/// </para>
/// </summary>
internal static class SmtResultPackageReader
{
    #region Functions

    /// <summary>
    /// Reads the rows of a package of results from a file.
    /// </summary>
    /// <param name="path">The blob's local file.</param>
    /// <param name="maxRows">The most rows the package may hold: the task package's own number of problems.</param>
    /// <param name="maxImageBytes">The most bytes the package may unpack to; 0 for a frame at its bound a row (<see cref="SmtPackageCodec.MaxResultsImageBytes"/>).</param>
    /// <returns>The rows, in the order written, each read when asked for.</returns>
    /// <exception cref="InvalidDataException">The file is not a package of results this module reads, is damaged, holds more rows than it may, or unpacks to more than it may. Thrown while the rows are read.</exception>
    public static IEnumerable<SmtResultData> Read(string path, int maxRows, long maxImageBytes = 0)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        SmtPackageCodec.ReadHeader(file, SmtPackageCodec.KIND_RESULTS);

        using var brotli = new BrotliStream(file, CompressionMode.Decompress);

        var most = maxImageBytes > 0 ? maxImageBytes : SmtPackageCodec.MaxResultsImageBytes(maxRows);
        long total = 0;
        var rows = 0;

        while (SmtPackageCodec.ReadFrame(brotli, SmtPackageCodec.MAX_RESULT_FRAME_BYTES) is { } image)
        {
            total += SmtPackageCodec.FRAME_HEAD_LENGTH + image.Length;
            if (total > most)
                throw new InvalidDataException("The result package unpacks to more than its rows may hold.");

            if (++rows > maxRows)
                throw new InvalidDataException("The result package holds more rows than its task package had problems.");

            yield return Decode(image);
        }
    }

    private static SmtResultData Decode(byte[] image)
    {
        try
        {
            return MemoryPackSerializer.Deserialize<SmtResultData>(image) ?? throw new InvalidDataException("A frame of the result package holds no row.");
        }
        catch (Exception exception) when (exception is not (InvalidDataException or OutOfMemoryException))
        {
            throw new InvalidDataException("A frame of the result package cannot be read as a row.");
        }
    }

    #endregion
}
