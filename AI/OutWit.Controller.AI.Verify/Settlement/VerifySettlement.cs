using System.Text.Json;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Settlement;

/// <summary>
/// Settles a run on the server from what its fan-outs returned: the first one, the repair round
/// (tasks without a verdict, sent out once more) and the dispute round (tasks whose executions
/// disagreed, in three copies). Every result package is taken in under its ticket and checked
/// (<see cref="VerifyRoundIntake"/>), every task is ruled on (<see cref="VerifyRulings"/>), and
/// the four documents are written into one ledger (<see cref="VerifyLedger"/>). The results are
/// read once, a batch at a time, and kept in a spool file while the documents are written, and
/// every document goes to a file as it is written: the server's memory holds a record per
/// execution, never the outputs and never a document.
/// </summary>
internal static class VerifySettlement
{
    #region Constants

    private const string OUTPUTS_EXTENSION = ".outputs";
    private const string REPORT_EXTENSION = ".report";
    private const string PROVENANCE_EXTENSION = ".provenance";
    private const string INTEGRITY_EXTENSION = ".integrity";
    private const string LEDGER_EXTENSION = ".wvl";
    private const int WRITE_BUFFER_BYTES = 128 * 1024;

    #endregion

    #region Functions

    /// <summary>Takes in the first fan-out.</summary>
    /// <param name="issued">The task packages it gave out.</param>
    /// <param name="returned">What Grid.ForEach returned.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="spool">Where whole results are kept, or null when only the comparison is needed.</param>
    /// <returns>The round as accepted.</returns>
    public static Task<VerifyRound> ReadMainAsync(IReadOnlyList<VerifyTaskPackageData> issued, IEnumerable<VerifyResultPackageData> returned, IWitBlobService blobs, VerifySpool? spool = null)
    {
        return VerifyRoundIntake.ReadAsync(VerifyRound.MAIN, issued, returned, VerifyRulings.SAMPLE_COPIES, surplusIsSuspect: true, blobs, spool);
    }

    /// <summary>
    /// Stores the tasks of the first fan-out that have no verdict as packages to send out once
    /// more: those of the packages no accepted result answers, and those every accepted
    /// execution reported as a fault of its node.
    /// </summary>
    /// <param name="main">The first fan-out.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <returns>The references; empty when every task has a verdict.</returns>
    public static Task<List<VerifyTaskPackageData>> PublishRepairsAsync(VerifyRound main, IWitBlobService blobs)
    {
        var wanted = new Dictionary<string, HashSet<int>?>(StringComparer.Ordinal);

        foreach (var unit in main.Unanswered)
            wanted[unit.Ticket] = null;

        foreach (var (index, unit) in main.UnitOf)
        {
            if (VerifyRulings.NeedsRepair(main, index))
                Want(wanted, unit.Ticket, index);
        }

        return PublishAsync(main.Issued, wanted, blobs);
    }

    /// <summary>
    /// Stores the tasks of the first fan-out whose executions disagree as packages to run three
    /// more times.
    /// </summary>
    /// <param name="main">The first fan-out.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <returns>The references; empty when nothing is disputed.</returns>
    public static Task<List<VerifyTaskPackageData>> PublishDisputesAsync(VerifyRound main, IWitBlobService blobs)
    {
        var wanted = new Dictionary<string, HashSet<int>?>(StringComparer.Ordinal);

        foreach (var (index, unit) in main.UnitOf)
        {
            if (VerifyRulings.IsDisputed(main, index))
                Want(wanted, unit.Ticket, index);
        }

        return PublishAsync(main.Issued, wanted, blobs);
    }

