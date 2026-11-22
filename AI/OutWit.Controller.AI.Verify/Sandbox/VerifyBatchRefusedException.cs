namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// This node cannot run a package, and knew it before it ran any of it: another controller
/// version, a runtime it does not have or has other bytes of, a sandbox that does not start, a
/// package outside what a node takes. The activity fails with this, the platform gives the
/// package to another node, and nothing is lost. The message is fixed words: it may reach a
/// submitter, so it carries no path, no account name and no text of an exception. Where on the
/// node the trouble is goes into <see cref="NodeDetail"/>, for the node's own log.
/// </summary>
public sealed class VerifyBatchRefusedException : InvalidOperationException
{
    #region Constructors

    /// <summary>
    /// A refusal.
    /// </summary>
    /// <param name="reason">Why, in fixed words without anything of the node's own.</param>
    /// <param name="nodeDetail">The file or folder the reason is about, or null; never sent anywhere.</param>
    public VerifyBatchRefusedException(string reason, string? nodeDetail)
        : base(reason)
    {
        NodeDetail = nodeDetail;
    }

    #endregion

    #region Properties

    /// <summary>The file or folder on the node the reason is about, for the node's log; null when there is none.</summary>
    public string? NodeDetail { get; }

    #endregion
}
