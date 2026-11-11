namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// One shard's place in <see cref="ShardShareProgress"/>: it says how much of the shard has
/// arrived, that the delivery is over, and - when disposed - that the shard has left the node.
/// A shard that never fetched (refused, failed before the fetch) still closes its delivery on
/// the way out, so the share's counts settle.
/// </summary>
public sealed class ShardShareTicket : IDisposable
{
    #region Fields

    private readonly ShardShareProgress m_progress;

    private long m_received;

    private int m_fetched;

    private int m_disposed;

    #endregion

    #region Constructors

    internal ShardShareTicket(ShardShareProgress progress)
    {
        m_progress = progress;
    }

    #endregion

    #region Functions

    /// <summary>The shard's bytes on disk so far; a fetch that starts over reports a smaller number.</summary>
    /// <param name="totalBytes">Bytes of this shard received so far.</param>
    public void Received(long totalBytes)
    {
        var difference = totalBytes - Interlocked.Exchange(ref m_received, totalBytes);
        if (difference != 0)
            m_progress.Received(difference);
    }

    /// <summary>The shard has arrived whole.</summary>
    public void Fetched()
    {
        if (Interlocked.Exchange(ref m_fetched, 1) == 0)
            m_progress.Fetched();
    }

    #endregion

    #region IDisposable

    /// <summary>The shard has left the node: processed and uploaded, or failed.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            return;

        Fetched();
        m_progress.Left();
    }

    #endregion
}
