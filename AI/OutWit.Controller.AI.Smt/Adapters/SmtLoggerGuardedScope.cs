namespace OutWit.Controller.AI.Smt.Adapters;

/// <summary>
/// A scope of the host's logger, ended under the rule of <see cref="SmtLoggerGuarded"/>: behind
/// the lock, and an exception of the logger's own goes no further.
/// </summary>
internal sealed class SmtLoggerGuardedScope : IDisposable
{
    #region Fields

    private IDisposable? m_scope;

    #endregion

    #region Constructors

    /// <summary>Wraps a scope the host's logger gave.</summary>
    /// <param name="scope">The scope.</param>
    public SmtLoggerGuardedScope(IDisposable scope)
    {
        m_scope = scope;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        var scope = Interlocked.Exchange(ref m_scope, null);
        if (scope != null)
            SmtLoggerGuarded.End(scope);
    }

    #endregion
}
