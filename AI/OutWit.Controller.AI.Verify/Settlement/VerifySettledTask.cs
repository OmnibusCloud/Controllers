using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>One task as the documents show it: how it was settled, and the accepted result when it has one.</summary>
/// <param name="Index">The task.</param>
/// <param name="Verdict">The accepted verdict, or <see cref="VerifyVerdict.NoResult"/> or <see cref="VerifyVerdict.Disputed"/>.</param>
/// <param name="ExitCode">The accepted exit code; -1 without an accepted result.</param>
/// <param name="Reason">The reason code of a task without a result (<see cref="VerifyFault"/>); empty otherwise.</param>
/// <param name="Result">The accepted result with its outputs, or null for a task without one.</param>
public sealed record VerifySettledTask(int Index, VerifyVerdict Verdict, int ExitCode, string Reason, VerifyResultData? Result)
{
    #region Functions

    /// <summary>A task settled by one result: what a run without lying or failing nodes gives every task.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The task.</returns>
    public static VerifySettledTask Of(VerifyResultData result)
    {
        return new VerifySettledTask(result.TaskIndex, result.Verdict, result.ExitCode, "", result);
    }

    #endregion
}
