namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// One outcome of the solver as the tests look at it: every value of the controller's outcome
/// as a word, a number or a text, and the digest of all of them but the wall time.
/// <para>
/// The tests' own type, with nothing of the controller in its shape (see
/// <see cref="SmtSolverRig"/>): a fixture, a test's signature and a closure can hold it without
/// the controller's assembly being loaded when the tests are discovered.
/// </para>
/// </summary>
internal sealed class SmtSolved
{
    #region Functions

    /// <summary>One line of an outcome, for a test's log and for an assertion's message.</summary>
    /// <returns>The line.</returns>
    public override string ToString()
    {
        return $"{Status} {Code} verdict={Verdict} reason={Reason} '{ReasonText}' count={Count} complete={Complete} limit={LimitReached} proof={Proof} "
               + $"models={Models.Count} values={Values.Count} checks={Checks} resource={ResourceCount} fuel={Fuel} peak={PeakMemoryBytes} wall={WallMs} ms {Message}";
    }

    /// <summary>
    /// The outcome in a few words, as <see cref="SmtSolverCase.Expected"/> writes it: the status
    /// and its code, or for an answered query <c>ok</c>, the verdict, the reason when there is
    /// one, for an enumeration the count and <c>complete</c>, <c>limit</c> or <c>incomplete</c>,
    /// for a proof what it came to.
    /// </summary>
    /// <returns>The words.</returns>
    public string Words()
    {
        if (Status != "ok")
            return Code.Length > 0 ? $"{Status} {Code}" : Status;

        var words = $"ok {Verdict}";
        if (Reason != "none")
            words += $" {Reason}";

        if (Query == "enumerate")
            words += $" {Count} {(Complete ? "complete" : LimitReached ? "limit" : "incomplete")}";

        return Proof == "none" ? words : $"{words} {Proof}";
    }

    #endregion

    #region Properties

    /// <summary>The status: <c>ok</c>, <c>refused</c>, <c>input_error</c>, <c>output_exceeded</c>, <c>engine_crash</c>, <c>node_fault</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The fixed word of the outcome's kind, or empty.</summary>
    public required string Code { get; init; }

    /// <summary>The sentence that goes with the code, or empty.</summary>
    public required string Message { get; init; }

    /// <summary>The refusal's line, command and reason on one line; empty when nothing was refused.</summary>
    public required string Refusal { get; init; }

    /// <summary>The query: <c>check</c>, <c>enumerate</c>, <c>prove</c>.</summary>
    public required string Query { get; init; }

    /// <summary>The verdict: <c>sat</c>, <c>unsat</c>, <c>unknown</c>, <c>none</c>.</summary>
    public required string Verdict { get; init; }

    /// <summary>The reason: <c>rlimit</c>, <c>fuel</c>, <c>memout</c>, <c>incomplete</c>, <c>other</c>, <c>none</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>The solver's own word for an unknown, or empty.</summary>
    public required string ReasonText { get; init; }

    /// <summary>The models an enumeration found.</summary>
    public required int Count { get; init; }

    /// <summary>Whether an enumeration is complete.</summary>
    public required bool Complete { get; init; }

    /// <summary>Whether an enumeration stopped at its number of models.</summary>
    public required bool LimitReached { get; init; }

    /// <summary>What a proof came to: <c>proved</c>, <c>counterexample</c>, <c>none</c>.</summary>
    public required string Proof { get; init; }

    /// <summary>The models as the solver printed them.</summary>
    public required IReadOnlyList<string> Models { get; init; }

    /// <summary>The values as the solver printed them.</summary>
    public required IReadOnlyList<string> Values { get; init; }

    /// <summary>The checks the session sent.</summary>
    public required int Checks { get; init; }

    /// <summary>The solver's resource counter.</summary>
    public required long ResourceCount { get; init; }

    /// <summary>The fuel the session used.</summary>
    public required long Fuel { get; init; }

    /// <summary>The peak of the solver's linear memory, bytes.</summary>
    public required long PeakMemoryBytes { get; init; }

    /// <summary>The end of the transcript.</summary>
    public required string Transcript { get; init; }

    /// <summary>The wall time of the session, milliseconds: not in <see cref="Canonical"/>, not in <see cref="Digest"/>.</summary>
    public required int WallMs { get; init; }

    /// <summary>Every value above but the wall time, written out one after the other with its name and its length.</summary>
    public required string Canonical { get; init; }

    /// <summary>The SHA-256 of the UTF-8 bytes of <see cref="Canonical"/>, in lower-case hexadecimal.</summary>
    public required string Digest { get; init; }

    #endregion
}
