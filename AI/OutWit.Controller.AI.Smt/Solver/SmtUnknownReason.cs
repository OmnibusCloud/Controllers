namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// Why a check has no verdict, or an enumeration is not complete: <c>rlimit</c>, <c>fuel</c>,
/// <c>memout</c>, <c>incomplete</c>, <c>other</c>. Each is a count or the solver's own word, so
/// it is the same on every machine, with one reservation that <see cref="Memory"/> states.
/// </summary>
internal enum SmtUnknownReason
{
    /// <summary>No reason: the check has a verdict.</summary>
    None = 0,

    /// <summary><c>rlimit</c>: the solver's resource limit for one check ended the check.</summary>
    ResourceLimit = 1,

    /// <summary><c>fuel</c>: the session's fuel, the hard cap, ended the session.</summary>
    Fuel = 2,

    /// <summary>
    /// <c>memout</c>: the solver was refused memory, by its own ceiling or by the host's caps,
    /// which are the same numbers on every machine. A refusal reaches the solver in one way
    /// whoever refused, so a machine that could not give the memory the caps allow would end a
    /// session with this reason too: the outcome does not tell which it was.
    /// </summary>
    Memory = 3,

    /// <summary><c>incomplete</c>: the solver's procedure is not complete for the problem, by its own word.</summary>
    Incomplete = 4,

    /// <summary>
    /// <c>other</c>: any other word of the solver's; the outcome keeps it. Also the reason of an
    /// enumeration that ended because the solver did not take a found model's values back: the
    /// word kept is what the solver said to that.
    /// </summary>
    Other = 5
}
