namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The room a shard needs on a node's disk and the room there is. A node holds a few shards at a
/// time (<see cref="ShardTaskRunner.FETCH_AHEAD"/> beyond the ones it processes), each with its
/// products beside it until they are uploaded; a node whose disk cannot take one shard knows so
/// before it fetches a byte.
/// </summary>
public static class ShardDisk
{
    #region Constants

    /// <summary>Room kept beyond the shard and its products: the PDF stage's files, the blob client's own.</summary>
    public const long MARGIN_BYTES = 64L * 1024 * 1024;

    #endregion

    #region Functions

    /// <summary>The free bytes a shard of a known size needs: itself, its products (never more than half of it, compressed) and the margin.</summary>
    /// <param name="shardBytes">The shard's size on the wire.</param>
    /// <returns>The bytes.</returns>
    public static long Needed(long shardBytes)
    {
        var size = Math.Max(0, shardBytes);
        return size + size / 2 + MARGIN_BYTES;
    }

    /// <summary>The bytes a node needs free for its part of a run: the shards it holds at once, each at <see cref="Needed"/>.</summary>
    /// <param name="largestShardBytes">The largest shard of the run.</param>
    /// <param name="degree">The shards the node processes at once.</param>
    /// <returns>The bytes.</returns>
    public static long NeededByNode(long largestShardBytes, int degree)
    {
        return (Math.Max(1, degree) + (long)ShardTaskRunner.FETCH_AHEAD) * Needed(largestShardBytes);
    }

    /// <summary>The preflight's line on the disk a node needs for a run.</summary>
    /// <param name="largestShardBytes">The run's largest shard; 0 or less when no size is known.</param>
    /// <param name="capBytes">The cap a shard is held to on the wire.</param>
    /// <returns>For example "Node disk: a node holds at most its degree and 2 more shards at a time ...".</returns>
    public static string Describe(long largestShardBytes, long capBytes)
    {
        var known = largestShardBytes > 0;
        var perShard = Needed(known ? largestShardBytes : capBytes);
        const int exampleDegree = 8;

        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Node disk: a node holds at most its degree and {ShardTaskRunner.FETCH_AHEAD} more shards at a time (its degree is the shards it processes at once: one per core, within its memory), never its whole share. " +
            $"Each held shard needs {perShard} bytes free ({(known ? $"the largest shard, {largestShardBytes} bytes" : $"no shard size is known: the cap, {capBytes} bytes")}, its products and a margin): " +
            $"{NeededByNode(known ? largestShardBytes : capBytes, exampleDegree)} bytes for a node of degree {exampleDegree}. A node without room for a shard refuses its batch and the platform moves the work.");
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

    /// <summary>Whether a folder's volume has room for a shard; a volume that does not say has.</summary>
    /// <param name="directory">The folder the shard is fetched into.</param>
    /// <param name="shardBytes">The shard's size on the wire; 0 or less = not known (no test).</param>
    /// <returns>False only when the volume states less free room than the shard needs.</returns>
    public static bool HasRoom(string directory, long shardBytes)
    {
        if (shardBytes <= 0)
            return true;

        var free = FreeBytes(directory);
        return free < 0 || free >= Needed(shardBytes);
    }

    #endregion
}
