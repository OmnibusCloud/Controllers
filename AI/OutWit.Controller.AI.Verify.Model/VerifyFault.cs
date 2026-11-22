namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// The reason codes of a task without a verdict (<see cref="VerifyVerdict.NoResult"/>): a short,
/// fixed list. A code names what kept the nodes from an answer; it never carries a node's own
/// words, a path or an exception text.
/// </summary>
public static class VerifyFault
{
    #region Constants

    /// <summary>The node's wall-clock backstop ended the run while it still had fuel.</summary>
    public const string WALL_CLOCK = "wall_clock";

    /// <summary>The node could not run the task: a failure of its own (its disk, an internal error).</summary>
    public const string NODE_FAULT = "node_fault";

    /// <summary>No accepted result came back for the task: its package was not answered, or the answer was refused.</summary>
    public const string NO_ANSWER = "no_answer";

    /// <summary>Every code, in the order the documents list them.</summary>
    public static readonly IReadOnlyList<string> ALL = [WALL_CLOCK, NODE_FAULT, NO_ANSWER];

    #endregion
}
