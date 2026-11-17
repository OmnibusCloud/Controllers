using System.Buffers.Binary;
using System.IO.Compression;
using MemoryPack;
using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// Reads a result package on the server: one batch at a time, each frame held to a bound before
/// it is read. A node wrote the package, so nothing in it is believed: the stream is unpacked in
/// pieces and stops at the first frame that is larger than a frame may be, at more batches or
/// results than the task package had, and at more bytes in all than a result package may hold.
/// A blob of a few kilobytes that unpacks to a gigabyte is refused after the bound, not after
/// the gigabyte.
/// </summary>
public static class VerifyResultPackageReader
{
    #region Constants

    /// <summary>
    /// The most bytes one frame may hold: a result's stdout twice (the task's and the last
    /// case's) and its stderr at their ceilings, as text of two bytes a character, with room
    /// for its case records.
    /// </summary>
    public const int MAX_FRAME_BYTES = 64 << 20;

    /// <summary>
    /// The most bytes a result package may unpack to: the output a package's tasks may keep
    /// (<see cref="VerifyPackagePlanner.MAX_PACKAGE_OUTPUT_BYTES"/>) as text of two bytes a
    /// character, and room for the records around it.
    /// </summary>
    public const long MAX_IMAGE_BYTES = 2 * VerifyPackagePlanner.MAX_PACKAGE_OUTPUT_BYTES + (64L << 20);

    private const int FIRST_FRAME_BUFFER_BYTES = 64 * 1024;

    #endregion

    #region Functions

    /// <summary>
    /// Reads the batches of a result package from a file.
    /// </summary>
    /// <param name="path">The blob's local file.</param>
    /// <param name="maxBatches">The most batches the package may hold (the task package's own count).</param>
    /// <param name="maxResults">The most results the package may hold (the task package's own count).</param>
    /// <returns>The batches, in the order written, each read when asked for.</returns>
    /// <exception cref="InvalidDataException">The file is not a result package this module reads, is damaged, or is larger than a result package may be. Thrown while the batches are read.</exception>
    /// <exception cref="VerifyPackageContentException">The package holds more batches or results than the task package it answers. Thrown while the batches are read.</exception>
    public static IEnumerable<VerifyResultBatchData> Read(string path, int maxBatches, int maxResults)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        VerifyPackageCodec.ReadHeader(file, VerifyPackageCodec.KIND_RESULTS, VerifyPackageCodec.RESULTS_VERSION);

        using var brotli = new BrotliStream(file, CompressionMode.Decompress);

        var head = new byte[VerifyResultPackageWriter.FRAME_HEAD_LENGTH];
        long total = 0;
        var batches = 0;
        var results = 0;
        VerifyResultBatchData? current = null;

        while (true)
        {
            var got = ReadSome(brotli, head);
            if (got == 0)
                break;

            if (got < head.Length)
                throw new InvalidDataException("The result package ends inside a frame.");

            var kind = head[0];
            var length = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(1));
            if (length < 0 || length > MAX_FRAME_BYTES)
                throw new InvalidDataException("A frame of the result package is larger than a frame may be.");

            total += head.Length + length;
            if (total > MAX_IMAGE_BYTES)
                throw new InvalidDataException("The result package unpacks to more than a result package may hold.");

            var image = ReadFrame(brotli, length)
                        ?? throw new InvalidDataException("The result package ends inside a frame.");

            switch (kind)
            {
                case VerifyResultPackageWriter.FRAME_BATCH:
                    if (current != null)
                        yield return current;

                    if (++batches > maxBatches)
                        throw new VerifyPackageContentException("The result package holds more batches than its task package had.");

                    current = Deserialize<VerifyResultBatchData>(image);
                    current.Results = [];
                    break;
                case VerifyResultPackageWriter.FRAME_RESULT:
                    if (current == null)
                        throw new InvalidDataException("A result of the result package belongs to no batch.");

                    if (++results > maxResults)
                        throw new VerifyPackageContentException("The result package holds more results than its task package had tasks.");

                    current.Results.Add(Deserialize<VerifyResultData>(image));
                    break;
                default:
                    throw new InvalidDataException("The result package holds a frame of an unknown kind.");
            }
        }

        if (current != null)
            yield return current;
    }

    private static T Deserialize<T>(byte[] image) where T : class
    {
        try
        {
            return MemoryPackSerializer.Deserialize<T>(image) ?? throw new InvalidDataException("A frame of the result package is empty.");
        }
        catch (MemoryPackSerializationException)
        {
            throw new InvalidDataException("A frame of the result package cannot be read.");
        }
    }

    /// <summary>
    /// Reads a frame of a stated length, or null when the stream ends before it. The buffer grows
    /// with the bytes that arrive, not by the length the frame's head states: a head that claims
    /// a large frame the stream does not hold costs nothing.
    /// </summary>
    private static byte[]? ReadFrame(Stream stream, int length)
    {
        var buffer = new byte[Math.Min(length, FIRST_FRAME_BUFFER_BYTES)];
        var filled = 0;

        while (filled < length)
        {
            if (filled == buffer.Length)
                Array.Resize(ref buffer, (int)Math.Min(length, buffer.Length * 2L));

            int read;
            try
            {
                read = stream.Read(buffer, filled, buffer.Length - filled);
            }
            catch (InvalidOperationException)
            {
                throw new InvalidDataException("The result package's stream is damaged.");
            }
            catch (IOException exception) when (exception is not FileNotFoundException)
            {
                throw new InvalidDataException("The result package's stream is damaged.");
            }

            if (read == 0)
                return null;

            filled += read;
        }

        return buffer;
    }

    /// <summary>Fills a buffer from the stream, or as much of it as the stream still has.</summary>
    private static int ReadSome(Stream stream, byte[] buffer)
    {
        try
        {
            return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        }
        catch (InvalidOperationException)
        {
            // Brotli's word for a stream that is not one.
            throw new InvalidDataException("The result package's stream is damaged.");
        }
        catch (IOException exception) when (exception is not FileNotFoundException)
        {
            throw new InvalidDataException("The result package's stream is damaged.");
        }
    }

    #endregion
}
