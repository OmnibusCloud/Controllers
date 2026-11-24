namespace OutWit.Controller.AI.Smt.Tests.Mock;

/// <summary>
/// One row of the long-tail kit's list (<c>kit-list.tsv</c>), the columns the solver's sample test
/// reads.
/// </summary>
internal sealed class SmtLongTailRow
{
    #region Properties

    /// <summary>The problem's id in the list: the name of its file.</summary>
    public required string Id { get; init; }

    /// <summary>The problem's logic.</summary>
    public required string Logic { get; init; }

    /// <summary>The SHA-256 of the problem's file.</summary>
    public required string Sha256 { get; init; }

    /// <summary>The problem's <c>:status</c>: <c>sat</c> or <c>unsat</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Z3's CPU time at the competition the list took it from, seconds.</summary>
    public required double Seconds { get; init; }

    /// <summary>The band of that time: <c>1-2</c>, <c>2-5</c>, ... <c>40-60</c>.</summary>
    public required string Band { get; init; }

    /// <summary>Whether the list marks the problem as one of the sample the build server solves.</summary>
    public required bool Sample { get; init; }

    #endregion
}
