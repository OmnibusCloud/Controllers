using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// One copy of one problem's row as the host keeps it while it settles a run: what it is
/// compared by (the digest), the words it is ruled on, what it cost, and where the whole row lies
/// in the spool. Small: models are never held here.
/// </summary>
internal sealed class SmtCopy
{
    #region Properties

    /// <summary>The problem.</summary>
    public required int Index { get; init; }

    /// <summary>The row's digest, held to the row before it was taken.</summary>
    public required SmtDigestKey Digest { get; init; }

    /// <summary>The row's status, one of <see cref="SmtResultWords.STATUSES"/>.</summary>
    public required string Status { get; init; }

    /// <summary>The row's verdict, one of <see cref="SmtResultWords.VERDICTS"/>.</summary>
    public required string Verdict { get; init; }

    /// <summary>The row's code: for a row of a fault of the node, what it was.</summary>
    public required string Code { get; init; }

    /// <summary>The solver's resource counter the row reports.</summary>
    public required long ResourceCount { get; init; }

    /// <summary>The fuel the row reports.</summary>
    public required long Fuel { get; init; }

    /// <summary>The ticket of the package the row came in.</summary>
    public required string Ticket { get; init; }

    /// <summary>
    /// Whether the row came in a result the platform told the host was returned for a task of its
    /// package (<see cref="SmtResultBindings"/>). Bound answers of one package in one fan-out came
    /// back for different tasks of it, which Grid keeps on different machines; unbound answers may
    /// all be one machine's.
    /// </summary>
    public bool Bound { get; init; }

    /// <summary>Where the whole row lies in the spool, or -1 when it was not kept.</summary>
    public long SpoolOffset { get; init; } = -1;

    /// <summary>The length of the whole row in the spool.</summary>
    public int SpoolLength { get; init; }

    /// <summary>Whether the row is a statement about the problem: everything but a fault of the node.</summary>
    public bool IsAnswer => !string.Equals(Status, SmtResultWords.NODE_FAULT, StringComparison.Ordinal);

    /// <summary>Whether the row says <c>sat</c> with what may be a model for the host's evaluator to check.</summary>
    public bool ClaimsSat => IsAnswer && string.Equals(Verdict, SmtResultWords.SAT, StringComparison.Ordinal);

    #endregion
}
