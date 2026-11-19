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
    /// Settles one shard of the dispute round.
    /// </summary>
    /// <param name="shard">The shard.</param>
    /// <param name="borrowable">The answers the other packages of the round are short of, together: as many of this shard's values may be one machine's second.</param>
    /// <param name="tally">Receives what is dropped.</param>
    public static void JudgeDispute(CorpusLedgerShard shard, int borrowable, CorpusLedgerTally tally)
    {
        // More values than machines were asked: none is taken.
        if (Taken(shard.Dispute) > CorpusLedger.DISPUTE_COPIES)
        {
            DropSurplus(shard.Dispute, tally);
            Leave(shard, CorpusLedger.DISPUTE_TOO_FEW_COPIES, 0);
            return;
        }

        var executions = Executions(shard.Dispute);
        if (executions.Count < CorpusLedger.DISPUTE_QUORUM)
        {
            Leave(shard, CorpusLedger.DISPUTE_TOO_FEW_COPIES, executions.Count);
            return;
        }

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
        {
            Leave(shard, CorpusLedger.DISPUTE_NO_MAJORITY, most);
            return;
        }

        // Two machines must have returned the leading result. Of its values, as many as the
        // other packages of the round are short of may be one machine's second, and do not count.
        var machines = most - Math.Max(0, borrowable);
        if (machines < CorpusLedger.DISPUTE_QUORUM || machines <= next)
        {
            Leave(shard, CorpusLedger.DISPUTE_SURPLUS_COPIES, most);
            return;
        }

        Accept(shard, groups[0][0], most);
        shard.DisputeOutcome = CorpusLedger.DISPUTE_RESOLVED;
        shard.DisputeAgreeing = most;
    }

    /// <summary>A dispute that was not settled: why, and how many of its executions agreed at the most.</summary>
    private static void Leave(CorpusLedgerShard shard, string outcome, int agreeing)
    {
        shard.DisputeOutcome = outcome;
        shard.DisputeAgreeing = agreeing;
    }

    /// <summary>What the shard ends as once no further round is coming.</summary>
    /// <param name="shard">The shard, judged round by round.</param>
    public static void Close(CorpusLedgerShard shard)
    {
        shard.Faults = shard.First.Concat(shard.Repair).Concat(shard.Dispute)
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
