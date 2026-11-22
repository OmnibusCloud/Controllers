using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Tasksets;

/// <summary>One generator task as its run settled it: what a check is made from.</summary>
/// <param name="Index">The task.</param>
/// <param name="Verdict">The settled verdict.</param>
/// <param name="Stdout">The settled stdout: the candidate.</param>
/// <param name="StdoutTruncated">Whether the stdout hit its cap.</param>
public sealed record VerifyCandidate(int Index, VerifyVerdict Verdict, string Stdout, bool StdoutTruncated)
{
    #region Functions

    /// <summary>The candidate of a result that settles its task.</summary>
    /// <param name="result">The result.</param>
    /// <returns>The candidate.</returns>
    public static VerifyCandidate Of(VerifyResultData result)
    {
        return new VerifyCandidate(result.TaskIndex, result.Verdict, result.Stdout, result.StdoutTruncated);
    }

    #endregion
}
