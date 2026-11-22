namespace OutWit.Controller.AI.Verify.Adapters;

/// <summary>
/// A scope of the host's logger, ended under the rule of <see cref="VerifyLoggerGuarded"/>: behind
/// the lock, and an exception of the logger's own goes no further.
/// </summary>
internal sealed class VerifyLoggerGuardedScope : IDisposable
{
    #region Fields

    private IDisposable? m_scope;

    #endregion

    #region Constructors

    /// <summary>Wraps a scope the host's logger gave.</summary>
    /// <param name="scope">The scope.</param>
    public VerifyLoggerGuardedScope(IDisposable scope)
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
            VerifyLoggerGuarded.End(scope);
    }

    #endregion
}
