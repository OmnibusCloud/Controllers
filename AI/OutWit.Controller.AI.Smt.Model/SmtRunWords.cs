namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// The words of a run's documents that a node's row does not have: the statuses the host gives
/// a problem (refused by the problem set, without a result, disputed), how a problem's answer was
/// settled, what the host's evaluator said of a model, how a verdict stands against the expected
/// one, the words of the repair and the dispute rounds, and whether the platform told the host
/// which package each result was returned for. A document names no machine: these words say how
/// many copies there were and how they compared, never where they ran.
/// </summary>
public static class SmtRunWords
{
    #region Constants

    /// <summary>Status: the row of the problem set was refused (its code is the refusal's); no node saw it.</summary>
    public const string REFUSED = SmtResultWords.REFUSED;

    /// <summary>Status: no node answered the problem, after the repair round either; the code says what the nodes said last.</summary>
    public const string NO_RESULT = "no_result";

    /// <summary>Status: the copies of the problem disagreed and three more did not settle it; no answer is given.</summary>
    public const string DISPUTED = "disputed";

    /// <summary>Settled by: one copy, which nothing contradicted and the evaluator did not call failed.</summary>
    public const string ONE_COPY = "one_copy";

    /// <summary>
    /// Settled by: two or more copies with one digest, which the evaluator did not call failed, two
    /// of them returned for two tasks of one fan-out as the platform told the host (<see cref="BOUND"/>).
    /// </summary>
    public const string COPIES = "copies";

    /// <summary>Settled by: the majority of three more copies, after the first ones disagreed or the evaluator called a model failed.</summary>
    public const string DISPUTE = "dispute";

    /// <summary>
    /// Settled by: the machines agree and the evaluator still calls the model failed. The answer
    /// is the machines'; the integrity document counts the problem as a disagreement between the
    /// evaluator and the solver, and no machine as lying.
    /// </summary>
    public const string AGREEMENT = "agreement";

    /// <summary>Validation: every assertion of the problem is true under the model, and every value the row reports is the value its term has under the model.</summary>
    public const string VALIDATED = "validated";

    /// <summary>Validation: the evaluator could not decide every assertion or every reported value, or the values are not those of the terms the row asked for; the answer rests on the copies.</summary>
    public const string PARTIAL = "partial";

    /// <summary>Validation: an assertion is false under the model, or a reported value is another than its term's, with nothing guessed.</summary>
    public const string FAILED = "failed";

    /// <summary>Validation: the row has no model to check (a verdict other than <c>sat</c>, or no verdict).</summary>
    public const string NOT_CHECKED = "none";

    /// <summary>The reason of a partial validation of a row whose session wrote more than its output cap: its model did not come whole.</summary>
    public const string OUTPUT_EXCEEDED = SmtResultWords.OUTPUT_EXCEEDED;

    /// <summary>
    /// The reason of a partial validation of a row whose models the host's evaluator did not check
    /// to the end: the result package the row came in had used up the steps the host gives the
    /// models of one package. The answer rests on the copies.
    /// </summary>
    public const string BUDGET_PACKAGE = "budget_package";

    /// <summary>Expected: the verdict is the one the row or the problem's <c>:status</c> expects.</summary>
    public const string AGREE = "agree";

    /// <summary>Expected: the verdict is the other one.</summary>
    public const string DISAGREE = "disagree";

    /// <summary>Expected: nothing was expected, or the verdict is no decision (<c>unknown</c>, or none).</summary>
    public const string NOT_APPLICABLE = "n/a";

    /// <summary>The reason of a problem without a result whose package no accepted answer came back for.</summary>
    public const string NO_ANSWER = "no_answer";

    /// <summary>
    /// The reason of a repair, and of a problem left without a result, whose copies were a node's
    /// refusal of it: a node refused a problem the host had taken. A node refuses only for what the
    /// host checks with the same code before it makes a package, so a refusal is no answer.
    /// </summary>
    public const string NODE_REFUSED = "node_refused";

    /// <summary>A dispute's cause: the copies' digests differ.</summary>
    public const string DIGESTS_DIFFER = "digests_differ";

    /// <summary>A dispute's cause: the evaluator called the model of the copies, or a value reported with it, failed.</summary>
    public const string MODEL_FAILED = "model_failed";

    /// <summary>A dispute's cause: more copies of the problem's package came back than were asked for.</summary>
    public const string SURPLUS = "surplus";

    /// <summary>A dispute's outcome: a majority of three more copies settled it.</summary>
    public const string RESOLVED = "resolved";

    /// <summary>A dispute's outcome: no answer was given by more copies than any other, and by two at least.</summary>
    public const string NO_MAJORITY = "no_majority";

    /// <summary>A dispute's outcome: fewer than two copies came back with an answer.</summary>
    public const string TOO_FEW_COPIES = "too_few_copies";

    /// <summary>
    /// A dispute's outcome: the leading answer does not lead by two machines once the answers
    /// that may be one machine's second are left out (as many of its unbound answers as other
    /// packages of the round are short of answers).
    /// </summary>
    public const string SURPLUS_COPIES = "surplus_copies";

    /// <summary>
    /// Binding: every result that came back was told by the platform (Grid 1.4.0 and later) to have
    /// been returned for a task of a package, and the host held it to the package it names. Two
    /// such answers of one package in one fan-out are two machines.
    /// </summary>
    public const string BOUND = "bound";

    /// <summary>
    /// Binding: no result that came back was told (a Grid older than 1.4.0, or a run that did not
    /// fan out through it): copies are still compared by digest, and no answer is ever counted as
    /// a second machine.
    /// </summary>
    public const string UNBOUND = "unbound";

    /// <summary>Binding: some results that came back were told and some were not; only the bound ones count as machines.</summary>
    public const string MIXED = "mixed";

    /// <summary>Binding: no result came back at all.</summary>
    public const string NO_RESULTS = "none";

    /// <summary>Every status a row of the results can have, the node's and the host's, in the order a report lists them.</summary>
    public static readonly IReadOnlyList<string> STATUSES = [.. SmtResultWords.STATUSES.Where(status => status != SmtResultWords.NODE_FAULT), NO_RESULT, DISPUTED];

    /// <summary>Every way an answer is settled.</summary>
    public static readonly IReadOnlyList<string> SETTLED_BY = [ONE_COPY, COPIES, DISPUTE, AGREEMENT];

    /// <summary>Every validation word of a row.</summary>
    public static readonly IReadOnlyList<string> VALIDATIONS = [VALIDATED, PARTIAL, FAILED, NOT_CHECKED];

    /// <summary>Every word of a row's agreement with its expected verdict.</summary>
    public static readonly IReadOnlyList<string> EXPECTATIONS = [AGREE, DISAGREE, NOT_APPLICABLE];

    /// <summary>Every cause of a dispute.</summary>
    public static readonly IReadOnlyList<string> DISPUTE_CAUSES = [DIGESTS_DIFFER, MODEL_FAILED, SURPLUS];

    /// <summary>Every outcome of a dispute.</summary>
    public static readonly IReadOnlyList<string> DISPUTE_OUTCOMES = [RESOLVED, NO_MAJORITY, TOO_FEW_COPIES, SURPLUS_COPIES];

    /// <summary>Every word of a run's binding: whether the platform told the host which package each result was returned for.</summary>
    public static readonly IReadOnlyList<string> BINDINGS = [BOUND, UNBOUND, MIXED, NO_RESULTS];

    #endregion
}
