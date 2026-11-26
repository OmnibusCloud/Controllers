namespace OutWit.Controller.AI.Smt.Solver;

/// <summary>
/// The fixed words an outcome names its kind by (<see cref="SmtSolveOutcome.Code"/>): beside the
/// refusal codes of the model package, the controller's own refusals, the kinds of a crash of
/// the solver and the kinds of a fault of the node.
/// </summary>
internal static class SmtSolveCodes
{
    #region Constants

    /// <summary>Refused: the problem's text is not what it was said to be (it hashes to another name than the one it came with).</summary>
    public const string HASH_MISMATCH = "hash_mismatch";

    /// <summary>Refused: the options name a profile this controller does not have.</summary>
    public const string PROFILE_UNKNOWN = "profile_unknown";

    /// <summary>Refused: an option is out of its range.</summary>
    public const string OPTIONS_INVALID = "options_invalid";

    /// <summary>Engine crash: a trap that is no budget stop.</summary>
    public const string TRAP = "trap";

    /// <summary>Engine crash: the solver ended by itself with a status that is no answer, or before the conversation was over.</summary>
    public const string EXIT = "exit";

    /// <summary>Engine crash: the engine failed one of the solver's calls without a trap or an exit.</summary>
    public const string ENGINE_ERROR = "engine_error";

    /// <summary>Engine crash: the solver answered a command with something that cannot be read as that command's answer.</summary>
    public const string UNREADABLE_ANSWER = "unreadable_answer";

    /// <summary>Node fault: this process has no sandbox (no module, another engine library, another binding).</summary>
    public const string SANDBOX_UNAVAILABLE = "sandbox_unavailable";

    /// <summary>Node fault: the session ran past the wall-clock backstop with fuel left.</summary>
    public const string BACKSTOP = "backstop";

    /// <summary>Node fault: the session was cancelled.</summary>
    public const string CANCELLED = "cancelled";

    /// <summary>Node fault: the module could not be instantiated on this machine.</summary>
    public const string NOT_INSTANTIATED = "not_instantiated";

    /// <summary>Node fault: the host failed outside the solver (a file it could not use).</summary>
    public const string HOST_FAULT = "host_fault";

    /// <summary>Every refusal of the controller's own, beside the refusal codes of the model package.</summary>
    public static readonly IReadOnlyList<string> REFUSALS = [HASH_MISMATCH, PROFILE_UNKNOWN, OPTIONS_INVALID];

    /// <summary>Every kind of a crash of the solver, in the order the documents list them.</summary>
    public static readonly IReadOnlyList<string> CRASHES = [TRAP, EXIT, ENGINE_ERROR, UNREADABLE_ANSWER];

    /// <summary>Every kind of a fault of the node, in the order the documents list them.</summary>
    public static readonly IReadOnlyList<string> FAULTS = [SANDBOX_UNAVAILABLE, BACKSTOP, CANCELLED, NOT_INSTANTIATED, HOST_FAULT];

    #endregion
}
