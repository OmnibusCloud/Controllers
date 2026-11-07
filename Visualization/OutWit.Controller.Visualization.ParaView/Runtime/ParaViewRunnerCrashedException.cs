namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The runner process died without writing its status document — a segfault, an abort, an OOM
/// kill: the interpreter never reached the <c>finally</c> that writes it. This is the only failure
/// shape the EGL demote-and-retry acts on (audit C-M1): a policy refusal, a usage error or the
/// wall-clock limit is the task's own verdict and comes back as a plain
/// <see cref="InvalidOperationException"/>.
/// </summary>
public sealed class ParaViewRunnerCrashedException : InvalidOperationException
{
    #region Constructors

    public ParaViewRunnerCrashedException(string message)
        : base(message)
    {
    }

    #endregion
}
