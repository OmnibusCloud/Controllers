using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Eval;
using OutWit.Controller.AI.Smt.Planning;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The rounds of a run on the host, between the fan-outs: what came back is taken in under its
/// tickets (<see cref="SmtRoundIntake"/>), the problems without an answer go out once more, the
/// problems whose copies disagree or whose model the evaluator calls failed go to three more
/// machines (<see cref="SmtRulings"/>). Every round's problems are read from the host's own
/// packages, a package at a time, and stored as packages of their own by the planner's rule.
/// Settling the run and writing its documents is <see cref="SmtRunSettlement"/>.
/// </summary>
internal static class SmtSettlement
{
    #region Functions

    /// <summary>Takes in the first fan-out.</summary>
    /// <param name="issued">The packages it gave out.</param>
    /// <param name="returned">What Grid.ForEach returned.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="spool">Where whole rows are kept, or null when only the comparison is needed.</param>
    /// <returns>The round as taken.</returns>
    public static Task<SmtRound> ReadMainAsync(IReadOnlyList<SmtTaskPackageData> issued, IEnumerable<SmtResultPackageData?> returned, IWitBlobService blobs, SmtSpool? spool = null)
    {
        return SmtRoundIntake.ReadAsync(SmtRound.MAIN, issued, returned, SmtRulings.SAMPLE_COPIES, surplusIsSuspect: true, blobs, spool);
    }

    /// <summary>Takes in the repair round.</summary>
    /// <param name="issued">The packages it gave out.</param>
    /// <param name="returned">What Grid.ForEach returned.</param>
    /// <param name="sampled">True when the round ran under the sampling of the first fan-out (a package may have two answers); false when every package was asked for once.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="spool">Where whole rows are kept, or null.</param>
    /// <returns>The round as taken.</returns>
    public static Task<SmtRound> ReadRepairAsync(IReadOnlyList<SmtTaskPackageData> issued, IEnumerable<SmtResultPackageData?> returned, bool sampled, IWitBlobService blobs, SmtSpool? spool = null)
    {
        return sampled
            ? SmtRoundIntake.ReadAsync(SmtRound.REPAIR, issued, returned, SmtRulings.SAMPLE_COPIES, surplusIsSuspect: true, blobs, spool)
            : SmtRoundIntake.ReadAsync(SmtRound.REPAIR, issued, returned, 1, surplusIsSuspect: false, blobs, spool);
    }

    /// <summary>Takes in the dispute round.</summary>
    /// <param name="issued">The packages it gave out.</param>
    /// <param name="returned">What Grid.ForEach returned.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="spool">Where whole rows are kept, or null.</param>
    /// <returns>The round as taken.</returns>
    public static Task<SmtRound> ReadDisputeAsync(IReadOnlyList<SmtTaskPackageData> issued, IEnumerable<SmtResultPackageData?> returned, IWitBlobService blobs, SmtSpool? spool = null)
    {
        return SmtRoundIntake.ReadAsync(SmtRound.DISPUTE, issued, returned, SmtRulings.DISPUTE_COPIES, surplusIsSuspect: false, blobs, spool);
    }

    /// <summary>
    /// Stores the problems of the first fan-out that have no answer as packages to send out once
    /// more: those of the packages no taken result answers (a node's fault code, a refused result,
    /// nothing at all), and those every taken copy of which is a fault of its node.
    /// </summary>
    /// <param name="main">The first fan-out.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="folder">A folder of the host's own for the package files.</param>
    /// <returns>The references; empty when every problem has an answer.</returns>
    public static Task<List<SmtTaskPackageData>> PublishRepairsAsync(SmtRound main, IWitBlobService blobs, string folder)
    {
        var wanted = new Dictionary<string, HashSet<int>?>(StringComparer.Ordinal);

        foreach (var unit in main.Unanswered)
            wanted[unit.Ticket] = null;

        foreach (var (index, unit) in main.UnitOf)
        {
            if (SmtRulings.NeedsRepair(main, index))
                Want(wanted, unit.Ticket, index);
        }

        return PublishAsync(main.Issued, wanted, blobs, folder);
    }

    /// <summary>
    /// Stores the problems that go to three more machines as packages: those of the first fan-out
    /// whose answers disagree, whose package had more answers than were asked for, or whose
    /// answer's model the evaluator calls failed; and the same of the repair round for the
    /// problems the first fan-out left without an answer.
    /// </summary>
    /// <param name="main">The first fan-out, taken in with a spool.</param>
    /// <param name="repair">The repair round, taken in as a sampled one with the same spool, or null.</param>
    /// <param name="spool">The spool both rounds' rows were kept in.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="folder">A folder of the host's own for the package files.</param>
    /// <returns>The references, and how many problems went for each cause.</returns>
    public static async Task<(List<SmtTaskPackageData> Packages, SortedDictionary<string, int> Causes)> PublishDisputesAsync(SmtRound main, SmtRound? repair, SmtSpool spool,
        IWitBlobService blobs, string folder)
    {
        // The answers a model check decides: those of one digest whose row says sat.
        var leaders = new List<(SmtRound Round, SmtCopy Copy)>();
        foreach (var index in main.Copies.Keys)
            AddLeader(leaders, main, index);

        if (repair != null)
        {
            foreach (var index in repair.Copies.Keys.Where(index => SmtRulings.NeedsRepair(main, index)))
                AddLeader(leaders, repair, index);
        }

        var checks = await CheckAsync(leaders, spool, blobs);
        SmtModelCheck Check(SmtCopy copy)
        {
            return checks.GetValueOrDefault((copy.Index, copy.Digest)) ?? SmtModelCheck.NONE;
        }

        var wanted = new Dictionary<string, HashSet<int>?>(StringComparer.Ordinal);
        var causes = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var (index, unit) in main.UnitOf)
        {
            if (SmtRulings.Cause(main, index, Check) is { } cause)
            {
                Want(wanted, unit.Ticket, index);
                SmtIntegrityFacts.Count(causes, cause);
            }
        }

