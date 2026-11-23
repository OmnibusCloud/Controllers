using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The rules by which the host settles a problem from what the nodes returned. It never chooses
/// between two copies by what a node says of itself, and compares copies by their digests only:
/// <list type="bullet">
/// <item>a problem without an answer from the first fan-out (no taken row, or only rows of a fault
/// of the node) is sent out once more, under the sampling of the first; if it still has none, it
/// has no result, with the code of what the nodes said last;</item>
/// <item>copies that give one digest are one answer, and so is the one copy of a problem that was
/// not sampled (it rests on one machine, and the documents say so). The answer's every model is
/// checked by the host's evaluator;</item>
/// <item>copies whose digests differ, and an answer whose model the evaluator calls failed, are a
/// dispute: the problem goes to three more machines, and the answer is the digest the most of them
/// give, two at least, with no tie;</item>
/// <item>a failed model accuses nobody by itself: a copy is a lie only when it differs from the
/// settled answer of the others. When the machines agree and the evaluator still calls the model
/// failed, the answer is the machines' and it is settled by <see cref="SmtRunWords.AGREEMENT"/>: a
/// disagreement between the evaluator and the solver, which the integrity document counts as
/// such and not as a lie.</item>
/// </list>
/// <para>
/// <b>A dependency on the platform</b>, as for Verify: the host sees results, not machines.
/// Counting answers as machines rests on Grid keeping one value for each return variable a node's
/// tasks asked for (Grid 1.3.0 and later); held to it, a node that answers one package of a round
/// twice has left another package of that round short, so in a dispute round an answer counts
/// towards "two machines" only as far as it cannot be such a second answer (<see cref="Borrowable"/>).
/// </para>
/// </summary>
internal static class SmtRulings
{
    #region Constants

    /// <summary>The copies a dispute asks for.</summary>
    public const int DISPUTE_COPIES = 3;

    /// <summary>The copies the first fan-out asks for of a sampled package.</summary>
    public const int SAMPLE_COPIES = 2;

    /// <summary>The fewest copies that must give an answer for it to settle a dispute.</summary>
    public const int MAJORITY = 2;

    #endregion

    #region Functions

    /// <summary>Whether a problem has no answer from the first fan-out and goes out once more.</summary>
    /// <param name="main">The first fan-out.</param>
    /// <param name="index">The problem.</param>
    /// <returns>True when no taken copy of it is an answer.</returns>
    public static bool NeedsRepair(SmtRound main, int index)
    {
        return main.Answers(index).Count == 0;
    }

    /// <summary>
    /// Why the answers of a problem in a fan-out that samples (the first one, or a repair round run
    /// under the same options) cannot settle it, or null when they can.
    /// </summary>
    /// <param name="round">The fan-out.</param>
    /// <param name="index">The problem.</param>
    /// <param name="check">The evaluator's check of a copy.</param>
    /// <returns>A cause of <see cref="SmtRunWords.DISPUTE_CAUSES"/>, or null.</returns>
    public static string? Cause(SmtRound round, int index, Func<SmtCopy, SmtModelCheck> check)
    {
        var answers = round.Answers(index);
        if (answers.Count == 0)
            return null;

        if (round.UnitOf.TryGetValue(index, out var unit) && round.Surplus.Contains(unit.Ticket))
            return SmtRunWords.SURPLUS;

        if (answers.Any(answer => answer.Digest != answers[0].Digest))
            return SmtRunWords.DIGESTS_DIFFER;

        return check(answers[0]).IsFailed ? SmtRunWords.MODEL_FAILED : null;
    }

    /// <summary>
    /// For every package of a dispute round, the most of its answers that may be a second answer
    /// of one machine: every package of the round was asked for in <see cref="DISPUTE_COPIES"/>
    /// copies, so the answers a node can spend on a package beyond its own are the answers the
    /// other packages of the round are short of.
    /// </summary>
    /// <param name="dispute">The dispute round.</param>
    /// <returns>By ticket: the answers the other packages of the round are short of, together.</returns>
    public static Dictionary<string, int> Borrowable(SmtRound dispute)
    {
        var missing = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var unit in dispute.Issued)
            missing[unit.Ticket] = Math.Max(0, DISPUTE_COPIES - dispute.Returned.GetValueOrDefault(unit.Ticket));

