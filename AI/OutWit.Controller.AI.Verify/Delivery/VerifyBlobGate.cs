namespace OutWit.Controller.AI.Verify.Delivery;

/// <summary>
/// Lets the first transfer over the node's blob channel go alone. The channel is connected
/// by its first user, and several first users at once each start a connection of their own;
/// on the test fleet a node whose first two requests came together (and were retried in step)
/// failed every attempt for minutes. While the gate is closed transfers pass one at a time;
/// the first that succeeds opens it and the rest run side by side. A failure closes it again,
/// since a failure is what a lost channel looks like.
/// </summary>
public sealed class VerifyBlobGate
{
    #region Fields

    private readonly SemaphoreSlim m_single = new(1, 1);
    private volatile bool m_open;

    #endregion

    #region Functions

    /// <summary>Runs one attempt of a transfer through the gate.</summary>
    /// <typeparam name="T">What the transfer returns.</typeparam>
    /// <param name="transfer">One attempt of the transfer.</param>
    /// <param name="cancellationToken">Stops waiting for the gate.</param>
    /// <returns>What the transfer returned.</returns>
    public async Task<T> RunAsync<T>(Func<Task<T>> transfer, CancellationToken cancellationToken)
    {
        if (m_open)
        {
            try
            {
                return await transfer();
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
            var result = await transfer();
            m_open = true;
            return result;
        }
        finally
        {
            m_single.Release();
        }
    }

    #endregion

    #region Properties

    /// <summary>True once a transfer has succeeded and none has failed since.</summary>
    public bool IsOpen => m_open;

    #endregion
}
