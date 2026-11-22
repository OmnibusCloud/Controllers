namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// A term the evaluator did not evaluate to a value: the assertion around it is neither true
/// nor false by this term. It carries a fixed reason code and nothing of the input.
/// </summary>
internal sealed class SmtPartialException : Exception
{
    #region Constructors

    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="kind">How far the failure reaches.</param>
    /// <param name="reason">A code of <see cref="SmtValidationReasons"/>.</param>
    /// <param name="detail">The name of an operator from the evaluator's vocabulary, or null.</param>
    /// <param name="depth">How deep the evaluation was when the term was met.</param>
    public SmtPartialException(SmtPartialKind kind, string reason, string? detail, int depth)
        : base(reason)
    {
        Kind = kind;
        Reason = reason;
        Detail = detail;
        Depth = depth;
    }

    #endregion

    #region Properties

    /// <summary>How far the failure reaches.</summary>
    public SmtPartialKind Kind { get; }

    /// <summary>A code of <see cref="SmtValidationReasons"/>.</summary>
    public string Reason { get; }

    /// <summary>The name of an operator from the evaluator's vocabulary, or null.</summary>
    public string? Detail { get; }

    /// <summary>How deep the evaluation was when the term was met: what unwinding to a handler costs.</summary>
    public int Depth { get; }

    #endregion
}
