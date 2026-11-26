namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// How one session ended, as a row of a result names it: <c>ok</c>, <c>refused</c>,
/// <c>input_error</c>, <c>output_exceeded</c>, <c>engine_crash</c>, <c>node_fault</c>. Every
/// status but the last is a statement about the problem under the profile, the same on every
/// machine; <see cref="NodeFault"/> is a statement about the machine and says nothing about the
/// problem. What of the machine still stands behind "the same on every machine" (the native stack
/// a guest is given, and memory the machine itself could not give) is said where an outcome is
/// (<see cref="SmtSolveOutcome"/>).
/// </summary>
internal enum SmtSolveStatus
{
    /// <summary><c>ok</c>: the solver answered the query, or one of its budgets ended it (the verdict is then <c>unknown</c>, with the reason).</summary>
    Ok = 0,

    /// <summary><c>refused</c>: the problem, a term beside it or the options were not taken, and no solver ran.</summary>
    Refused = 1,

    /// <summary><c>input_error</c>: the solver reported an error in what it was given; its message is kept, cut.</summary>
    InputError = 2,

    /// <summary><c>output_exceeded</c>: the session wrote more than its output cap and ended at that write.</summary>
    OutputExceeded = 3,

    /// <summary><c>engine_crash</c>: the solver trapped, ended with a status that is no answer, or answered something that cannot be read.</summary>
    EngineCrash = 4,

    /// <summary><c>node_fault</c>: the machine, not the problem: no sandbox, the wall-clock backstop, a cancelled run, a file the host could not use.</summary>
    NodeFault = 5
}
