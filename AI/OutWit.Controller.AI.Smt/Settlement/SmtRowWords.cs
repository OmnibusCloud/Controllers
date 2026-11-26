using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Lang;
using OutWit.Controller.AI.Smt.Solver;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The words of a node's row held to each other: a row is taken only when a session of this
/// controller could have ended in it. What a session's outcome can be is the node's code
/// (<see cref="SmtSessionOutcomes"/>, the solver's entry before it); this is the same relation as
/// the host holds a row to it, and the tests walk every way a session ends through both.
/// <list type="bullet">
/// <item><b>The status and its code.</b> An answered query, an error of the solver's and an output
/// past its cap carry no code; a refusal one of the refusal codes; a crash and a fault of the node
/// one of the controller's own words for them. No other word is a code, so none reaches a
/// document as one.</item>
/// <item><b>The verdict.</b> Only an answered query (<c>ok</c>) claims one, and a session whose
/// output passed its cap after the solver had answered a check. Every other status has none.</item>
/// <item><b>A check and a proof, answered.</b> <c>sat</c> with exactly the one model, and with one
/// answer of values exactly when the check asked for values; <c>unsat</c> and <c>unknown</c> with
/// neither; <c>unknown</c> with its reason, the others without one.</item>
/// <item><b>An enumeration, answered.</b> Complete (its last check said <c>unsat</c>) below its
/// number of models, <c>unsat</c> without a model and <c>sat</c> with one, without a reason; at
/// its limit with exactly its number of models, <c>sat</c>, without a reason; neither, with the
/// reason it stopped for, <c>sat</c> with a model and <c>unknown</c> without.</item>
/// <item><b>A session that did not answer</b> keeps what it had gathered and claims nothing: the
/// models that were whole (a check's values only after its model, an enumeration's models each
/// with its values), no reason, neither complete nor at a limit. A refusal ran nothing at all.</item>
/// <item><b>The solver's own word</b> stands only beside the reason it was read for.</item>
/// </list>
/// </summary>
internal static class SmtRowWords
{
    #region Constants

    private static readonly HashSet<string> REFUSALS = new(SmtRefusalCodes.All.Concat(SmtSolveCodes.REFUSALS), StringComparer.Ordinal);

    private static readonly HashSet<string> CRASHES = new(SmtSolveCodes.CRASHES, StringComparer.Ordinal);

