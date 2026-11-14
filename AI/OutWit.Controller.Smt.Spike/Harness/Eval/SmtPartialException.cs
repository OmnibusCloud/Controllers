namespace OutWit.Controller.Smt.Spike.Eval;

/// <summary>
/// The evaluator met something it cannot close (a quantifier, an algebraic number, an
/// uninterpreted division by zero, an array known only as a function, an unsupported operator):
/// the assertion is <c>partial</c>, neither true nor false.
/// </summary>
internal sealed class SmtPartialException : Exception
{
    #region Constructors

    /// <summary>Makes the exception.</summary>
    /// <param name="reason">What could not be closed, in a few fixed words (counted in the report).</param>
    public SmtPartialException(string reason) : base(reason)
    {
        Reason = reason;
    }

    #endregion

    #region Properties

    /// <summary>What could not be closed.</summary>
    public string Reason { get; }

    #endregion
}
