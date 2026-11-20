namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>
/// Lets the first transfer over the node's blob channel go alone. The channel is connected
/// by its first user, and several first users at once each start a connection of their own;
/// on the test fleet a node whose first two shards asked for the language model together
/// (and retried in step) failed every attempt for minutes. While the gate is closed transfers
/// pass one at a time; the first that succeeds opens it and the rest run side by side. A
/// failure closes it again, since a failure is what a lost channel looks like.
/// <para>
/// An attempt has a time to finish in, and the job's token ends the wait for it. The blob
/// channel's calls take no token, and one that never returns would otherwise hold the gate's
/// one slot, and with it every Corpus transfer of every job on the node, for as long as the
/// process lives. An attempt that runs out of its time counts as failed and one that is
/// cancelled stops being waited for: either way the slot is free again. The call itself cannot
/// be stopped from here: the caller is handed it (<c>abandoned</c>) and decides what to wait for;
/// whatever it comes to is looked at.
/// </para>
/// </summary>
public sealed class ShardBlobGate
{
    #region Fields

    private readonly SemaphoreSlim m_single = new(1, 1);
    private volatile bool m_open;

    #endregion

    #region Functions

    /// <summary>Runs one attempt of a transfer through the gate, without a time limit of the gate's own (the job's token still ends the wait).</summary>
    /// <typeparam name="T">What the transfer returns.</typeparam>
    /// <param name="transfer">One attempt of the transfer.</param>
    /// <param name="cancellationToken">Stops waiting for the gate and for the transfer.</param>
    /// <returns>What the transfer returned.</returns>
    public Task<T> RunAsync<T>(Func<Task<T>> transfer, CancellationToken cancellationToken)
    {
        return RunAsync(transfer, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    /// <summary>Runs one attempt of a transfer through the gate.</summary>
    /// <typeparam name="T">What the transfer returns.</typeparam>
    /// <param name="transfer">One attempt of the transfer.</param>
    /// <param name="timeout">The time the attempt has to finish in.</param>
    /// <param name="cancellationToken">Stops waiting for the gate and for the transfer.</param>
    /// <param name="abandoned">Handed the call when it is given up while it still runs (out of its time, or the job cancelled); null for none.</param>
    /// <returns>What the transfer returned.</returns>
    /// <exception cref="TimeoutException">The attempt did not finish in its time; the gate is free again.</exception>
    /// <exception cref="OperationCanceledException">The job was cancelled; the gate is free again.</exception>
    public async Task<T> RunAsync<T>(Func<Task<T>> transfer, TimeSpan timeout, CancellationToken cancellationToken, Action<Task<T>>? abandoned = null)
    {
        if (m_open)
        {
            try
            {
                return await Bounded(transfer, timeout, cancellationToken, abandoned);
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
            var result = await Bounded(transfer, timeout, cancellationToken, abandoned);
            m_open = true;
            return result;
        }
        finally
        {
            m_single.Release();
        }
    }

    /// <summary>Waits for an attempt within its time and the job's token.</summary>
    private static async Task<T> Bounded<T>(Func<Task<T>> transfer, TimeSpan timeout, CancellationToken cancellationToken, Action<Task<T>>? abandoned)
    {
        var running = transfer();
        try
        {
            return await running.WaitAsync(timeout, cancellationToken);
        }
        catch (Exception exception) when ((exception is TimeoutException or OperationCanceledException) && !running.IsCompleted)
        {
            // The call itself cannot be stopped: whatever it comes to later is looked at, so
            // that it does not end as an exception nobody saw, and the caller decides what to
            // wait for.
            _ = running.ContinueWith(static given => _ = given.Exception, TaskScheduler.Default);
            abandoned?.Invoke(running);
            throw;
        }
    }

    #endregion

    #region Properties

    /// <summary>True once a transfer has succeeded and none has failed since.</summary>
    public bool IsOpen => m_open;

    #endregion
}
