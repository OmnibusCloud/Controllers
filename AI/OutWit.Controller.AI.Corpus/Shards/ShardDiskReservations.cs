namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// The room on the node's disk its shards have reserved. A task reserves the most its shard may
/// take (<see cref="ShardDisk.Needed"/>: the shard, the most its products may be, a margin)
/// before it fetches a byte, and gives it back when its files are gone. The question is asked of
/// the volume's free room less what the node's other shards hold reserved, so a window of shards
/// that each fit alone is not told there is room for all of them.
/// <list type="bullet">
/// <item>A shard whose room is not there while other shards hold theirs waits: their room comes
/// back when they finish, and nothing of the waiting shard is on the disk yet.</item>
/// <item>A shard that does not fit with nothing else reserved never will: it is a fault of the
/// node (<c>disk_space</c>) and another node runs it.</item>
/// <item>A volume that does not say how much room it has takes every shard, as before.</item>
/// <item>A task gives its room back when it ends, also when an upload call it gave up still
/// reads its products: those stay until the call ends, and the volume's free room counts their
/// bytes. So room is never held for a call that may not end, and a shard that does not fit
/// beside such a file is refused, not left waiting.</item>
/// </list>
/// The room a shard holds counts in full until it is given back, also once part of it is used:
/// the question errs on the side of too little room, never of too much.
/// </summary>
public static class ShardDiskReservations
{
    #region Fields

    private static readonly object LOCK = new();

    private static long m_reserved;

    private static int m_holders;

    private static TaskCompletionSource m_released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    #endregion

    #region Functions

    /// <summary>Reserves the room a shard may take, waiting while other shards hold the room it needs.</summary>
    /// <param name="directory">The folder the shard is fetched into (its volume is asked).</param>
    /// <param name="bytes">The room the shard may take at the most.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <returns>The reservation, to be disposed when the shard's files are gone; null when the shard does not fit with nothing else reserved.</returns>
    public static async Task<ShardDiskReservation?> ReserveAsync(string directory, long bytes, CancellationToken cancellationToken)
    {
        var needed = Math.Max(0, bytes);

        while (true)
        {
            Task released;
            lock (LOCK)
            {
                var free = FreeBytes(directory);
                if (free < 0 || free - m_reserved >= needed)
                {
                    m_reserved += needed;
                    m_holders++;
                    return new ShardDiskReservation(needed);
                }

                // Nothing else holds room: the shard does not fit on this disk at all.
                if (m_holders == 0)
                    return null;

                released = m_released.Task;
            }

            await released.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Gives a shard's room back and wakes the shards that wait for room.</summary>
    /// <param name="bytes">The room the shard held.</param>
    internal static void Release(long bytes)
    {
        TaskCompletionSource released;
        lock (LOCK)
        {
            m_reserved -= bytes;
            m_holders--;
            released = m_released;
            m_released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        released.TrySetResult();
    }

    #endregion

    #region Properties

    /// <summary>The room the node's shards hold reserved now.</summary>
    public static long Reserved
    {
        get
        {
            lock (LOCK)
                return m_reserved;
        }
    }

    /// <summary>Asks a folder's volume for its free bytes (<see cref="ShardDisk.FreeBytes"/>; a test of the reservation answers for a disk of its own).</summary>
    internal static Func<string, long> FreeBytes { get; set; } = ShardDisk.FreeBytes;

    #endregion
}
