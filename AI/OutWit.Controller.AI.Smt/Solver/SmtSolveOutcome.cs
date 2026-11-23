using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// What one session came to: one problem, one query, one outcome. A working type of the
/// controller; what travels to a host is shaped after it.
/// <para>
/// Everything here but <see cref="WallMs"/> is a function of the problem, the options and the
/// solver module, and of nothing else: the same on every machine, in both trap modes, to the
/// last byte. <see cref="WallMs"/> is the one measurement of the machine, kept apart and never
/// compared. A <see cref="SmtSolveStatus.NodeFault"/> is the exception by its nature: it says
/// something about the machine and nothing about the problem.
/// </para>
/// <para>
/// A verdict is claimed by an outcome whose status is <see cref="SmtSolveStatus.Ok"/>, and by
/// one whose output passed its cap after the solver had answered the check: the solver said
/// <c>sat</c>, and it is the model that did not fit
/// (<see cref="SmtSolveStatus.OutputExceeded"/>). Any other outcome keeps what the session had
/// gathered when it ended (the checks sent, the resource count, the models that were whole, the
/// end of the transcript) for whoever looks into it, and claims nothing.
/// </para>
/// </summary>
internal sealed class SmtSolveOutcome
{
    #region Properties

    /// <summary>How the session ended.</summary>
    public SmtSolveStatus Status { get; init; }

    /// <summary>
    /// The fixed word of the outcome's kind: the code of a refusal, the kind of a crash or of a
    /// fault (<see cref="SmtSolveCodes"/>); empty where the status says it all.
    /// </summary>
    public string Code { get; init; } = "";

    /// <summary>
    /// The sentence that goes with <see cref="Code"/>: a refusal's reason, the solver's own
    /// message of an input error (cut), the fixed words of a crash, why the node could not run
    /// the session; empty for an answered query.
    /// </summary>
    public string Message { get; init; } = "";

    /// <summary>The refusal in full when the status is <see cref="SmtSolveStatus.Refused"/>, else null.</summary>
    public SmtRefusalData? Refusal { get; init; }

    /// <summary>What was asked of the problem.</summary>
    public SmtQuery Query { get; init; }

    /// <summary>
    /// The verdict. For an enumeration: <c>sat</c> when it found a model, <c>unsat</c> when there
    /// is none, <c>unknown</c> when it found none and could not say that there is none.
    /// </summary>
    public SmtVerdict Verdict { get; init; }

    /// <summary>Why a check has no verdict, or why an enumeration stopped before it was complete (none when it stopped at its number of models).</summary>
    public SmtUnknownReason Reason { get; init; }

    /// <summary>The solver's own word for an <c>unknown</c>, as it answered <c>(get-info :reason-unknown)</c>; empty when it was not asked.</summary>
    public string ReasonText { get; init; } = "";

    /// <summary>The models an enumeration found; 0 for the other queries.</summary>
    public int Count { get; init; }

    /// <summary>Whether an enumeration is complete: its last check answered <c>unsat</c>, so <see cref="Count"/> is exact.</summary>
    public bool Complete { get; init; }

    /// <summary>Whether an enumeration stopped because it had found as many models as it was asked for: there may be more.</summary>
    public bool LimitReached { get; init; }

    /// <summary>What a proof came to.</summary>
    public SmtProofResult Proof { get; init; }

    /// <summary>
    /// The models, each as the solver printed it in answer to <c>(get-model)</c>, byte for byte:
    /// one for a satisfiable check and for a counterexample, one for each model of an enumeration.
    /// </summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    /// <summary>
    /// The values, each as the solver printed them in answer to <c>(get-value (...))</c>: of the
    /// terms a check asked for (one entry), or of the projected constants of each model of an
    /// enumeration (one entry a model, in the order of <see cref="Models"/>).
    /// </summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>The checks the session sent.</summary>
    public int Checks { get; init; }

    /// <summary>The solver's resource counter after the last check it answered, over the whole session.</summary>
    public long ResourceCount { get; init; }

    /// <summary>The fuel the session used, counted from the solver's first instruction; the whole budget when it ran out.</summary>
    public long Fuel { get; init; }

    /// <summary>The solver's linear memory when the session ended (it only grows, so this is its peak), bytes.</summary>
    public long PeakMemoryBytes { get; init; }

    /// <summary>The end of the session's transcript: the commands sent, without the problem's text, and the solver's answers.</summary>
    public string Transcript { get; init; } = "";

    /// <summary>The wall time of the session on this machine, milliseconds: a measurement, never a part of what is compared.</summary>
    public int WallMs { get; init; }

    #endregion
}
