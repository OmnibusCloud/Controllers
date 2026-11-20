using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// How the ledger settles a shard from the values of a round: the rulings of the first round, the
/// repair round and the dispute round, and what a shard ends as when no further round is coming.
/// The rules are <see cref="CorpusLedger"/>'s (its summary states them); nothing here reads a run.
/// </summary>
internal static class CorpusLedgerRuling
{
    #region Functions

    /// <summary>Settles a shard on the first round's values: accepted, to be repaired, or to be disputed.</summary>
    /// <param name="shard">The shard with its first round's entries.</param>
    public static void JudgeFirst(CorpusLedgerShard shard)
    {
        var executions = Executions(shard.First);

        // More answers than the round sent out: one machine answered twice, and nothing says which.
        if (shard.First.Count > CorpusLedger.FIRST_ROUND_COPIES || !Agree(executions))
            shard.NeedsDispute = true;
        else if (executions.Count == 0)
            shard.NeedsRepair = true;
        else
            Accept(shard, executions[0], executions.Count);
    }

    /// <summary>
    /// Settles a shard on the repair round's values, by the first round's rule: the round sends a
    /// shard out on two machines at the most, so copies that agree are accepted, and copies that
    /// disagree, or more values than two, are a dispute. A shard nothing came back for with a
    /// verdict stays without a result.
    /// </summary>
    /// <param name="shard">The shard with its repair round's entries.</param>
    public static void JudgeRepair(CorpusLedgerShard shard)
    {
        shard.NeedsRepair = false;
        var executions = Executions(shard.Repair);

        // More answers than the round sent out: one machine answered twice, and nothing says which.
        if (shard.Repair.Count > CorpusLedger.REPAIR_COPIES || !Agree(executions))
            shard.NeedsDispute = true;
        else if (executions.Count > 0)
            Accept(shard, executions[0], executions.Count);
    }

    /// <summary>
    /// Settles one shard of the dispute round. A dispute that ends with too few copies, or with a
    /// lead that may be one machine's second answers, goes out once more
    /// (<see cref="CorpusLedgerShard.NeedsDisputeAgain"/>): neither says the copies disagree.
    /// </summary>
    /// <param name="shard">The shard.</param>
    /// <param name="borrowable">How many of this shard's values may be one machine's second: what the other packages of the round are short of, no more than the round's silent slots.</param>
    /// <param name="tally">Receives what is dropped.</param>
    public static void JudgeDispute(CorpusLedgerShard shard, int borrowable, CorpusLedgerTally tally)
    {
        var (outcome, agreeing) = Rule(shard, shard.Dispute, borrowable, tally);
        shard.DisputeOutcome = outcome;
        shard.DisputeAgreeing = agreeing;
        shard.NeedsDisputeAgain = outcome is CorpusLedger.DISPUTE_TOO_FEW_COPIES or CorpusLedger.DISPUTE_SURPLUS_COPIES;
    }

    /// <summary>
    /// Settles one shard of the second dispute round, by the dispute round's rule: its outcome is
    /// the dispute's, and the first round's is kept as the reason it went out again.
    /// </summary>
    /// <param name="shard">The shard, sent out again.</param>
    /// <param name="borrowable">How many of this shard's values may be one machine's second in the second round.</param>
    /// <param name="tally">Receives what is dropped.</param>
    public static void JudgeDisputeAgain(CorpusLedgerShard shard, int borrowable, CorpusLedgerTally tally)
    {
        shard.AgainAfter = shard.DisputeOutcome;
        var (outcome, agreeing) = Rule(shard, shard.Again, borrowable, tally);
        shard.DisputeOutcome = outcome;
        shard.AgainAgreeing = agreeing;
    }

