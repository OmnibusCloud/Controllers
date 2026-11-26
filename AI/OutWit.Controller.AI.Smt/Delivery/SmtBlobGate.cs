namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// Lets the first transfer over the node's blob channel go alone. The channel is connected by its
/// first user, and several first users at once each start a connection of their own; on a test
/// fleet a node whose first requests came together (and were retried in step) failed every attempt
/// for minutes. While the gate is closed transfers pass one at a time; the first that succeeds
/// opens it and the rest run side by side. A failure closes it again, since a failure is what a
/// lost channel looks like.
/// <para>
/// A transfer has a time to finish in. The blob channel's calls take no token, and one that never
/// returns would hold the gate's one slot, and with it every transfer of every job on the node,
/// for as long as the process lives. A transfer that runs out of its time counts as failed: the
/// slot is free again, and the caller may try once more. The call itself goes on, since nothing
/// can stop it: the next transfer passes the gate beside it, and the caller that gave it up waits
/// for it a little longer before it tries again (<see cref="SmtPackageTransfer"/>).
/// </para>
/// </summary>
internal sealed class SmtBlobGate
{
    #region Fields

    private readonly SemaphoreSlim m_single = new(1, 1);
    private volatile bool m_open;

    #endregion

    #region Functions

    /// <summary>Runs one attempt of a transfer through the gate.</summary>
    /// <typeparam name="T">What the transfer returns.</typeparam>
    /// <param name="transfer">One attempt of the transfer.</param>
    /// <param name="timeout">The time the attempt has to finish in.</param>
    /// <param name="cancellationToken">Stops waiting for the gate and for the transfer.</param>
    /// <returns>What the transfer returned.</returns>
    /// <exception cref="TimeoutException">The attempt did not finish in its time; the gate is free again.</exception>
    public async Task<T> RunAsync<T>(Func<Task<T>> transfer, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (m_open)
        {
            try
            {
                return await Bounded(transfer, timeout, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                m_open = false;
                throw;
            }
        }

        await m_single.WaitAsync(cancellationToken);
        try
        {
            var result = await Bounded(transfer, timeout, cancellationToken);
            m_open = true;
            return result;
        }
        finally
        {
            m_single.Release();
        }
    }

    private static async Task<T> Bounded<T>(Func<Task<T>> transfer, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var running = transfer();
        try
        {
            return await running.WaitAsync(timeout, cancellationToken);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException && !running.IsCompleted)
        {
            // The call itself cannot be stopped: whatever it comes to later is looked at, so
            // that it does not end as an exception nobody saw.
            _ = running.ContinueWith(static abandoned => _ = abandoned.Exception, TaskScheduler.Default);
            throw;
        }
    }

    #endregion

    #region Properties

    /// <summary>True once a transfer has succeeded and none has failed since.</summary>
    public bool IsOpen => m_open;

    #endregion
}
