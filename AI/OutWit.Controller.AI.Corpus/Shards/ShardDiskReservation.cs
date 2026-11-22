namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>The room one shard holds on the node's disk (<see cref="ShardDiskReservations"/>); disposing it gives the room back, once.</summary>
public sealed class ShardDiskReservation : IDisposable
{
    #region Fields

    private int m_released;

    #endregion

    #region Constructors

    /// <summary>Records a reservation that has been made.</summary>
    /// <param name="bytes">The room held.</param>
    internal ShardDiskReservation(long bytes)
    {
        Bytes = bytes;
    }

    #endregion

    #region IDisposable

    /// <summary>Gives the room back.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_released, 1) == 0)
            ShardDiskReservations.Release(Bytes);
    }

    #endregion

    #region Properties

    /// <summary>The room held.</summary>
    public long Bytes { get; }

    #endregion
}
