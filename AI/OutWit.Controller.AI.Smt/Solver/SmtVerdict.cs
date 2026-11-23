namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>The verdict of a session whose status is <c>ok</c>: <c>sat</c>, <c>unsat</c>, <c>unknown</c>.</summary>
internal enum SmtVerdict
{
    /// <summary>No verdict: the session's status is not <c>ok</c>.</summary>
    None = 0,

    /// <summary><c>sat</c>: a model was found (a counterexample, for a proof; one model or more, for an enumeration).</summary>
    Sat = 1,

    /// <summary><c>unsat</c>: there is no model (the goal is proved; the enumeration found none).</summary>
    Unsat = 2,

    /// <summary><c>unknown</c>: the solver gave up or a budget ended it; <see cref="SmtUnknownReason"/> says which.</summary>
    Unknown = 3
}
