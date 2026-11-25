namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// What the host keeps of one run on its own disk while it settles it, at the most: the rows the
/// nodes returned (the spool, <see cref="SmtSpool"/>) and the full models of the rows whose models
/// the results cut short (the ledger's section of them, <see cref="SmtLedger.MODELS"/>). A run that
/// passes either is not settled: the activity fails by name
/// (<see cref="SmtSettlementBoundException"/>) instead of filling the disk.
/// </summary>
/// <param name="RowsBytes">The most bytes of rows the spool holds.</param>
/// <param name="ModelsBytes">The most bytes the ledger's section of full models holds.</param>
internal sealed record SmtSettlementBounds(long RowsBytes, long ModelsBytes)
{
    #region Constants

    /// <summary>
    /// The most bytes of full models a run's ledger holds: 2 GiB. A ledger travels as one blob,
    /// and the host's blob service takes a blob whole, as much as one array holds: a run whose
    /// full models alone pass that has no ledger it could store.
    /// </summary>
    public const long MAX_MODELS_BYTES = 2L << 30;

    /// <summary>
    /// The most bytes of rows a run's spool holds: 128 GiB. What the largest run a set allows can
    /// write is under it: 1,000,000 problems, each answered by every copy the three fan-outs ask
    /// for at the most (2 + 2 + 3), are 7,000,000 rows; a row beside its models and values is at
    /// most 14 KiB (a transcript of 8 KiB, a message of 1,024 characters and a reason of 512 at
    /// three bytes a character, its words and its numbers), 94 GiB in all; and the models and
    /// values of the settled rows fit a ledger (<see cref="MAX_MODELS_BYTES"/>), so seven copies
    /// of them are 14 GiB: 108 GiB, and the rest is margin.
    /// </summary>
    public const long MAX_ROWS_BYTES = 128L << 30;

    /// <summary>The bounds of a run on a host.</summary>
    public static readonly SmtSettlementBounds DEFAULT = new(MAX_ROWS_BYTES, MAX_MODELS_BYTES);

    #endregion
}
