namespace OutWit.Controller.AI.Smt.Live;

/// <summary>
/// What Ctrl+C does. The first press stops the tool's work: a local run hands out no further
/// problem (the sessions that run end at their check's end, and the settlement is not
/// interrupted), and the waiting for a job, a transfer or a download stops, the job going on on
/// the server; the command then says what it stopped and what is left. The second press ends the
/// tool at once, whatever it is doing.
/// </summary>
internal sealed class SmtLiveInterrupt : IDisposable
{
    #region Fields

    private readonly CancellationTokenSource m_cancel = new();
    private int m_presses;

    #endregion

    #region Functions

    /// <summary>
    /// Takes one press of Ctrl+C.
    /// </summary>
    /// <returns>True when the tool goes on (the first press: its work is stopped); false when it is to end now.</returns>
    public bool Press()
    {
        if (Interlocked.Increment(ref m_presses) > 1)
            return false;

        m_cancel.Cancel();
        return true;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        m_cancel.Dispose();
    }

    #endregion

    #region Properties

    /// <summary>Stops the tool's work at the first press.</summary>
    public CancellationToken Token => m_cancel.Token;

    #endregion
}
