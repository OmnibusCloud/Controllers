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

    /// <summary>A dispute for which more copies came back than for the others: one machine may have answered twice.</summary>
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
    /// The task packages of a dispute round that got more answers than the round's packages
    /// commonly did. Every package of the round is sent in the same number of copies, so one with
    /// more answers than the others has one that is not a machine's first (a node that holds two
    /// packages can spend its answer to one on the other): nothing is settled by counting there.
    /// "Commonly" is the median of the packages' answers, the lower one of two: a node that
    /// moves answers gives to one package what it takes from another, so the packages it gave to
    /// are never the lower half.
    /// </summary>
    /// <param name="dispute">The dispute round.</param>
    /// <returns>The tickets of those packages.</returns>
    public static HashSet<string> Crowded(VerifyRound dispute)
    {
        var copies = dispute.Issued
            .Select(unit => (unit.Ticket, Copies: dispute.Copies(unit.Ticket)))
            .Where(unit => unit.Copies > 0)
            .OrderBy(unit => unit.Copies)
            .ToList();

        if (copies.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var common = copies[(copies.Count - 1) / 2].Copies;
        return copies.Where(unit => unit.Copies > common).Select(unit => unit.Ticket).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Settles one task.
    /// </summary>
    /// <param name="index">The task.</param>
    /// <param name="main">The first fan-out.</param>
    /// <param name="repair">The repair round, or null when the script ran none.</param>
    /// <param name="dispute">The dispute round, or null when the script ran none.</param>
    /// <param name="crowded">The dispute round's packages with more answers than the others (<see cref="Crowded"/>).</param>
    /// <returns>The ruling.</returns>
    public static VerifyRuling Rule(int index, VerifyRound main, VerifyRound? repair, VerifyRound? dispute, IReadOnlySet<string> crowded)
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

        return Settle(index, verdicts.Count, dispute, crowded);
    }

    private static VerifyRuling Settle(int index, int firstCopies, VerifyRound? dispute, IReadOnlySet<string> crowded)
    {
        if (dispute == null || !dispute.UnitOf.TryGetValue(index, out var unit))
            return VerifyRuling.Dispute(index, firstCopies, 0, 0, TOO_FEW_COPIES);

        var votes = dispute.Verdicts(index);
        if (crowded.Contains(unit.Ticket))
            return VerifyRuling.Dispute(index, firstCopies, votes.Count, 0, SURPLUS_COPIES);

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

        return most >= MAJORITY && most > next
            ? VerifyRuling.Accept(index, groups[0][0], most, repaired: false, new VerifyDisputeRecord(index, firstCopies, votes.Count, most, RESOLVED))
            : VerifyRuling.Dispute(index, firstCopies, votes.Count, most, NO_MAJORITY);
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
