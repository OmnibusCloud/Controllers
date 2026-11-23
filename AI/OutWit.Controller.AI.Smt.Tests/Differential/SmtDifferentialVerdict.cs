namespace OutWit.Controller.AI.Smt.Tests.Differential;

/// <summary>
/// What became of one generated term.
/// </summary>
internal enum SmtDifferentialVerdict
{
    /// <summary>The evaluator gave a value, and the solver says the term equals it.</summary>
    Agreed,

    /// <summary>The evaluator gave no value, for a reason of its own; the solver was not asked.</summary>
    Partial,

    /// <summary>The evaluator gave a value, and the solver says the term does not equal it.</summary>
    Disagreed,

    /// <summary>The solver did not answer the question: it refused the text, or ran out.</summary>
    NotTaken,

    /// <summary>The evaluator failed in a way it must not: an exception, a value of another sort, a value it does not validate itself.</summary>
    EvaluatorFault
}
