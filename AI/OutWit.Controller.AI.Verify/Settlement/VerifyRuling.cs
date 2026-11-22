using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>How one task was settled.</summary>
/// <param name="Index">The task.</param>
/// <param name="Verdict">The accepted execution's verdict, or <see cref="VerifyVerdict.NoResult"/> or <see cref="VerifyVerdict.Disputed"/>.</param>
/// <param name="ExitCode">The accepted execution's exit code; -1 for a task without an accepted execution.</param>
/// <param name="Accepted">The accepted execution, or null.</param>
/// <param name="Reason">The reason code of a task without a result; empty otherwise.</param>
/// <param name="Copies">The executions that returned the accepted result; 0 without one.</param>
/// <param name="Repaired">Whether the accepted execution is of the repair round.</param>
/// <param name="Disputed">The record of the task's dispute, or null when its executions never disagreed.</param>
internal sealed record VerifyRuling(int Index, VerifyVerdict Verdict, int ExitCode, VerifyExecution? Accepted, string Reason, int Copies, bool Repaired, VerifyDisputeRecord? Disputed)
{
    #region Functions

    /// <summary>A task with an accepted execution.</summary>
    /// <param name="index">The task.</param>
    /// <param name="execution">The execution.</param>
    /// <param name="copies">The executions that returned this result.</param>
    /// <param name="repaired">Whether it is of the repair round.</param>
    /// <param name="dispute">The dispute it settled, or null.</param>
    /// <returns>The ruling.</returns>
    public static VerifyRuling Accept(int index, VerifyExecution execution, int copies, bool repaired, VerifyDisputeRecord? dispute = null)
    {
        return new VerifyRuling(index, execution.Verdict, execution.ExitCode, execution, "", copies, repaired, dispute);
    }

    /// <summary>A task no node gave a verdict for.</summary>
    /// <param name="index">The task.</param>
    /// <param name="reason">The reason code.</param>
    /// <returns>The ruling.</returns>
    public static VerifyRuling Without(int index, string reason)
    {
        return new VerifyRuling(index, VerifyVerdict.NoResult, -1, null, reason, 0, true, null);
    }

    /// <summary>A task whose dispute was not settled.</summary>
    /// <param name="index">The task.</param>
    /// <param name="firstCopies">The executions of the first fan-out that disagreed.</param>
    /// <param name="copies">The executions of the dispute round that came back with a verdict.</param>
    /// <param name="agreeing">The most of them that agreed.</param>
    /// <param name="cause">Why it was not settled.</param>
    /// <returns>The ruling.</returns>
    public static VerifyRuling Dispute(int index, int firstCopies, int copies, int agreeing, string cause)
    {
        return new VerifyRuling(index, VerifyVerdict.Disputed, -1, null, "", 0, false, new VerifyDisputeRecord(index, firstCopies, copies, agreeing, cause));
    }

    #endregion
}
