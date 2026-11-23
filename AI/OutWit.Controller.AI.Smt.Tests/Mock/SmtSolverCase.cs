using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// One problem of the solver's tests with the options it is solved under, and what its outcome
/// must be in a few words.
/// </summary>
/// <param name="Id">The case's name, once in <see cref="SmtSolverCases"/>.</param>
/// <param name="Text">The problem's text as it was written for the tests.</param>
/// <param name="Problem">The problem as it travels.</param>
/// <param name="Options">The run's options.</param>
/// <param name="Expected">The outcome in words: the status, then what the status has (<c>ok sat</c>, <c>ok unknown rlimit</c>, <c>ok sat 4 complete</c>, <c>ok unsat proved</c>, <c>input_error</c>, <c>refused hash_mismatch</c>).</param>
internal sealed record SmtSolverCase(string Id, string Text, SmtProblemData Problem, SmtOptionsData Options, string Expected)
{
    #region Functions

    /// <summary>The case's name, as a test's log shows it.</summary>
    /// <returns>The id.</returns>
    public override string ToString()
    {
        return Id;
    }

    #endregion
}