        if (repair == null)
            return (await PublishAsync(main.Issued, wanted, blobs, folder), causes);

        foreach (var (index, unit) in repair.UnitOf)
        {
            if (SmtRulings.NeedsRepair(main, index) && SmtRulings.Cause(repair, index, Check) is { } cause)
            {
                Want(wanted, unit.Ticket, index);
                SmtIntegrityFacts.Count(causes, cause);
            }
        }

        // A ticket is of one round, so the packages of the two rounds are told apart by it.
        return (await PublishAsync([.. main.Issued, .. repair.Issued], wanted, blobs, folder), causes);
    }

    /// <summary>
    /// The evaluator's checks of copies, each against its problem as the host's own package gives
    /// it, sanitized again here: the packages are read a problem at a time, and only the problems
    /// with a copy to check are sanitized.
    /// </summary>
    /// <param name="copies">The copies, each with the round it was taken in (whose package of it is read).</param>
    /// <param name="spool">The spool the copies' rows were kept in.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <returns>The checks, by problem and digest.</returns>
    public static async Task<Dictionary<(int Index, SmtDigestKey Digest), SmtModelCheck>> CheckAsync(IEnumerable<(SmtRound Round, SmtCopy Copy)> copies, SmtSpool spool, IWitBlobService blobs)
    {
        var checks = new Dictionary<(int, SmtDigestKey), SmtModelCheck>();

        foreach (var package in copies.GroupBy(pair => pair.Round.UnitOf[pair.Copy.Index].Ticket, StringComparer.Ordinal))
        {
            var unit = package.First().Round.UnitOf[package.First().Copy.Index];
            var wanted = package.GroupBy(pair => pair.Copy.Index).ToDictionary(group => group.Key, group => group.Select(pair => pair.Copy).ToList());

            using var reader = new SmtTaskPackageReader(await blobs.GetLocalPathAsync(unit.ProblemsBlobId), unit.ProblemCount);
            while (reader.TryRead(out var problem))
            {
                if (!wanted.TryGetValue(problem!.Index, out var rows))
                    continue;

                var evaluator = SmtHostEvaluator.Of(problem);
                foreach (var copy in rows)
                {
                    if (!checks.ContainsKey((copy.Index, copy.Digest)))
                        checks[(copy.Index, copy.Digest)] = evaluator?.Check(spool.Read(copy)) ?? new SmtModelCheck(SmtRunWords.PARTIAL, SmtValidationReasons.ILL_FORMED_COMMAND, "");
                }
            }
        }

        return checks;
    }

    /// <summary>Deletes a working file; best-effort.</summary>
    /// <param name="path">The file.</param>
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // a working file of the host's own
        }
    }

    #endregion

    #region Tools

    private static void AddLeader(List<(SmtRound Round, SmtCopy Copy)> leaders, SmtRound round, int index)
    {
        var answers = round.Answers(index);
        if (answers.Count > 0 && answers[0].ClaimsSat && answers.All(answer => answer.Digest == answers[0].Digest))
            leaders.Add((round, answers[0]));
    }

    private static void Want(Dictionary<string, HashSet<int>?> wanted, string ticket, int index)
    {
        if (!wanted.TryGetValue(ticket, out var indices))
            wanted[ticket] = indices = [];

        indices?.Add(index);
    }

    /// <summary>
    /// Stores the wanted problems of the wanted packages as packages of their own: read from the
    /// host's own package blobs a package at a time, in the order the packages were given out, and
    /// stored as each new package fills. The problems of a round are never together in memory,
    /// even when the round is the whole run (a fleet that answered nothing).
    /// </summary>
    private static async Task<List<SmtTaskPackageData>> PublishAsync(IReadOnlyList<SmtTaskPackageData> issued, Dictionary<string, HashSet<int>?> wanted, IWitBlobService blobs, string folder)
    {
        long total = 0;
        SmtOptionsData? options = null;
        foreach (var unit in issued)
        {
            if (!wanted.TryGetValue(unit.Ticket, out var indices))
                continue;

            total += indices?.Count ?? unit.ProblemCount;
            options ??= unit.Options;
        }

        if (total == 0 || options == null)
            return [];

        var publisher = new SmtPackagePublisher(total, options, blobs, folder);

        foreach (var unit in issued)
        {
            if (!wanted.TryGetValue(unit.Ticket, out var indices))
                continue;

            using var reader = new SmtTaskPackageReader(await blobs.GetLocalPathAsync(unit.ProblemsBlobId), unit.ProblemCount);
            while (reader.TryRead(out var problem))
            {
                if (indices == null || indices.Contains(problem!.Index))
                    await publisher.AddAsync(problem!);
            }
        }

        return await publisher.CompleteAsync();
    }

    #endregion
}
