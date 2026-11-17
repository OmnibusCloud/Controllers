using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// What bounds the reading of one shard, whatever its bytes say: the largest record held in
/// memory, by format, and the most the file may unpack to. Each is a function of the shard alone,
/// so a shard past a limit ends the same way on every node.
/// </summary>
public static class ShardLimits
{
    #region Constants

    /// <summary>
    /// The largest WET record held: a conversion record is the text of one page, and Common Crawl
    /// cuts a page at 1 MiB of its response (5 MiB for a PDF). A larger one is skipped and counted
    /// (<see cref="ShardProcessor.DROP_RECORD_OVERSIZED"/>), not split into millions of words.
    /// </summary>
    public const int MAX_WET_RECORD_BYTES = 8 * 1024 * 1024;

    /// <summary>The largest WARC record held: a response with its body, a PDF among them (the PDF stage's own input cap is the same).</summary>
    public const int MAX_WARC_RECORD_BYTES = WarcReader.DEFAULT_MAX_RECORD_BYTES;

    /// <summary>How many times its packed size a shard may unpack to: Common Crawl's WET files unpack to 3 to 4 times, its WARC files to 4 to 5.</summary>
    public const int UNPACK_RATIO = 32;

    /// <summary>What any shard may unpack to, however small it is packed.</summary>
    public const long MIN_UNPACKED_BYTES = 64L * 1024 * 1024;

    /// <summary>What no shard may unpack past: sixteen times the 1 GiB a shard may be on the wire.</summary>
    public const long MAX_UNPACKED_BYTES = 16L * 1024 * 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>The most bytes a shard of a packed size may unpack to; past it the shard is <c>Oversized</c> (<c>oversized_unpacked</c>).</summary>
    /// <param name="packedBytes">The shard's size as fetched.</param>
    /// <returns>The ceiling, <see cref="MIN_UNPACKED_BYTES"/> to <see cref="MAX_UNPACKED_BYTES"/>.</returns>
    public static long MaxUnpackedBytes(long packedBytes)
    {
        var byRatio = Math.Max(0, packedBytes) > MAX_UNPACKED_BYTES / UNPACK_RATIO ? MAX_UNPACKED_BYTES : Math.Max(0, packedBytes) * UNPACK_RATIO;
        return Math.Clamp(byRatio, MIN_UNPACKED_BYTES, MAX_UNPACKED_BYTES);
    }

    /// <summary>The largest record of a shard of the given format that is held in memory.</summary>
    /// <param name="format">What the shard holds.</param>
    /// <returns>The cap, bytes.</returns>
    public static int MaxRecordBytes(CorpusSourceFormat format)
    {
        return format == CorpusSourceFormat.Wet ? MAX_WET_RECORD_BYTES : MAX_WARC_RECORD_BYTES;
    }

    #endregion
}
