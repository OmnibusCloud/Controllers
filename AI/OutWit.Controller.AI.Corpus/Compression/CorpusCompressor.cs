using ZstdSharp;

namespace OutWit.Controller.AI.Corpus.Compression;

/// <summary>
/// The output compression of the controller: zstd through a managed port (the same code on
/// every platform), one pinned level, one thread, no dictionary — so the compressed bytes of
/// a shard product are a function of its content alone and the content hash the pipeline
/// records can be taken over the bytes a node actually uploaded.
/// </summary>
public static class CorpusCompressor
{
    #region Constants

    /// <summary>The pinned zstd level: a bump is a pipeline version change.</summary>
    public const int LEVEL = 9;

    #endregion

    #region Functions

    /// <summary>
    /// Compresses a buffer.
    /// </summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The zstd frame.</returns>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var compressor = new Compressor(LEVEL);
        return compressor.Wrap(data).ToArray();
    }

    /// <summary>
    /// Decompresses a frame produced by <see cref="Compress"/>.
    /// </summary>
    /// <param name="frame">The zstd frame.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Decompress(ReadOnlySpan<byte> frame)
    {
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(frame).ToArray();
    }

    /// <summary>
    /// Wraps a destination stream for streamed compression (a shard product written document by
    /// document); disposing the returned stream finishes the frame.
    /// </summary>
    /// <param name="destination">Where the frame goes.</param>
    /// <returns>The stream to write the content to.</returns>
    public static Stream CompressTo(Stream destination)
    {
        return new CompressionStream(destination, LEVEL, leaveOpen: true);
    }

    /// <summary>
    /// Wraps a source stream for streamed decompression.
    /// </summary>
    /// <param name="source">The frame.</param>
    /// <returns>The stream to read the content from.</returns>
    public static Stream DecompressFrom(Stream source)
    {
        return new DecompressionStream(source, leaveOpen: true);
    }

    #endregion
}
