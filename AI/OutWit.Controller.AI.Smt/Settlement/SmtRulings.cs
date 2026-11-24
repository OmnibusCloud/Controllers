using OutWit.Controller.AI.Smt.Model;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The rules by which the host settles a problem from what the nodes returned. It never chooses
/// between two copies by what a node says of itself, and compares copies by their digests only:
/// <list type="bullet">
/// <item>a problem without an answer from the first fan-out (no taken row, or only rows of a fault
/// of the node) is sent out once more, and so is a problem of a package the first fan-out asked
/// for twice that came back with fewer than two answers (<see cref="NeedsRepair"/>): the repair
/// round asks two machines for each of its packages. A problem that still has no answer has no
/// result, with the code of what the nodes said last;</item>
/// <item>copies that give one digest are one answer, and so is the one copy of a problem that was
/// not sampled (it rests on one machine, and the documents say so). Two answers rest on two
/// machines only when they come from one fan-out: Grid keeps the copies of one fan-out on
/// different machines and guarantees nothing across fan-outs, so the lone answer of the first
/// fan-out and a lone answer of the repair round may be one machine's
/// (<see cref="SmtFirstAnswers.TwoMachines"/>). The lone answer is still compared with the repair
/// round's by digest. The answer's every model is checked by the host's evaluator;</item>
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
/// Which packages a rate falls on is the platform's choice too, and the host never learns it: a
/// package is known to have been asked for twice by the results that came back under its ticket
/// (<see cref="Sampled"/>).
/// </para>
/// </summary>
internal static class SmtRulings
{
    #region Constants

    /// <summary>The copies a dispute asks for.</summary>
    public const int DISPUTE_COPIES = 3;

    /// <summary>The copies the first fan-out asks for of a sampled package, and the repair round of every package.</summary>
    public const int SAMPLE_COPIES = 2;

    /// <summary>The fewest copies that must give an answer for it to settle a dispute.</summary>
    public const int MAJORITY = 2;

    /// <summary>
    /// The reason of a repair (the integrity document's <c>repairs_by_reason</c>): the problem's
    /// package was asked for twice and fewer than two answers to it came back from the first fan-out.
    /// </summary>
    public const string COPIES_SHORT = "copies_short";

    #endregion

    #region Functions

    /// <summary>
    /// The tickets of the packages the first fan-out is known to have asked for twice: those under
    /// which two results or more came back, whatever they say (an answer, a node's fault code, a
    /// result the host refused), and every package when the rate asks for every one. A package of
    /// which fewer than two results came back cannot be told from one asked for once.
    /// </summary>
    /// <param name="main">The first fan-out.</param>
    /// <param name="rate">The sampling rate the fan-out ran under, or null when the script does not say.</param>
    /// <returns>The tickets.</returns>
    public static HashSet<string> Sampled(SmtRound main, double? rate)
    {
        var every = rate is { } share && main.Issued.Count > 0 && SmtSampling.Selected(main.Issued.Count, SmtSampling.Clamp(share)) >= main.Issued.Count;

        return main.Issued
            .Where(unit => every || main.Returned.GetValueOrDefault(unit.Ticket) >= SAMPLE_COPIES)
            .Select(unit => unit.Ticket)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Whether a problem goes out once more: it has no answer from the first fan-out, or its
    /// package was asked for twice and fewer than two answers to it came back.
    /// </summary>
    /// <param name="main">The first fan-out.</param>
    /// <param name="index">The problem.</param>
    /// <param name="sampled">Whether the problem's package is known to have been asked for twice (<see cref="Sampled"/>).</param>
    /// <returns>True when the repair round sends it.</returns>
    public static bool NeedsRepair(SmtRound main, int index, bool sampled)
    {
        var answers = main.Answers(index).Count;
        return answers == 0 || (sampled && answers < SAMPLE_COPIES);
    }

    /// <summary>
    /// The answers a problem is first ruled on: the first fan-out's; for a problem the repair round
    /// answered, the first fan-out's with the repair round's after them. A ticket's surplus counts
    /// only for a round that gave the problem an answer.
    /// </summary>
    /// <param name="index">The problem.</param>
    /// <param name="main">The first fan-out.</param>
    /// <param name="repair">The repair round, or null when the script ran none.</param>
    /// <returns>The answers.</returns>
    public static SmtFirstAnswers First(int index, SmtRound main, SmtRound? repair)
    {
        var answers = main.Answers(index);
        var again = repair?.Answers(index) ?? [];
        if (repair == null || again.Count == 0)
            return new SmtFirstAnswers(answers, main, answers.Count >= SAMPLE_COPIES, answers.Count > 0 && IsSurplus(main, index), false);

        return new SmtFirstAnswers([.. answers, .. again], repair,
            answers.Count >= SAMPLE_COPIES || again.Count >= SAMPLE_COPIES,
            (answers.Count > 0 && IsSurplus(main, index)) || IsSurplus(repair, index),
            true);
    }

    /// <summary>Why the answers a problem is first ruled on cannot settle it, or null when they can.</summary>
    /// <param name="first">The answers (<see cref="First"/>).</param>
    /// <param name="check">The evaluator's check of a copy.</param>
    /// <returns>A cause of <see cref="SmtRunWords.DISPUTE_CAUSES"/>, or null.</returns>
    public static string? Cause(SmtFirstAnswers first, Func<SmtCopy, SmtModelCheck> check)
    {
        var answers = first.Answers;
        if (answers.Count == 0)
            return null;

        if (first.Surplus)
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
        var first = First(index, main, repair);
        if (first.Answers.Count == 0)
            return SmtRuling.Without(index, reason());

        var cause = Cause(first, check);
        if (cause == null)
        {
            var answer = first.Answers[0];
            return SmtRuling.Accept(index, answer, check(answer), first.Answers.Count, agreed: true, first.TwoMachines ? SmtRunWords.COPIES : SmtRunWords.ONE_COPY, first.Repaired);
        }

        return Settle(index, cause, first.Answers, dispute, borrowable, check, first.Repaired);
    }

    #endregion

    #region Tools

    private static bool IsSurplus(SmtRound round, int index)
    {
        return round.UnitOf.TryGetValue(index, out var unit) && round.Surplus.Contains(unit.Ticket);
    }

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
        var differing = first.Count(copy => copy.Digest != leader.Digest) + votes.Count(copy => copy.Digest != leader.Digest);
        var leaderCheck = check(leader);

        // A copy is a lie only when it differs from a settled answer the evaluator does not reject.
        // When the machines' answer is a model the evaluator calls failed, nobody is counted as
        // lying, the copies that differ from it least of all: the problem is a disagreement between
        // the evaluator and the solver (settled by agreement), and the integrity document lists it.
        var settledBy = leaderCheck.IsFailed ? SmtRunWords.AGREEMENT : SmtRunWords.DISPUTE;
        var lies = leaderCheck.IsFailed ? 0 : differing;

        return SmtRuling.Accept(index, leader, leaderCheck, copies, differing == 0, settledBy, repaired,
            new SmtDisputeRecord(index, cause, first.Count, votes.Count, most, SmtRunWords.RESOLVED), lies);
    }

    #endregion
}