    private static readonly HashSet<string> FAULTS = new(SmtSolveCodes.FAULTS, StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>
    /// Whether the words and the counts of a row are the ones of a session's outcome. The row's
    /// words are words of a result and its texts are there (<see cref="SmtRowTexts"/>) already.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="bound">What the row's problem allows.</param>
    /// <returns>True when a session of the problem could have ended in the row.</returns>
    public static bool Hold(SmtResultData row, SmtTaskBound bound)
    {
        if (!HasItsCounts(row, bound) || !IsCodeOf(row.Status, row.Code) || row.Proof != ProofOf(row.Query, row.Verdict))
            return false;

        // The solver is asked for its word after an unknown, and for nothing else.
        if (row.ReasonText.Length > 0 && (row.Status != SmtResultWords.OK || row.Reason is SmtResultWords.NONE or SmtResultWords.FUEL))
            return false;

        return row.Status switch
        {
            SmtResultWords.OK => row.Message.Length == 0 && (row.Query == SmtQuery.Enumerate ? IsEnumerated(row, bound) : IsAnswered(row, bound)),
            SmtResultWords.OUTPUT_EXCEEDED => IsUnfinished(row) && IsExceeded(row),
            SmtResultWords.REFUSED => IsUnfinished(row) && row.Verdict == SmtResultWords.NONE && RanNothing(row),
            _ => IsUnfinished(row) && row.Verdict == SmtResultWords.NONE
        };
    }

    /// <summary>The word of a proof a session of a query gives with a verdict.</summary>
    /// <param name="query">The query.</param>
    /// <param name="verdict">The verdict.</param>
    /// <returns><c>proved</c>, <c>counterexample</c> or <c>none</c>.</returns>
    public static string ProofOf(SmtQuery query, string verdict)
    {
        if (query != SmtQuery.Prove)
            return SmtResultWords.NONE;

        return verdict switch
        {
            SmtResultWords.UNSAT => SmtResultWords.PROVED,
            SmtResultWords.SAT => SmtResultWords.COUNTEREXAMPLE,
            _ => SmtResultWords.NONE
        };
    }

    #endregion

    #region Tools

    /// <summary>
    /// What a session of a query can have gathered, however it ended: a check at most one model,
    /// and one answer of values only after it and only when it asked for them; a proof at most a
    /// model; an enumeration a model and its projected values together, as many as it counted
    /// and no more than it looks for. Only an enumeration counts.
    /// </summary>
    private static bool HasItsCounts(SmtResultData row, SmtTaskBound bound)
    {
        return row.Query switch
        {
            SmtQuery.Check => row.Count == 0 && row.Models.Count <= 1 && row.Values.Count <= row.Models.Count && (bound.HasValues || row.Values.Count == 0),
            SmtQuery.Prove => row.Count == 0 && row.Models.Count <= 1 && row.Values.Count == 0,
            SmtQuery.Enumerate => row.Count == row.Models.Count && row.Values.Count == row.Models.Count && row.Count <= bound.MaxModels,
            _ => false
        };
    }

    private static bool IsCodeOf(string status, string? code)
    {
        if (code == null)
            return false;

        return status switch
        {
            SmtResultWords.REFUSED => REFUSALS.Contains(code),
            SmtResultWords.ENGINE_CRASH => CRASHES.Contains(code),
            SmtResultWords.NODE_FAULT => FAULTS.Contains(code),
            _ => code.Length == 0
        };
    }

    /// <summary>A check or a proof whose session was answered, or ended by a budget: its verdict with what the session carries beside it.</summary>
    private static bool IsAnswered(SmtResultData row, SmtTaskBound bound)
    {
        if (row.Complete || row.LimitReached)
            return false;

        return row.Verdict switch
        {
            SmtResultWords.SAT => row.Models.Count == 1 && row.Values.Count == (bound.HasValues ? 1 : 0) && row.Reason == SmtResultWords.NONE,
            SmtResultWords.UNSAT => row.Models.Count == 0 && row.Reason == SmtResultWords.NONE,
            SmtResultWords.UNKNOWN => row.Models.Count == 0 && row.Reason != SmtResultWords.NONE,
            _ => false
        };
    }

    /// <summary>
    /// An enumeration whose session was answered, or ended by a budget: its verdict follows from
    /// its count and from how it stopped. It stops at its number of models without another check,
    /// so it is complete only below that number.
    /// </summary>
    private static bool IsEnumerated(SmtResultData row, SmtTaskBound bound)
    {
        if (row.Complete && row.LimitReached)
            return false;

        if (row.Complete)
            return row.Count < bound.MaxModels && row.Verdict == (row.Count > 0 ? SmtResultWords.SAT : SmtResultWords.UNSAT) && row.Reason == SmtResultWords.NONE;

        if (row.LimitReached)
            return row.Count == bound.MaxModels && row.Verdict == SmtResultWords.SAT && row.Reason == SmtResultWords.NONE;

        return row.Verdict == (row.Count > 0 ? SmtResultWords.SAT : SmtResultWords.UNKNOWN) && row.Reason != SmtResultWords.NONE;
    }

    /// <summary>
    /// A session whose output passed its cap: what the solver had answered by then stands, so a
    /// check or a proof has the verdict of its check, or none; an enumeration says <c>sat</c>
    /// when it has a model or its check had just said so, and nothing else.
    /// </summary>
    private static bool IsExceeded(SmtResultData row)
    {
        return row.Query != SmtQuery.Enumerate || row.Verdict == SmtResultWords.SAT || (row.Verdict == SmtResultWords.NONE && row.Count == 0);
    }

    /// <summary>What every session that was not answered has in common: no reason, and nothing an answered enumeration says of its end.</summary>
    private static bool IsUnfinished(SmtResultData row)
    {
        return row.Reason == SmtResultWords.NONE && !row.Complete && !row.LimitReached;
    }

    /// <summary>A refusal is made before a solver starts: there is no model, no check, no cost and no transcript.</summary>
    private static bool RanNothing(SmtResultData row)
    {
        return row.Models.Count == 0 && row.Checks == 0 && row.ResourceCount == 0 && row.Fuel == 0 && row.PeakMemoryBytes == 0 && row.Transcript.Length == 0;
    }

    #endregion
}
