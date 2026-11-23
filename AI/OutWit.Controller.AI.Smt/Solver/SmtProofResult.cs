namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>What a <c>prove</c> came to: <c>proved</c> or <c>counterexample</c>.</summary>
internal enum SmtProofResult
{
    /// <summary>Neither: another query, or a proof without a verdict.</summary>
    None = 0,

    /// <summary><c>proved</c>: the problem with the negated goal is unsatisfiable.</summary>
    Proved = 1,

    /// <summary><c>counterexample</c>: a model of the problem in which the goal is false.</summary>
    Counterexample = 2
}
