using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Smt.Delivery;

/// <summary>
/// Makes the row a node returns for one problem (<see cref="SmtResultData"/>) of the session's
/// outcome: the problem it answers, the ticket of its package, every value of the outcome in the
/// words of a result, the controller's version and the platform, and the digest of the row
/// (<see cref="SmtResultDigest"/>). Nothing else of the machine goes in.
/// </summary>
internal static class SmtResultRows
{
    #region Functions

    /// <summary>
    /// Makes a problem's row.
    /// </summary>
    /// <param name="problem">The problem as the node was given it.</param>
    /// <param name="outcome">Its session's outcome.</param>
    /// <param name="ticket">The ticket of the package.</param>
    /// <returns>The row, its digest computed.</returns>
    public static SmtResultData Of(SmtProblemData problem, SmtSolveOutcome outcome, string ticket)
    {
        var row = new SmtResultData
        {
            Index = problem.Index,
            Id = problem.Id ?? "",
            Hash = SmtPackageCodec.HashText(problem.Text ?? ""),
            Ticket = ticket,
            Query = problem.Query,
            Status = SmtSolveWords.Of(outcome.Status),
            Code = outcome.Code,
            Message = Cut(outcome.Message, SmtResultWords.MAX_MESSAGE_CHARACTERS),
            Verdict = SmtSolveWords.Of(outcome.Verdict),
            Reason = SmtSolveWords.Of(outcome.Reason),
            ReasonText = outcome.ReasonText,
            Proof = SmtSolveWords.Of(outcome.Proof),
            Count = outcome.Count,
            Complete = outcome.Complete,
            LimitReached = outcome.LimitReached,
            Models = [.. outcome.Models],
            Values = [.. outcome.Values],
            Checks = outcome.Checks,
            ResourceCount = outcome.ResourceCount,
            Fuel = outcome.Fuel,
            PeakMemoryBytes = outcome.PeakMemoryBytes,
            Transcript = outcome.Transcript,
            ControllerVersion = ControllerBuildInfo.VERSION,
            Platform = Platform,
            WallMs = outcome.WallMs
        };

        row.Digest = SmtResultDigest.Of(row);
        return row;
    }

    /// <summary>A text cut to a number of characters, never between the two halves of a surrogate pair.</summary>
    /// <param name="text">The text.</param>
    /// <param name="characters">The most characters.</param>
    /// <returns>The text, or its start.</returns>
    public static string Cut(string text, int characters)
    {
        if (text.Length <= characters)
            return text;

        var length = char.IsHighSurrogate(text[characters - 1]) ? characters - 1 : characters;
        return text[..length];
    }

    #endregion

    #region Properties

    /// <summary>The platform a row names: the portable runtime identifier of the process (<c>win-x64</c>, <c>linux-x64</c>, <c>osx-arm64</c>).</summary>
    public static string Platform => WasmHostNativeLibrary.PortableRid;

    #endregion
}
