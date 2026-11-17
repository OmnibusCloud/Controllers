namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// What one reading of a run counts. The ledger keeps these and never the shards: a run of any
/// length is a handful of numbers here.
/// </summary>
internal sealed class CorpusLedgerTally
{
    #region Fields

    private readonly SortedDictionary<string, int> m_rejected = new(StringComparer.Ordinal);

    #endregion

    #region Functions

    /// <summary>Counts values dropped for a reason.</summary>
    /// <param name="reason">One of <see cref="CorpusLedger.REJECTION_REASONS"/>.</param>
    /// <param name="count">How many.</param>
    public void Reject(string reason, int count = 1)
    {
        m_rejected[reason] = m_rejected.GetValueOrDefault(reason) + count;
        RejectedResults += count;
    }

    /// <summary>Counts what a round's source dropped before a shard was known for it.</summary>
    /// <param name="dropped">Dropped values by reason, or null.</param>
    public void Add(IReadOnlyDictionary<string, int>? dropped)
    {
        if (dropped == null)
            return;

        foreach (var (reason, count) in dropped)
            Reject(reason, count);
    }

    /// <summary>Counts a shard that has been judged.</summary>
    /// <param name="shard">The shard.</param>
    public void Add(CorpusLedgerShard shard)
    {
        var accepted = shard.Outcome == CorpusShardOutcome.Accepted;

        NodeFaults += Faults(shard.First) + Faults(shard.Repair) + Faults(shard.Dispute);
        if (shard.SentToRepair)
            Repairs++;
        if (shard.SentToRepair && !shard.SentToDispute && accepted)
            Repaired++;
        if (shard.NeedsDispute)
            Disputes++;
        if (shard.NeedsDispute && accepted)
            DisputesResolved++;
        if (accepted && shard.Copies == 1)
            SingleExecutions++;
        if (shard.NeedsRepair && !shard.SentToRepair)
            PendingRepairs++;
        if (shard.NeedsDispute && !shard.SentToDispute)
            PendingDisputes++;
    }

    #endregion

    #region Tools

    private static int Faults(List<CorpusLedgerEntry> entries)
    {
        var faults = 0;
        foreach (var entry in entries)
        {
            if (entry.Kind == CorpusLedgerEntryKind.Fault)
                faults++;
        }

        return faults;
    }

    #endregion

    #region Properties

    /// <summary>Values dropped over every round.</summary>
    public int RejectedResults { get; private set; }

    /// <summary>The dropped values by reason, reasons in ordinal order.</summary>
    public IReadOnlyDictionary<string, int> Rejected => m_rejected;

    /// <summary>Shards returned as a node fault over every round.</summary>
    public int NodeFaults { get; private set; }

    /// <summary>Shards the repair round took.</summary>
    public int Repairs { get; private set; }

    /// <summary>Shards the repair round gave a result.</summary>
    public int Repaired { get; private set; }

    /// <summary>Shards whose copies disagreed.</summary>
    public int Disputes { get; private set; }

    /// <summary>Of those, the ones the dispute round settled.</summary>
    public int DisputesResolved { get; private set; }

    /// <summary>Shards accepted on one machine's word.</summary>
    public int SingleExecutions { get; private set; }

    /// <summary>Shards the first round left without a result that no repair round has taken yet.</summary>
    public int PendingRepairs { get; private set; }

    /// <summary>Shards whose copies disagreed that no dispute round has taken yet.</summary>
    public int PendingDisputes { get; private set; }

    #endregion
}
