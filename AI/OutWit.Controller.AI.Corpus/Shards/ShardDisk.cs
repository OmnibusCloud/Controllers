using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The room a shard needs on a node's disk and the room there is. A node holds a few shards at a
/// time (<see cref="ShardTaskRunner.FETCH_AHEAD"/> beyond the ones it processes), each with its
/// products beside it until they are uploaded; a node whose disk cannot take one shard knows so
/// before it fetches a byte. A shard needs itself, the most its products may be
/// (<see cref="ShardProductLimits"/>, a function of the shard's size alone) and a margin; a shard
/// without a size of its own needs that at the cap it is held to. Each task reserves the room
/// before it fetches (<see cref="ShardDiskReservations"/>).
/// </summary>
public static class ShardDisk
{
    #region Constants

    /// <summary>Room kept beyond the shard and its products: the PDF stage's files, the blob client's own.</summary>
    public const long MARGIN_BYTES = 64L * 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>The free bytes a shard of a known size needs: itself, the most its products may be under the pipeline, and the margin.</summary>
    /// <param name="shardBytes">The shard's size on the wire (or the cap it is held to when it has none).</param>
    /// <param name="pipeline">The pipeline (whether it signs, and with how many hashes); null for the default one, which signs with 128.</param>
    /// <returns>The bytes.</returns>
    public static long Needed(long shardBytes, CorpusPipelineData? pipeline = null)
    {
        var size = Math.Max(0, shardBytes);
        return size + ShardProductLimits.MaxProductBytes(size, pipeline) + MARGIN_BYTES;
    }

    /// <summary>The bytes a node needs free for its part of a run: the shards it holds at once, each at <see cref="Needed"/>.</summary>
    /// <param name="largestShardBytes">The largest shard of the run.</param>
    /// <param name="degree">The shards the node processes at once.</param>
    /// <param name="pipeline">The pipeline; null for the default one.</param>
    /// <returns>The bytes.</returns>
    public static long NeededByNode(long largestShardBytes, int degree, CorpusPipelineData? pipeline = null)
    {
        return (Math.Max(1, degree) + (long)ShardTaskRunner.FETCH_AHEAD) * Needed(largestShardBytes, pipeline);
    }

    /// <summary>The preflight's line on the disk a node needs for a run.</summary>
    /// <param name="largestShardBytes">The run's largest shard; 0 or less when no size is known.</param>
    /// <param name="capBytes">The cap a shard is held to on the wire.</param>
    /// <param name="pipeline">The pipeline; null for the default one.</param>
    /// <returns>For example "Node disk: a node holds at most its degree and 2 more shards at a time ...".</returns>
    public static string Describe(long largestShardBytes, long capBytes, CorpusPipelineData? pipeline = null)
    {
        var known = largestShardBytes > 0;
        var perShard = Needed(known ? largestShardBytes : capBytes, pipeline);
        const int exampleDegree = 8;

        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Node disk: a node holds at most its degree and {ShardTaskRunner.FETCH_AHEAD} more shards at a time (its degree is the shards it processes at once: one per core, within its memory), never its whole share. " +
            $"Each held shard reserves {perShard} bytes before it is fetched ({(known ? $"the largest shard, {largestShardBytes} bytes" : $"no shard size is known: the cap, {capBytes} bytes")}, the most its products may be, and a margin): " +
            $"{NeededByNode(known ? largestShardBytes : capBytes, exampleDegree, pipeline)} bytes for a node of degree {exampleDegree}. A shard waits while the node's other shards hold the room; a node without room for one shard refuses its batch and the platform moves the work.");
    }

    /// <summary>The free bytes of the volume that holds a folder.</summary>
    /// <param name="directory">The folder (it need not exist yet).</param>
    /// <returns>The free bytes, or -1 when the volume does not say.</returns>
    public static long FreeBytes(string directory)
    {
        try
        {
            var path = Path.GetFullPath(directory);
            while (!Directory.Exists(path))
            {
                var parent = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(parent) || parent == path)
                    return -1;

                path = parent;
            }

            var volume = OperatingSystem.IsWindows() ? Path.GetPathRoot(path) : path;
            return string.IsNullOrEmpty(volume) ? -1 : new DriveInfo(volume).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return -1;
        }
    }

    /// <summary>Whether a folder's volume has room for a shard, with nothing else reserved; a volume that does not say has.</summary>
    /// <param name="directory">The folder the shard is fetched into.</param>
    /// <param name="shardBytes">The shard's size on the wire, or the cap it is held to when it has none.</param>
    /// <param name="pipeline">The pipeline; null for the default one.</param>
    /// <returns>False only when the volume states less free room than the shard needs.</returns>
    public static bool HasRoom(string directory, long shardBytes, CorpusPipelineData? pipeline = null)
    {
        var free = ShardDiskReservations.FreeBytes(directory);
        return free < 0 || free >= Needed(shardBytes, pipeline);
    }

    #endregion
}
