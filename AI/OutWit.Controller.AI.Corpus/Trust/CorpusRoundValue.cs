using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// One value that came back under a shard's ticket, as a round's source hands it to the ledger:
/// a result the ledger has yet to check, or what the source already knows of it.
/// </summary>
/// <param name="Kind">
/// <see cref="CorpusLedgerEntryKind.Execution"/> for a result the ledger checks (it may still turn
/// out a fault or be dropped); <see cref="CorpusLedgerEntryKind.Fault"/> for a shard of a package
/// its node could not answer as a whole; <see cref="CorpusLedgerEntryKind.Rejected"/> for a value
/// the source dropped itself.
/// </param>
/// <param name="Result">The result to check; null unless <paramref name="Kind"/> is an execution.</param>
/// <param name="Code">The fault's code or the reason of the rejection; "" for a result to check.</param>
public readonly record struct CorpusRoundValue(CorpusLedgerEntryKind Kind, CorpusResultData? Result, string Code)
{
    #region Functions

    /// <summary>A result to check.</summary>
    /// <param name="result">What a node returned under the ticket.</param>
    /// <returns>The value.</returns>
    public static CorpusRoundValue Of(CorpusResultData result)
    {
        return new CorpusRoundValue(CorpusLedgerEntryKind.Execution, result, "");
    }

    /// <summary>A shard of a package its node returned as a fault of its own.</summary>
    /// <param name="code">The fault's code.</param>
    /// <returns>The value.</returns>
    public static CorpusRoundValue Fault(string code)
    {
        return new CorpusRoundValue(CorpusLedgerEntryKind.Fault, null, code);
    }

    /// <summary>A value the source dropped.</summary>
    /// <param name="reason">One of <see cref="CorpusLedger.REJECTION_REASONS"/>.</param>
    /// <returns>The value.</returns>
    public static CorpusRoundValue Rejected(string reason)
    {
        return new CorpusRoundValue(CorpusLedgerEntryKind.Rejected, null, reason);
    }

    #endregion
}
