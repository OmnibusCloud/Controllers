namespace OutWit.Controller.AI.Smt.Model.Lang;

/// <summary>
/// What becomes of a top-level command of a problem, by its name: kept (a declaration, a
/// definition, an assertion, the logic, a piece of information), stripped (what asks the solver
/// to act: the query decides what runs), or refused. A name that is in neither list is refused,
/// whatever it is: no command is passed on because nothing was known against it. Two of the
/// stripped commands end a problem: after <c>check-sat</c> and after <c>exit</c> nothing that
/// states the problem may follow.
/// </summary>
internal static class SmtCommandPolicy
{
    #region Constants

    public const string SET_LOGIC = "set-logic";
    public const string SET_INFO = "set-info";
    public const string DECLARE_SORT = "declare-sort";
    public const string DEFINE_SORT = "define-sort";
    public const string DECLARE_CONST = "declare-const";
    public const string DECLARE_FUN = "declare-fun";
    public const string DEFINE_FUN = "define-fun";
    public const string DEFINE_FUN_REC = "define-fun-rec";
    public const string DEFINE_FUNS_REC = "define-funs-rec";
    public const string DECLARE_DATATYPE = "declare-datatype";
    public const string DECLARE_DATATYPES = "declare-datatypes";
    public const string ASSERT = "assert";
    public const string CHECK_SAT = "check-sat";
    public const string EXIT = "exit";

    private const string SET_OPTION = "set-option";

    private const string REASON_INCREMENTAL = "incremental scripts, unsat cores and proofs are not supported";
    private const string REASON_OPTION = "the controller sets every option itself";
    private const string REASON_OTHER = "a problem holds declarations, definitions and assertions; no other command is taken";

    private static readonly HashSet<string> KEPT = new(StringComparer.Ordinal)
    {
        SET_LOGIC, SET_INFO, DECLARE_SORT, DEFINE_SORT, DECLARE_CONST, DECLARE_FUN, DEFINE_FUN, DEFINE_FUN_REC,
        DEFINE_FUNS_REC, DECLARE_DATATYPE, DECLARE_DATATYPES, ASSERT
    };

    private static readonly HashSet<string> STRIPPED = new(StringComparer.Ordinal)
    {
        CHECK_SAT, "get-model", "get-value", "get-info", EXIT
    };

    private static readonly HashSet<string> INCREMENTAL = new(StringComparer.Ordinal)
    {
        "push", "pop", "reset", "reset-assertions", "check-sat-assuming", "get-unsat-core", "get-unsat-assumptions",
        "get-assertions", "get-assignment", "get-proof", "get-option"
    };

    #endregion

    #region Functions

    /// <summary>
    /// Whether a command stays in the problem.
    /// </summary>
    /// <param name="head">The command's name.</param>
    /// <returns>True for a kept command.</returns>
    public static bool IsKept(string head)
    {
        return KEPT.Contains(head);
    }

    /// <summary>
    /// Whether a command is dropped and counted.
    /// </summary>
    /// <param name="head">The command's name.</param>
    /// <returns>True for a stripped command.</returns>
    public static bool IsStripped(string head)
    {
        return STRIPPED.Contains(head);
    }

    /// <summary>
    /// Whether a command reaches the solver: a kept command that is no <c>set-info</c>. The solver
    /// acts on some pieces of information (a <c>:status</c> that disagrees with its verdict is an
    /// error to it, <c>:smt-lib-version</c> changes how it reads), so it is given none.
    /// </summary>
    /// <param name="head">The command's name.</param>
    /// <returns>True for a command of the solver's text.</returns>
    public static bool IsForSolver(string head)
    {
        return KEPT.Contains(head) && !string.Equals(head, SET_INFO, StringComparison.Ordinal);
    }

    /// <summary>
    /// Why a command that is neither kept nor stripped is refused.
    /// </summary>
    /// <param name="head">The command's name.</param>
    /// <returns>The reason, in words.</returns>
    public static string WhyRefused(string head)
    {
        if (INCREMENTAL.Contains(head))
            return REASON_INCREMENTAL;

        return string.Equals(head, SET_OPTION, StringComparison.Ordinal) ? REASON_OPTION : REASON_OTHER;
    }

    #endregion
}
