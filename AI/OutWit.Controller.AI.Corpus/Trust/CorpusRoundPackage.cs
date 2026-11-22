namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// One task package of a round as the ledger reads it: its tasks in ascending shard order, each
/// with the values that came back under its ticket, how many answers the package got, and the
/// values of those answers that belong to no task of the package.
/// </summary>
public sealed class CorpusRoundPackage
{
    #region Constructors

    /// <summary>Creates the package's record.</summary>
    /// <param name="slots">The package's tasks with their values, in ascending shard order.</param>
    /// <param name="returned">The answers bound to the package in this round, of any kind, whether they were opened or not.</param>
    /// <param name="dropped">Values of the package's answers that were dropped without reaching a task, by reason.</param>
    public CorpusRoundPackage(IReadOnlyList<CorpusRoundSlot> slots, int returned, IReadOnlyDictionary<string, int> dropped)
    {
        Slots = slots;
        Returned = returned;
        Dropped = dropped;
    }

    #endregion

    #region Properties

    /// <summary>The package's tasks with their values, in ascending shard order.</summary>
    public IReadOnlyList<CorpusRoundSlot> Slots { get; }

    /// <summary>The answers bound to the package in this round: what "the machines that answered it" is counted from.</summary>
    public int Returned { get; }

    /// <summary>Values dropped without reaching a task: no result, a ticket that is no task's of this package, more answers than a round opens.</summary>
    public IReadOnlyDictionary<string, int> Dropped { get; }

    #endregion
}