    /// <summary>
    /// Settles the run and writes its ledger.
    /// </summary>
    /// <param name="fanOut">The first fan-out: what it gave out and what came back.</param>
    /// <param name="repair">The repair round, or null when the script ran none.</param>
    /// <param name="dispute">The dispute round, or null when the script ran none.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="workFolder">A folder of the server's own for the working files.</param>
    /// <returns>The ledger file (the caller stores and deletes it) and the run's counts.</returns>
    public static async Task<(string LedgerPath, VerifySettlementSummary Summary)> SettleAsync(
        (IReadOnlyList<VerifyTaskPackageData> Issued, IEnumerable<VerifyResultPackageData> Returned) fanOut,
        (IReadOnlyList<VerifyTaskPackageData> Issued, IEnumerable<VerifyResultPackageData> Returned)? repair,
        (IReadOnlyList<VerifyTaskPackageData> Issued, IEnumerable<VerifyResultPackageData> Returned)? dispute,
        IWitBlobService blobs, string workFolder)
    {
        using var spool = new VerifySpool(workFolder);

        var main = await ReadMainAsync(fanOut.Issued, fanOut.Returned, blobs, spool);
        var repaired = repair is { } again
            ? await VerifyRoundIntake.ReadAsync(VerifyRound.REPAIR, again.Issued, again.Returned, 1, surplusIsSuspect: false, blobs, spool)
            : null;
        var settled = dispute is { } thrice
            ? await VerifyRoundIntake.ReadAsync(VerifyRound.DISPUTE, thrice.Issued, thrice.Returned, VerifyRulings.DISPUTE_COPIES, surplusIsSuspect: false, blobs, spool)
            : null;

        // Every task of the run: those an accepted package holds, and those of the packages
        // nothing answered (read from the server's own task blobs, a package at a time).
        var indices = new SortedSet<int>(main.UnitOf.Keys);
        foreach (var unit in main.Unanswered)
        {
            foreach (var task in (await VerifyTaskPackages.LoadAsync(unit, blobs)).SelectMany(batch => batch.Tasks))
                indices.Add(task.TaskIndex);
        }

        var borrowable = settled == null ? new Dictionary<string, int>(StringComparer.Ordinal) : VerifyRulings.Borrowable(settled);
        var rulings = indices.Select(index => VerifyRulings.Rule(index, main, repaired, settled, borrowable)).ToList();
        var rounds = new[] { main, repaired, settled }.OfType<VerifyRound>().ToList();

        var summary = Summarize(rulings, rounds);
        var facts = Facts(indices, rulings, main, rounds);

        var stem = Path.Combine(workFolder, $"verify-settle-{Guid.NewGuid():N}");
        var outputsPath = stem + OUTPUTS_EXTENSION;
        var reportPath = stem + REPORT_EXTENSION;
        var provenancePath = stem + PROVENANCE_EXTENSION;
        var integrityPath = stem + INTEGRITY_EXTENSION;
        var ledgerPath = stem + LEDGER_EXTENSION;
        try
        {
            using (var report = Create(reportPath))
            using (var outputs = Create(outputsPath))
            using (var writer = new Utf8JsonWriter(report, new JsonWriterOptions { Indented = false }))
            {
                VerifyReportWriter.WriteHead(writer, summary);

                // One task at a time: its accepted result comes out of the spool, goes into
                // both documents, and is let go.
                foreach (var ruling in rulings)
                {
                    var task = new VerifySettledTask(ruling.Index, ruling.Verdict, ruling.ExitCode, ruling.Reason,
                        ruling.Accepted == null ? null : spool.Read(ruling.Accepted));

                    VerifyReportWriter.WriteTask(writer, task);
                    VerifyCorpusWriter.WriteLine(outputs, task);

                    // What the writer holds goes to the file as it fills, not at the end.
                    if (writer.BytesPending >= WRITE_BUFFER_BYTES)
                        writer.Flush();
                }

                VerifyReportWriter.WriteTail(writer);
            }

            using (var provenance = Create(provenancePath))
                VerifyProvenanceWriter.Write(provenance, rounds);

            using (var integrity = Create(integrityPath))
                VerifyIntegrityWriter.Write(integrity, facts);

            VerifyLedger.Write(ledgerPath, reportPath, provenancePath, integrityPath, outputsPath);
            return (ledgerPath, summary);
        }
        catch
        {
            TryDelete(ledgerPath);
            throw;
        }
        finally
        {
            TryDelete(outputsPath);
            TryDelete(reportPath);
            TryDelete(provenancePath);
            TryDelete(integrityPath);
        }
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
            // a working file of the server's own
        }
    }

    private static FileStream Create(string path)
    {
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, WRITE_BUFFER_BYTES);
    }

    private static VerifySettlementSummary Summarize(IReadOnlyList<VerifyRuling> rulings, IReadOnlyList<VerifyRound> rounds)
    {
        var summary = new VerifySettlementSummary();
        foreach (var ruling in rulings)
            summary.Count(ruling.Verdict);

        summary.RejectedResults = rounds.Sum(round => round.RejectedTotal);
        summary.Disputes = rulings.Count(ruling => ruling.Disputed != null);
        summary.DisputesResolved = rulings.Count(ruling => ruling.Disputed is { Resolved: true });
        return summary;
    }

    private static VerifyIntegrityFacts Facts(IReadOnlyCollection<int> indices, IReadOnlyList<VerifyRuling> rulings, VerifyRound main, IReadOnlyList<VerifyRound> rounds)
    {
        var facts = new VerifyIntegrityFacts { Tasks = indices.Count };

        foreach (var index in indices)
        {
            var verdicts = main.Verdicts(index);
            for (var copy = 1; copy < verdicts.Count; copy++)
                facts.Pairs.Add(new VerifyPairRecord(index, verdicts[0], verdicts[copy], VerifyComparable.Differences(verdicts[0], verdicts[copy])));

            if (VerifyRulings.NeedsRepair(main, index))
                facts.Repairs++;
        }

        foreach (var ruling in rulings)
        {
            if (ruling.Disputed != null)
                facts.Disputes.Add(ruling.Disputed);

            if (ruling.Verdict == VerifyVerdict.NoResult)
                facts.NoResult++;
            else if (ruling.Accepted != null && ruling.Copies >= VerifyRulings.MAJORITY)
                facts.Agreed++;
            else if (ruling.Accepted != null)
                facts.SingleExecution++;
        }

        foreach (var round in rounds)
        {
            foreach (var (reason, count) in round.Rejected)
                facts.Rejected[reason] = facts.Rejected.GetValueOrDefault(reason) + count;
        }

        return facts;
    }

    private static void Want(Dictionary<string, HashSet<int>?> wanted, string ticket, int index)
    {
        if (!wanted.TryGetValue(ticket, out var indices))
            wanted[ticket] = indices = [];

        indices?.Add(index);
    }

    /// <summary>
    /// Stores the wanted tasks of the wanted packages, in the batches they came in, as packages
    /// of their own: read from the server's own task blobs a package at a time, in the order the
    /// packages were given out, and stored as each new package fills. The tasks of a round are
    /// never together in memory, even when the round is the whole run (a fleet that answered
    /// nothing).
    /// </summary>
    private static async Task<List<VerifyTaskPackageData>> PublishAsync(IReadOnlyList<VerifyTaskPackageData> issued, Dictionary<string, HashSet<int>?> wanted, IWitBlobService blobs)
    {
        // How many tasks go out decides how the packages are cut; the references say it.
        long total = 0;
        foreach (var unit in issued)
        {
            if (wanted.TryGetValue(unit.Ticket, out var indices))
                total += indices?.Count ?? unit.TaskCount;
        }

        if (total == 0)
            return [];

        var publisher = new VerifyPackagePublisher(total, blobs);

        foreach (var unit in issued)
        {
            if (!wanted.TryGetValue(unit.Ticket, out var indices))
                continue;

            foreach (var batch in await VerifyTaskPackages.LoadAsync(unit, blobs))
            {
                var tasks = batch.Tasks.Where(task => indices == null || indices.Contains(task.TaskIndex)).ToList();
                if (tasks.Count == 0)
                    continue;

                await publisher.AddAsync(new VerifyTaskBatchData
                {
                    RuntimeId = batch.RuntimeId,
                    DefaultLimits = batch.DefaultLimits,
                    MaxDegree = batch.MaxDegree,
                    RuntimeSha256 = batch.RuntimeSha256,
                    Tasks = tasks
                });
            }
        }

        return await publisher.CompleteAsync();
    }

    #endregion
}
