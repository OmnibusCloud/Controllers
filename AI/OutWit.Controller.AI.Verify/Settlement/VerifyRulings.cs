using OutWit.Controller.AI.Verify.Model;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// The rules by which the server settles a task from what the nodes returned. It never chooses
/// between two executions by what a node says:
/// <list type="bullet">
/// <item>a task with no verdict from the first fan-out (no accepted answer, or only faults of
/// the nodes) is sent out once more; if it still has none, it has no result;</item>
/// <item>executions that agree on everything that is compared are accepted, and so is the one
/// execution of a task that was not sampled (it rests on one machine, and the integrity
/// document counts it so);</item>
/// <item>executions that disagree are a dispute: the task runs three more times on different
/// machines, and the result is the one that the most of those three returned, at least two,
/// with no tie; otherwise the task is disputed.</item>
/// </list>
/// <para>
/// <b>A dependency on the platform.</b> The server sees results, not machines. Counting answers
/// as machines rests on a property the controller cannot check itself and does not assume to be
/// enforced: that a node returns one result for each package it was given. Where the platform
/// holds a node to that, a node that answers one package of a round twice has left another
/// package of that round without its answer, so in a dispute round an answer counts towards
/// "two machines" only as far as it cannot be such a second answer (<see cref="Borrowable"/>):
/// with one lying node among the copies, whatever the others lose, the result that is accepted
/// was returned by an honest machine.
/// </para>
/// <para>
/// What holds by the controller alone, whatever the platform lets a node return: a node answers
/// only for packages it was given (tickets); a package with more answers than were asked for is
/// never settled by counting them (the first fan-out sends its tasks to a dispute, the repair
/// and the dispute round take none of its answers); so a node does not win a dispute in which
/// all three copies came back. What needs the platform: when a copy of a disputed package did
/// not come back, a second answer of one node can take its place, and the controller alone
/// cannot tell that from two machines outvoting a third. A package that was not sampled rests
/// on the machine that ran it either way; a second answer to it only makes the count of
/// executions say two.
/// </para>
/// </summary>
internal static class VerifyRulings
{
    #region Constants

    /// <summary>The copies a dispute asks for.</summary>
    public const int DISPUTE_COPIES = 3;

    /// <summary>The copies the first fan-out asks for of a sampled package.</summary>
    public const int SAMPLE_COPIES = 2;

    /// <summary>The fewest copies that must return a result for it to settle a dispute.</summary>
    public const int MAJORITY = 2;

    /// <summary>A dispute settled by a majority.</summary>
    public const string RESOLVED = "resolved";

    /// <summary>A dispute in which no result was returned by more copies than any other, and by two at least.</summary>
    public const string NO_MAJORITY = "no_majority";

    /// <summary>A dispute for which fewer than two copies came back with a verdict.</summary>
    public const string TOO_FEW_COPIES = "too_few_copies";

    /// <summary>
    /// A dispute whose leading result does not lead by two machines once the answers that may be
    /// one machine's second are left out: other packages of the round are short of answers.
    /// </summary>
    public const string SURPLUS_COPIES = "surplus_copies";

    #endregion

    #region Functions

    /// <summary>Whether a task has no verdict from the first fan-out and goes out once more.</summary>
    /// <param name="main">The first fan-out.</param>
    /// <param name="index">The task.</param>
    /// <returns>True when no accepted execution of it is a verdict.</returns>
    public static bool NeedsRepair(VerifyRound main, int index)
    {
        return main.Verdicts(index).Count == 0;
    }

    /// <summary>Whether the executions of a task in the first fan-out make a dispute.</summary>
    /// <param name="main">The first fan-out.</param>
    /// <param name="index">The task.</param>
    /// <returns>True when its verdicts disagree, or when more answers came back for its package than were asked for.</returns>
    public static bool IsDisputed(VerifyRound main, int index)
    {
        var verdicts = main.Verdicts(index);
        if (verdicts.Count == 0)
            return false;

        if (main.UnitOf.TryGetValue(index, out var unit) && main.Surplus.Contains(unit.Ticket))
            return true;

        return verdicts.Skip(1).Any(other => !VerifyComparable.Agree(verdicts[0], other));
    }