    /// <summary>The rule of a dispute round over its values for a shard: how it ended, and how many executions agreed at the most.</summary>
    private static (string Outcome, int Agreeing) Rule(CorpusLedgerShard shard, List<CorpusLedgerEntry> entries, int borrowable, CorpusLedgerTally tally)
    {
        // More values than machines were asked: none is taken.
        if (Taken(entries) > CorpusLedger.DISPUTE_COPIES)
        {
            DropSurplus(entries, tally);
            return (CorpusLedger.DISPUTE_TOO_FEW_COPIES, 0);
        }

        var executions = Executions(entries);
        if (executions.Count < CorpusLedger.DISPUTE_QUORUM)
            return (CorpusLedger.DISPUTE_TOO_FEW_COPIES, executions.Count);

        // The executions that agree with one another, the largest group first.
        var groups = new List<List<CorpusResultData>>();
        foreach (var execution in executions)
        {
            var same = groups.FirstOrDefault(group => CorpusLedger.Differences(group[0], execution).Count == 0);
            if (same == null)
                groups.Add([execution]);
            else
                same.Add(execution);
        }

        groups = groups.OrderByDescending(group => group.Count).ToList();
        var most = groups[0].Count;
        var next = groups.Count > 1 ? groups[1].Count : 0;

        if (most < CorpusLedger.DISPUTE_QUORUM || most <= next)
            return (CorpusLedger.DISPUTE_NO_MAJORITY, most);

        // Two machines must have returned the leading result. Of its values, as many as may be
        // one machine's second answers do not count.
        var machines = most - Math.Max(0, borrowable);
        if (machines < CorpusLedger.DISPUTE_QUORUM || machines <= next)
            return (CorpusLedger.DISPUTE_SURPLUS_COPIES, most);

        Accept(shard, groups[0][0], most);
        return (CorpusLedger.DISPUTE_RESOLVED, most);
    }

    /// <summary>What the shard ends as once no further round is coming.</summary>
    /// <param name="shard">The shard, judged round by round.</param>
    public static void Close(CorpusLedgerShard shard)
    {
        shard.Faults = shard.First.Concat(shard.Repair).Concat(shard.Dispute).Concat(shard.Again)
            .Where(entry => entry.Kind == CorpusLedgerEntryKind.Fault)
            .Select(entry => entry.Code)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (shard.Accepted != null)
            return;

        if (shard.NeedsDispute)
        {
            // A dispute no round was run for (or none held the shard) has no copies at all.
            if (shard.DisputeOutcome.Length == 0)
                shard.DisputeOutcome = CorpusLedger.DISPUTE_TOO_FEW_COPIES;

            shard.Outcome = CorpusShardOutcome.Disputed;
            return;
        }

        // What the nodes said: a fault of a node's own when one returned the shard so, and
        // "no answer" when nothing that was accepted came back.
        shard.Outcome = CorpusShardOutcome.NoResult;
        shard.Reason = shard.First.Concat(shard.Repair).Any(entry => entry.Kind == CorpusLedgerEntryKind.Fault) ? CorpusLedger.NO_RESULT_NODE_FAULT : CorpusLedger.NO_RESULT_NO_ANSWER;
    }

    private static void Accept(CorpusLedgerShard shard, CorpusResultData result, int copies)
    {
        shard.Outcome = CorpusShardOutcome.Accepted;
        shard.Accepted = result;
        shard.Copies = copies;
        shard.NeedsRepair = false;
    }

    /// <summary>The values of a round that were not dropped on arrival: executions and node faults.</summary>
    private static int Taken(IEnumerable<CorpusLedgerEntry> entries)
    {
        return entries.Count(entry => entry.Kind != CorpusLedgerEntryKind.Rejected);
    }

    private static List<CorpusResultData> Executions(IEnumerable<CorpusLedgerEntry> entries)
    {
        return entries.Where(entry => entry.Kind == CorpusLedgerEntryKind.Execution).Select(entry => entry.Result!).ToList();
    }

    private static bool Agree(IReadOnlyList<CorpusResultData> executions)
    {
        for (var index = 1; index < executions.Count; index++)
        {
            if (CorpusLedger.Differences(executions[0], executions[index]).Count > 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// More values came back under a ticket than the dispute round asked for, the round that
    /// settles a shard for good: which of them is the answer cannot be told, so none is. Each
    /// value that would have been taken becomes a rejection
    /// (<see cref="CorpusLedger.REJECTED_SURPLUS"/>); one that was dropped for a reason of its own
    /// keeps it.
    /// </summary>
    private static void DropSurplus(List<CorpusLedgerEntry> entries, CorpusLedgerTally tally)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Kind == CorpusLedgerEntryKind.Rejected)
                continue;

            entries[index] = new CorpusLedgerEntry(CorpusLedgerEntryKind.Rejected, null, CorpusLedger.REJECTED_SURPLUS);
            tally.Reject(CorpusLedger.REJECTED_SURPLUS);
        }
    }

    #endregion
}
