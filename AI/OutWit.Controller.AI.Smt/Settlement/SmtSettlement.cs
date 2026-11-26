using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Planning;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// The rounds of a run on the host, between the fan-outs: what came back is taken in under its
/// tickets (<see cref="SmtRoundIntake"/>), the problems without an answer and the problems of a
/// package asked for twice that came back with fewer than two answers go out once more, to two
/// machines, the problems whose copies disagree or whose model the evaluator calls failed go to
/// three more machines (<see cref="SmtRulings"/>). Every round's problems are read from the host's
/// own packages, a package at a time, and stored as packages of their own by the planner's rule.
/// Settling the run and writing its documents is <see cref="SmtRunSettlement"/>. Every function
/// here looks at the job's cancellation before each package and each problem it reads.
/// </summary>
internal static class SmtSettlement
{
    #region Functions

    /// <summary>Takes in the first fan-out.</summary>
    /// <param name="issued">The packages it gave out.</param>
    /// <param name="returned">What Grid.ForEach returned.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="spool">Where whole rows are kept, or null when only the comparison is needed.</param>
    /// <param name="cancellation">The job's cancellation.</param>
    /// <returns>The round as taken.</returns>
    /// <exception cref="InvalidOperationException">A package of problems this host made cannot be read: the run cannot be settled.</exception>
    /// <exception cref="SmtSettlementBoundException">The rows taken pass what the spool holds of one run.</exception>
    /// <exception cref="IOException">The host's own spool could not be written.</exception>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public static Task<SmtRound> ReadMainAsync(IReadOnlyList<SmtTaskPackageData> issued, IEnumerable<SmtResultPackageData?> returned, IWitBlobService blobs, SmtSpool? spool = null,
        CancellationToken cancellation = default)
    {
        return SmtRoundIntake.ReadAsync(SmtRound.MAIN, issued, returned, SmtRulings.SAMPLE_COPIES, surplusIsSuspect: true, blobs, spool, cancellation);
    }

    /// <summary>Takes in the repair round, which asks two machines for every one of its packages (<c>Grid.Replicas(2)</c>).</summary>
    /// <param name="issued">The packages it gave out.</param>
    /// <param name="returned">What Grid.ForEach returned.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="spool">Where whole rows are kept, or null.</param>
    /// <param name="cancellation">The job's cancellation.</param>
    /// <returns>The round as taken.</returns>
    /// <exception cref="InvalidOperationException">A package of problems this host made cannot be read: the run cannot be settled.</exception>
    /// <exception cref="SmtSettlementBoundException">The rows taken pass what the spool holds of one run.</exception>
    /// <exception cref="IOException">The host's own spool could not be written.</exception>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public static Task<SmtRound> ReadRepairAsync(IReadOnlyList<SmtTaskPackageData> issued, IEnumerable<SmtResultPackageData?> returned, IWitBlobService blobs, SmtSpool? spool = null,
        CancellationToken cancellation = default)
    {
        return SmtRoundIntake.ReadAsync(SmtRound.REPAIR, issued, returned, SmtRulings.SAMPLE_COPIES, surplusIsSuspect: true, blobs, spool, cancellation);
    }

    /// <summary>Takes in the dispute round.</summary>
    /// <param name="issued">The packages it gave out.</param>
    /// <param name="returned">What Grid.ForEach returned.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="spool">Where whole rows are kept, or null.</param>
    /// <param name="cancellation">The job's cancellation.</param>
    /// <returns>The round as taken.</returns>
    /// <exception cref="InvalidOperationException">A package of problems this host made cannot be read: the run cannot be settled.</exception>
    /// <exception cref="SmtSettlementBoundException">The rows taken pass what the spool holds of one run.</exception>
    /// <exception cref="IOException">The host's own spool could not be written.</exception>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public static Task<SmtRound> ReadDisputeAsync(IReadOnlyList<SmtTaskPackageData> issued, IEnumerable<SmtResultPackageData?> returned, IWitBlobService blobs, SmtSpool? spool = null,
        CancellationToken cancellation = default)
    {
        return SmtRoundIntake.ReadAsync(SmtRound.DISPUTE, issued, returned, SmtRulings.DISPUTE_COPIES, surplusIsSuspect: false, blobs, spool, cancellation);
    }

    /// <summary>
    /// Stores the problems of the first fan-out that go out once more as packages: those of the
    /// packages no taken result answers (a node's fault code, a refused result, nothing at all),
    /// those every taken copy of which is a fault of its node or a node's refusal, and those of a package the fan-out
    /// is known to have asked for twice that have fewer than two answers (<see cref="SmtRulings.NeedsRepair"/>).
    /// </summary>
    /// <param name="main">The first fan-out.</param>
    /// <param name="rate">The sampling rate the fan-out ran under, or null when the script does not say.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="folder">A folder of the host's own for the package files.</param>
    /// <param name="cancellation">The job's cancellation.</param>
    /// <returns>The references; empty when every problem has the answers it was asked for.</returns>
    /// <exception cref="SmtPackageException">A package of problems this host made cannot be read again: the run cannot go on.</exception>
    /// <exception cref="IOException">The host's own files could not be read or written.</exception>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public static Task<List<SmtTaskPackageData>> PublishRepairsAsync(SmtRound main, double? rate, IWitBlobService blobs, string folder, CancellationToken cancellation = default)
    {
        var sampled = SmtRulings.Sampled(main, rate);
        var wanted = new Dictionary<string, HashSet<int>?>(StringComparer.Ordinal);

        foreach (var unit in main.Unanswered)
            wanted[unit.Ticket] = null;

        foreach (var (index, unit) in main.UnitOf)
        {
            if (SmtRulings.NeedsRepair(main, index, sampled.Contains(unit.Ticket)))
                Want(wanted, unit.Ticket, index);
        }

        return PublishAsync(main.Issued, wanted, blobs, folder, cancellation);
    }

    /// <summary>
    /// Stores the problems that go to three more machines as packages: those whose first answers
    /// (<see cref="SmtRulings.First"/>: the first fan-out's, with the repair round's for a problem
    /// it answered) disagree, came under a ticket with more answers than were asked for, or are one
    /// answer whose model the evaluator calls failed. Every answer of the two rounds that says
    /// <c>sat</c> is checked, each under its package's budget, as <c>Smt.Settle</c> checks it: a
    /// package's budget goes to its rows in their order in both activities, and both see one check
    /// of each answer.
    /// </summary>
    /// <param name="main">The first fan-out, taken in with a spool.</param>
    /// <param name="repair">The repair round, taken in with the same spool, or null.</param>
    /// <param name="spool">The spool both rounds' rows were kept in.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="folder">A folder of the host's own for the package files.</param>
    /// <param name="packageSteps">What the host's evaluator spends on the models of one result package at the most.</param>
    /// <param name="cancellation">The job's cancellation.</param>
    /// <returns>The references, and how many problems went for each cause.</returns>
    /// <exception cref="SmtPackageException">A package of problems this host made cannot be read again: the run cannot go on.</exception>
    /// <exception cref="IOException">The host's own files could not be read or written.</exception>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public static async Task<(List<SmtTaskPackageData> Packages, SortedDictionary<string, int> Causes)> PublishDisputesAsync(SmtRound main, SmtRound? repair, SmtSpool spool,
        IWitBlobService blobs, string folder, long packageSteps = SmtPackageBudget.DEFAULT_STEPS, CancellationToken cancellation = default)
    {
        // Every problem with a copy in either round, once; its first answers are made when they are
        // looked at instead of being held, a few small lists at a time.
        IEnumerable<int> Indices()
        {
            return repair == null ? main.Copies.Keys : main.Copies.Keys.Concat(repair.Copies.Keys.Where(index => !main.Copies.ContainsKey(index)));
        }

        var checks = await CheckAsync(repair == null ? [main] : [main, repair], spool, blobs, packageSteps, cancellation);
        SmtModelCheck Check(SmtCopy copy)
        {
            return copy.ClaimsSat && checks.TryGetValue((copy.Index, copy.Digest), out var walk) ? walk.Check : SmtModelCheck.NONE;
        }

        var wanted = new Dictionary<string, HashSet<int>?>(StringComparer.Ordinal);
        var causes = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var index in Indices())
        {
            var first = SmtRulings.First(index, main, repair);
            if (SmtRulings.Cause(first, Check) is { } cause)
            {
                Want(wanted, first.Round.UnitOf[index].Ticket, index);
                SmtIntegrityFacts.Count(causes, cause);
            }
        }

        // A ticket is of one round, so the packages of the two rounds are told apart by it.
        return (await PublishAsync(repair == null ? main.Issued : [.. main.Issued, .. repair.Issued], wanted, blobs, folder, cancellation), causes);
    }

    /// <summary>
    /// The evaluator's checks of every copy that says <c>sat</c> in some rounds, each against its
    /// problem as the host's own package gives it, sanitized again here, and under the budget of
    /// the result package it came in. A round's packages of problems are read a problem at a time,
    /// in the order they were given out, and only the problems with a copy to check are sanitized:
    /// a package of results spends its budget on its rows in their order (a package of problems
    /// holds its problems in the set's order, as the host cuts every one), as in <c>Smt.Settle</c>.
    /// </summary>
    /// <param name="rounds">The rounds, taken in with the spool.</param>
    /// <param name="spool">The spool the copies' rows were kept in.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="packageSteps">What the host's evaluator spends on the models of one result package at the most.</param>
    /// <param name="cancellation">The job's cancellation, looked at before every package, problem and model.</param>
    /// <returns>By problem and digest, the walk that says the most of the copies of that digest in the rounds.</returns>
    /// <exception cref="SmtPackageException">A package of problems this host made cannot be read again: the run cannot go on.</exception>
    /// <exception cref="IOException">The host's own files could not be read or written.</exception>
    /// <exception cref="OperationCanceledException">The job was cancelled.</exception>
    public static async Task<Dictionary<(int Index, SmtDigestKey Digest), SmtWalk>> CheckAsync(IReadOnlyList<SmtRound> rounds, SmtSpool spool, IWitBlobService blobs,
        long packageSteps, CancellationToken cancellation)
    {
        var walks = new Dictionary<(int, SmtDigestKey), SmtWalk>();

        foreach (var round in rounds)
        {
            // The problems of each package of problems that have a copy saying sat in this round.
            var wanted = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            foreach (var (index, copies) in round.Copies)
            {
                if (!copies.Any(copy => copy.ClaimsSat))
                    continue;

                var ticket = round.UnitOf[index].Ticket;
                if (!wanted.TryGetValue(ticket, out var indices))
                    wanted[ticket] = indices = [];

                indices.Add(index);
            }

            foreach (var unit in round.Issued)
            {
                if (!wanted.TryGetValue(unit.Ticket, out var indices))
                    continue;

                cancellation.ThrowIfCancellationRequested();
                using var reader = new SmtTaskPackageReader(await blobs.GetLocalPathAsync(unit.ProblemsBlobId), unit.ProblemCount);
                while (reader.TryRead(out var problem))
                {
                    if (!indices.Contains(problem.Index))
                        continue;

                    cancellation.ThrowIfCancellationRequested();
                    var copies = round.Of(problem.Index);
                    var checks = new SmtProblemChecks(SmtHostEvaluator.Of(problem), copies, spool, packageSteps, cancellation);
                    foreach (var digest in copies.Where(copy => copy.ClaimsSat).Select(copy => copy.Digest).Distinct())
                        walks[(problem.Index, digest)] = SmtWalk.Better(walks.GetValueOrDefault((problem.Index, digest)), checks.Walk(digest));
                }
            }
        }

        return walks;
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
    private static async Task<List<SmtTaskPackageData>> PublishAsync(IReadOnlyList<SmtTaskPackageData> issued, Dictionary<string, HashSet<int>?> wanted, IWitBlobService blobs, string folder,
        CancellationToken cancellation)
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

            cancellation.ThrowIfCancellationRequested();
            using var reader = new SmtTaskPackageReader(await blobs.GetLocalPathAsync(unit.ProblemsBlobId), unit.ProblemCount);
            while (reader.TryRead(out var problem))
            {
                cancellation.ThrowIfCancellationRequested();
                if (indices == null || indices.Contains(problem.Index))
                    await publisher.AddAsync(problem);
            }
        }

        return await publisher.CompleteAsync();
    }

    #endregion
}