    /// <summary>
    /// For every task package of a dispute round, the most of its answers that may be a second
    /// answer of one machine, where the platform holds a node to one result for each package it
    /// was given (a dependency: see the class). Every package of the round was asked for in
    /// <see cref="DISPUTE_COPIES"/> copies, so the answers a node can then spend on a package
    /// beyond its own are the answers the other packages of the round are short of.
    /// </summary>
    /// <param name="dispute">The dispute round.</param>
    /// <returns>By ticket: the answers the other packages of the round are short of, together.</returns>
    public static Dictionary<string, int> Borrowable(VerifyRound dispute)
    {
        var missing = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var unit in dispute.Issued)
            missing[unit.Ticket] = Math.Max(0, DISPUTE_COPIES - dispute.Returned.GetValueOrDefault(unit.Ticket));

        var total = missing.Values.Sum();
        return missing.ToDictionary(pair => pair.Key, pair => total - pair.Value, StringComparer.Ordinal);
    }

    /// <summary>
    /// Settles one task.
    /// </summary>
    /// <param name="index">The task.</param>
    /// <param name="main">The first fan-out.</param>
    /// <param name="repair">The repair round, or null when the script ran none.</param>
    /// <param name="dispute">The dispute round, or null when the script ran none.</param>
    /// <param name="borrowable">For the dispute round's packages, the answers that may be a machine's second (<see cref="Borrowable"/>).</param>
    /// <returns>The ruling.</returns>
    public static VerifyRuling Rule(int index, VerifyRound main, VerifyRound? repair, VerifyRound? dispute, IReadOnlyDictionary<string, int> borrowable)
    {
        var verdicts = main.Verdicts(index);

        if (verdicts.Count == 0)
        {
            var repaired = repair?.Verdicts(index) ?? [];
            return repaired.Count > 0
                ? VerifyRuling.Accept(index, repaired[0], 1, repaired: true)
                : VerifyRuling.Without(index, FaultOf(main.Of(index), repair?.Of(index) ?? []));
        }

        if (!IsDisputed(main, index))
            return VerifyRuling.Accept(index, verdicts[0], verdicts.Count, repaired: false);

        return Settle(index, verdicts.Count, dispute, borrowable);
    }

    private static VerifyRuling Settle(int index, int firstCopies, VerifyRound? dispute, IReadOnlyDictionary<string, int> borrowable)
    {
        if (dispute == null || !dispute.UnitOf.TryGetValue(index, out var unit))
            return VerifyRuling.Dispute(index, firstCopies, 0, 0, TOO_FEW_COPIES);

        var votes = dispute.Verdicts(index);
        if (votes.Count < MAJORITY)
            return VerifyRuling.Dispute(index, firstCopies, votes.Count, votes.Count, TOO_FEW_COPIES);

        // The executions that agree with one another, the largest group first.
        var groups = new List<List<VerifyExecution>>();
        foreach (var vote in votes)
        {
            var same = groups.FirstOrDefault(group => VerifyComparable.Agree(group[0], vote));
            if (same != null)
                same.Add(vote);
            else
                groups.Add([vote]);
        }

        groups = groups.OrderByDescending(group => group.Count).ToList();
        var most = groups[0].Count;
        var next = groups.Count > 1 ? groups[1].Count : 0;

        if (most < MAJORITY || most <= next)
            return VerifyRuling.Dispute(index, firstCopies, votes.Count, most, NO_MAJORITY);

        // Two machines must have returned the leading result. Of its answers, as many as the
        // other packages of the round are short of may be one machine's second, and do not count.
        var machines = most - borrowable.GetValueOrDefault(unit.Ticket);

        return machines >= MAJORITY && machines > next
            ? VerifyRuling.Accept(index, groups[0][0], most, repaired: false, new VerifyDisputeRecord(index, firstCopies, votes.Count, most, RESOLVED))
            : VerifyRuling.Dispute(index, firstCopies, votes.Count, most, SURPLUS_COPIES);
    }

    /// <summary>
    /// The reason of a task without a result: what the nodes said last. The clock's code when a
    /// node's backstop ended it, the node's own when a node failed it, and "no answer" when
    /// nothing that was accepted came back.
    /// </summary>
    private static string FaultOf(IReadOnlyList<VerifyExecution> first, IReadOnlyList<VerifyExecution> second)
    {
        foreach (var executions in new[] { second, first })
        {
            if (executions.Any(execution => execution.Fault == VerifyFault.WALL_CLOCK))
                return VerifyFault.WALL_CLOCK;

            if (executions.Any(execution => execution.Fault == VerifyFault.NODE_FAULT))
                return VerifyFault.NODE_FAULT;
        }

        return VerifyFault.NO_ANSWER;
    }

    #endregion
}
