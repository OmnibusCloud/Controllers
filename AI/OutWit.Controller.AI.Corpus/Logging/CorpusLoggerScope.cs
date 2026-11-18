namespace OutWit.Controller.AI.Corpus.Logging;

/// <summary>
/// A scope of a host's logger as <see cref="CorpusLogger"/> hands it out: ending it is a call
/// into the host's logger like any other, so it waits its turn and its failure ends here.
/// </summary>
internal sealed class CorpusLoggerScope : IDisposable
{
    #region Fields

    private readonly IDisposable m_inner;

    private readonly Lock m_gate;

    private int m_disposed;

    #endregion

    #region Constructors

    /// <summary>Wraps a scope of the host's logger.</summary>
    /// <param name="inner">The host's scope.</param>
    /// <param name="gate">The gate every call into the host's logger takes.</param>
    public CorpusLoggerScope(IDisposable inner, Lock gate)
    {
        m_inner = inner;
        m_gate = gate;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            return;

        try
        {
            lock (m_gate)
            {
                m_inner.Dispose();
            }
        }
        catch (Exception)
        {
            CorpusLogger.Lose();
        }
    }

    #endregion
}
