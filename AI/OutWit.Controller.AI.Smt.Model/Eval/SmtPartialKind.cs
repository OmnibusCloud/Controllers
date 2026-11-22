namespace OutWit.Controller.AI.Smt.Model.Eval;

/// <summary>
/// How far a term that was not evaluated reaches: whether the terms around it can still decide
/// the formula it stands in.
/// </summary>
internal enum SmtPartialKind
{
    /// <summary>
    /// The term has a value in every interpretation, and the evaluator does not compute it (a
    /// quantified formula, a division by zero, an operator it does not evaluate). A sibling
    /// that decides a connective still decides it.
    /// </summary>
    Undecided = 0,

    /// <summary>
    /// The evaluator cannot make sense of the term (a wrong number of arguments, a value of
    /// the wrong sort, an unknown name). The assertion it stands in is neither true nor false,
    /// whatever its other terms say: the problem may not mean what was read.
    /// </summary>
    Doubt = 1,

    /// <summary>A budget ended the evaluation: nothing more is evaluated.</summary>
    Terminal = 2
}
