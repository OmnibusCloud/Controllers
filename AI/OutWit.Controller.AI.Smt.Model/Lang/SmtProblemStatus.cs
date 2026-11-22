namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// The verdict a problem says it expects, in its <c>:status</c>.
/// </summary>
public enum SmtProblemStatus
{
    /// <summary>The problem states no verdict: no <c>:status</c>, <c>unknown</c>, or anything that is neither <c>sat</c> nor <c>unsat</c>.</summary>
    None = 0,

    /// <summary>The problem says it is satisfiable.</summary>
    Sat = 1,

    /// <summary>The problem says it is unsatisfiable.</summary>
    Unsat = 2
}
