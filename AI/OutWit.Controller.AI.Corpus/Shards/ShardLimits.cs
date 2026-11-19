using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Warc;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// What bounds the fetching and the reading of one shard, whatever its bytes say: the most it
/// may be on the wire (the cap, and the size it was declared with), the largest record held in
/// memory, by format, and the most the file may unpack to. Each is a function of the shard and
/// its task alone, so a shard past a limit ends the same way on every node.
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

    /// <summary>
    /// The slack a declared size has, as a divisor of it: a shard may be an eighth larger than
    /// the manifest declares (or the origin answered) before a node refuses it. A manifest's
    /// size is often an estimate; a shard several times its declaration is a manifest that
    /// promised the contributors less traffic than it asks of them.
    /// </summary>
    public const int DECLARED_SLACK_DIVISOR = 8;

    /// <summary>The least slack a declared size has: a mebibyte, so a declaration of a few kilobytes is not held to the byte.</summary>
    public const long DECLARED_SLACK_MIN_BYTES = 1024 * 1024;

    /// <summary>
    /// The fewest bytes of the unpacked file one record takes: a version line (<c>WARC/</c> and
    /// its line feed, 6 bytes), a <c>Content-Length</c> header of one digit (17) and the blank line
    /// that ends the headers (1). The reader takes no record without the three, so what a shard
    /// may unpack to bounds the records it can hold.
    /// </summary>
    public const int MIN_RECORD_BYTES = 24;

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

    /// <summary>
    /// The most records a shard of a packed size can hold: what it may unpack to, in records of
    /// the fewest bytes a record takes. A node counts a record for each one it read or passed
    /// over, so no honest count is above this; the server holds a result's records to it.
    /// </summary>
    /// <param name="packedBytes">The shard's size as fetched.</param>
    /// <returns>The bound: 2,796,202 for a shard of up to 2 MiB, 715,827,882 for one of 512 MiB and more.</returns>
    public static long MaxRecords(long packedBytes)
    {
        return MaxUnpackedBytes(packedBytes) / MIN_RECORD_BYTES;
    }

    /// <summary>The most bytes a shard of a declared size may be on the wire: the declaration and its slack.</summary>
    /// <param name="declaredBytes">What the manifest declares or the origin answered; 0 or less = the shard has no size of its own.</param>
    /// <returns>The bound; <see cref="long.MaxValue"/> for a shard with no size of its own (the cap alone holds it).</returns>
    public static long DeclaredCap(long declaredBytes)
    {
        if (declaredBytes <= 0)
            return long.MaxValue;

        var slack = Math.Max(declaredBytes / DECLARED_SLACK_DIVISOR, DECLARED_SLACK_MIN_BYTES);
        return declaredBytes > long.MaxValue - slack ? long.MaxValue : declaredBytes + slack;
    }

    /// <summary>
    /// The most bytes a node accepts for a task's shard: the node's own ceiling and the task's cap
    /// (<see cref="Manifests.CorpusTaskPlanner.NodeShardCap"/>), and below them the size the shard
    /// was declared with and its slack. The server holds a result to the same number.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <returns>The bound, 1 byte or more.</returns>
    public static long FetchCap(CorpusTaskData task)
    {
        return Math.Min(Manifests.CorpusTaskPlanner.NodeShardCap(task.MaxShardBytes), DeclaredCap(task.DeclaredBytes));
    }

    /// <summary>Whether a task's shard is held to its declared size rather than to the cap: a shard past the bound is then <c>oversized_declared</c>.</summary>
    /// <param name="task">The task.</param>
    /// <returns>True when the declared size and its slack are below the cap.</returns>
    public static bool DeclaredBinds(CorpusTaskData task)
    {
        return DeclaredCap(task.DeclaredBytes) < Manifests.CorpusTaskPlanner.NodeShardCap(task.MaxShardBytes);
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
