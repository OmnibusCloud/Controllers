namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// The words of a row (<see cref="SmtResultData"/>), in lower case, as a result names them: the
/// status of a session, its verdict, the reason of an <c>unknown</c>, what a proof came to. A
/// host holds every row a node returns to these lists; a word outside them is no answer.
/// </summary>
public static class SmtResultWords
{
    #region Constants

    /// <summary>The word of a verdict, a reason or a proof that is not there.</summary>
    public const string NONE = "none";

    /// <summary>Status: the query was answered, or one of its budgets ended it (the verdict is then <c>unknown</c>, with the reason).</summary>
    public const string OK = "ok";

    /// <summary>Status: the problem, a term beside it or the options were not taken; no solver ran.</summary>
    public const string REFUSED = "refused";

    /// <summary>Status: the solver reported an error in what it was given.</summary>
    public const string INPUT_ERROR = "input_error";

    /// <summary>Status: the session wrote more than its output cap.</summary>
    public const string OUTPUT_EXCEEDED = "output_exceeded";

    /// <summary>Status: the solver trapped, ended with a status that is no answer, or answered something that cannot be read.</summary>
    public const string ENGINE_CRASH = "engine_crash";

    /// <summary>Status: the machine, not the problem; the row says nothing about the problem.</summary>
    public const string NODE_FAULT = "node_fault";

    /// <summary>Verdict: a model was found.</summary>
    public const string SAT = "sat";

    /// <summary>Verdict: there is no model.</summary>
    public const string UNSAT = "unsat";

    /// <summary>Verdict: the solver gave up or a budget ended it.</summary>
    public const string UNKNOWN = "unknown";

    /// <summary>Reason: the solver's resource limit of one check ended the check.</summary>
    public const string RLIMIT = "rlimit";

    /// <summary>Reason: the session's fuel, the hard cap, ended the session.</summary>
    public const string FUEL = "fuel";

    /// <summary>Reason: the solver was refused memory.</summary>
    public const string MEMOUT = "memout";

    /// <summary>Reason: the solver's procedure is not complete for the problem, by its own word.</summary>
    public const string INCOMPLETE = "incomplete";

    /// <summary>
    /// Reason: any other word of the solver's (<see cref="SmtResultData.ReasonText"/> keeps it); also of an enumeration that
    /// ended, its models kept, because the solver did not take a found model's values back.
    /// </summary>
    public const string OTHER = "other";

    /// <summary>Proof: the problem with the negated goal is unsatisfiable.</summary>
    public const string PROVED = "proved";

    /// <summary>Proof: a model of the problem in which the goal is false.</summary>
    public const string COUNTEREXAMPLE = "counterexample";

    /// <summary>The most characters of a row's <see cref="SmtResultData.Message"/>.</summary>
    public const int MAX_MESSAGE_CHARACTERS = 1_024;

    /// <summary>
    /// Every status of a node's row: the words a host holds a row a node returns to. A row of the
    /// results of a run says <see cref="SmtRunWords.STATUSES"/> instead: never <see cref="NODE_FAULT"/>,
    /// and the host's own <c>no_result</c> and <c>disputed</c> beside the others.
    /// </summary>
    public static readonly IReadOnlyList<string> STATUSES = [OK, REFUSED, INPUT_ERROR, OUTPUT_EXCEEDED, ENGINE_CRASH, NODE_FAULT];

    /// <summary>Every verdict.</summary>
    public static readonly IReadOnlyList<string> VERDICTS = [SAT, UNSAT, UNKNOWN, NONE];

    /// <summary>Every reason of an <c>unknown</c>.</summary>
    public static readonly IReadOnlyList<string> REASONS = [RLIMIT, FUEL, MEMOUT, INCOMPLETE, OTHER, NONE];

    /// <summary>Everything a proof can come to.</summary>
    public static readonly IReadOnlyList<string> PROOFS = [PROVED, COUNTEREXAMPLE, NONE];

    #endregion
}