        var total = missing.Values.Sum();
        return missing.ToDictionary(pair => pair.Key, pair => total - pair.Value, StringComparer.Ordinal);
    }

    /// <summary>Settles one problem.</summary>
    /// <param name="index">The problem.</param>
    /// <param name="main">The first fan-out.</param>
    /// <param name="repair">The repair round, or null when the script ran none.</param>
    /// <param name="dispute">The dispute round, or null when the script ran none.</param>
    /// <param name="borrowable">For the dispute round's packages, the answers that may be a machine's second (<see cref="Borrowable"/>).</param>
    /// <param name="check">The evaluator's check of a copy.</param>
    /// <param name="reason">The code of what the nodes said last, for a problem that has no answer at all.</param>
    /// <returns>The ruling.</returns>
    public static SmtRuling Rule(int index, SmtRound main, SmtRound? repair, SmtRound? dispute, IReadOnlyDictionary<string, int> borrowable,
        Func<SmtCopy, SmtModelCheck> check, Func<string> reason)
    {
        var round = main;
        var first = main.Answers(index);
        var repaired = false;

        if (first.Count == 0)
        {
            if (repair == null || repair.Answers(index).Count == 0)
                return SmtRuling.Without(index, reason());

            round = repair;
            first = repair.Answers(index);
            repaired = true;
        }

        var cause = Cause(round, index, check);
        if (cause == null)
            return SmtRuling.Accept(index, first[0], check(first[0]), first.Count, agreed: true, first.Count >= SAMPLE_COPIES ? SmtRunWords.COPIES : SmtRunWords.ONE_COPY, repaired);

        return Settle(index, cause, first, dispute, borrowable, check, repaired);
    }

    #endregion

    #region Tools

    private static SmtRuling Settle(int index, string cause, List<SmtCopy> first, SmtRound? dispute, IReadOnlyDictionary<string, int> borrowable,
        Func<SmtCopy, SmtModelCheck> check, bool repaired)
    {
        if (dispute == null || !dispute.UnitOf.TryGetValue(index, out var unit))
            return SmtRuling.Disputed(index, new SmtDisputeRecord(index, cause, first.Count, 0, 0, SmtRunWords.TOO_FEW_COPIES), first.Count, repaired);

        var votes = dispute.Answers(index);
        if (votes.Count < MAJORITY)
            return SmtRuling.Disputed(index, new SmtDisputeRecord(index, cause, first.Count, votes.Count, votes.Count, SmtRunWords.TOO_FEW_COPIES), first.Count + votes.Count, repaired);

        // The copies that give one digest, the largest group first; ties in an order of the digests.
        var groups = votes
            .GroupBy(vote => vote.Digest)
            .Select(group => group.ToList())
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group[0].Digest.High)
            .ThenBy(group => group[0].Digest.Low)
            .ToList();

        var most = groups[0].Count;
        var next = groups.Count > 1 ? groups[1].Count : 0;

        if (most < MAJORITY || most <= next)
            return SmtRuling.Disputed(index, new SmtDisputeRecord(index, cause, first.Count, votes.Count, most, SmtRunWords.NO_MAJORITY), first.Count + votes.Count, repaired);

        // Two machines must have given the leading digest. Of its answers, as many as the other
        // packages of the round are short of may be one machine's second, and do not count.
        var machines = most - borrowable.GetValueOrDefault(unit.Ticket);
        if (machines < MAJORITY || machines <= next)
            return SmtRuling.Disputed(index, new SmtDisputeRecord(index, cause, first.Count, votes.Count, most, SmtRunWords.SURPLUS_COPIES), first.Count + votes.Count, repaired);

        var leader = groups[0][0];
        var copies = first.Count + votes.Count;
        var lies = first.Count(copy => copy.Digest != leader.Digest) + votes.Count(copy => copy.Digest != leader.Digest);
        var leaderCheck = check(leader);
        var settledBy = leaderCheck.IsFailed ? SmtRunWords.AGREEMENT : SmtRunWords.DISPUTE;

        return SmtRuling.Accept(index, leader, leaderCheck, copies, lies == 0, settledBy, repaired,
            new SmtDisputeRecord(index, cause, first.Count, votes.Count, most, SmtRunWords.RESOLVED), lies);
    }

    #endregion
}
