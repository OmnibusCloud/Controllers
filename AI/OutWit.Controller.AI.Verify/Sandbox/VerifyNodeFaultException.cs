using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// A task could not be run to an end of its own for a reason that is the node's, not the
/// program's: the wall-clock backstop, a file the node could not write, a tool that did not
/// start. It carries a reason code and nothing else; whatever the node knows beyond it (a path,
/// an exception's text) goes to the node's log and never into a result.
/// </summary>
internal sealed class VerifyNodeFaultException : Exception
{
    #region Constructors

    /// <summary>
    /// A fault of the node.
    /// </summary>
    /// <param name="code">The reason code (<see cref="VerifyFault"/>).</param>
    /// <param name="inner">What happened on the node, for its log; never sent anywhere.</param>
    public VerifyNodeFaultException(string code, Exception? inner = null)
        : base($"the node could not run the task: {code}", inner)
    {
        Code = code;
    }

    #endregion

    #region Properties

    /// <summary>The reason code.</summary>
    public string Code { get; }

    #endregion
}
