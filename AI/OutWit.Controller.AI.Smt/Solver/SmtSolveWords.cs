namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// The words of an outcome as a result names them, in lower case: <c>ok</c>, <c>input_error</c>,
/// <c>sat</c>, <c>rlimit</c>, <c>proved</c> and the rest.
/// </summary>
internal static class SmtSolveWords
{
    #region Constants

    /// <summary>The word of a verdict, a reason or a proof that is not there.</summary>
    public const string NONE = "none";

    #endregion

    #region Functions

    /// <summary>The word of a status.</summary>
    /// <param name="status">The status.</param>
    /// <returns><c>ok</c>, <c>refused</c>, <c>input_error</c>, <c>output_exceeded</c>, <c>engine_crash</c> or <c>node_fault</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is no status.</exception>
    public static string Of(SmtSolveStatus status)
    {
        return status switch
        {
            SmtSolveStatus.Ok => "ok",
            SmtSolveStatus.Refused => "refused",
            SmtSolveStatus.InputError => "input_error",
            SmtSolveStatus.OutputExceeded => "output_exceeded",
            SmtSolveStatus.EngineCrash => "engine_crash",
            SmtSolveStatus.NodeFault => "node_fault",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "no status")
        };
    }

    /// <summary>The word of a verdict.</summary>
    /// <param name="verdict">The verdict.</param>
    /// <returns><c>sat</c>, <c>unsat</c>, <c>unknown</c>, or <c>none</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is no verdict.</exception>
    public static string Of(SmtVerdict verdict)
    {
        return verdict switch
        {
            SmtVerdict.None => NONE,
            SmtVerdict.Sat => "sat",
            SmtVerdict.Unsat => "unsat",
            SmtVerdict.Unknown => "unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "no verdict")
        };
    }

    /// <summary>The word of the reason of an <c>unknown</c>.</summary>
    /// <param name="reason">The reason.</param>
    /// <returns><c>rlimit</c>, <c>fuel</c>, <c>memout</c>, <c>incomplete</c>, <c>other</c>, or <c>none</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is no reason.</exception>
    public static string Of(SmtUnknownReason reason)
    {
        return reason switch
        {
            SmtUnknownReason.None => NONE,
            SmtUnknownReason.ResourceLimit => "rlimit",
            SmtUnknownReason.Fuel => "fuel",
            SmtUnknownReason.Memory => "memout",
            SmtUnknownReason.Incomplete => "incomplete",
            SmtUnknownReason.Other => "other",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "no reason")
        };
    }

    /// <summary>The word of what a proof came to.</summary>
    /// <param name="proof">The result.</param>
    /// <returns><c>proved</c>, <c>counterexample</c>, or <c>none</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is no result of a proof.</exception>
    public static string Of(SmtProofResult proof)
    {
        return proof switch
        {
            SmtProofResult.None => NONE,
            SmtProofResult.Proved => "proved",
            SmtProofResult.Counterexample => "counterexample",
            _ => throw new ArgumentOutOfRangeException(nameof(proof), proof, "no result of a proof")
        };
    }

    #endregion
}
