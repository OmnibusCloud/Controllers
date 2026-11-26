using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Documents;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Model.Problems;
using OutWit.Controller.AI.Smt.Planning;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Settlement;

/// <summary>
/// Settles a run on the host from what its fan-outs returned: the first one, the repair round and
/// the dispute round. Every result package is taken in under its ticket and checked
/// (<see cref="SmtRoundIntake"/>); then the problem set is read once more, a row at a time, and
/// every row is ruled on in the set's order (<see cref="SmtRulings"/>): every answer that says
/// <c>sat</c> is checked by the host's evaluator against the problem as the host's own sanitizer
/// takes it (<see cref="SmtHostEvaluator"/>), and the row of the results is written. The rows are
/// kept in a spool file while the documents are written, and every document goes to a file as it
/// is written: the host's memory holds a record a copy and a few numbers a problem, never a model
/// of more than the problem at hand, and never a document. What the host's disk holds of a run has
/// bounds (<see cref="SmtSettlementBounds"/>): the rows in the spool, and the full models in the
/// ledger; a run past either is not settled, and the failure names the bound.
/// </summary>
internal static class SmtRunSettlement
{
    #region Constants

    private const string RESULTS_EXTENSION = ".results";
    private const string REPORT_EXTENSION = ".report";
    private const string PROVENANCE_EXTENSION = ".provenance";
    private const string INTEGRITY_EXTENSION = ".integrity";
    private const string MODELS_EXTENSION = ".models";
    private const string LEDGER_EXTENSION = ".wsl";
    private const int WRITE_BUFFER_BYTES = 128 * 1024;

    #endregion

    #region Functions

    /// <summary>Settles the run and writes its ledger.</summary>
    /// <param name="problemsPath">The problem set's local file.</param>
    /// <param name="options">The run's options as the job gave them.</param>
    /// <param name="fanOut">The first fan-out: what it gave out and what came back.</param>
    /// <param name="repair">The repair round, or null when the script ran none.</param>
    /// <param name="dispute">The dispute round, or null when the script ran none.</param>
    /// <param name="blobs">The host's blob service.</param>
    /// <param name="workFolder">A folder of the host's own for the working files.</param>
    /// <param name="samplingRate">The share of the packages the script asked to solve twice, or null when it does not say.</param>
    /// <param name="bounds">What the host keeps of the run at the most; null for a host's bounds (<see cref="SmtSettlementBounds.DEFAULT"/>).</param>
    /// <returns>The ledger file (the caller stores and deletes it) and the integrity facts.</returns>
    /// <exception cref="SmtSettlementBoundException">The rows the nodes returned, or the full models of the settled rows, pass what the host keeps of one run.</exception>
    public static async Task<(string LedgerPath, SmtIntegrityFacts Facts)> SettleAsync(string problemsPath, SmtOptionsData options,
        (IReadOnlyList<SmtTaskPackageData> Issued, IEnumerable<SmtResultPackageData?> Returned) fanOut,
        (IReadOnlyList<SmtTaskPackageData> Issued, IEnumerable<SmtResultPackageData?> Returned)? repair,
        (IReadOnlyList<SmtTaskPackageData> Issued, IEnumerable<SmtResultPackageData?> Returned)? dispute,
        IWitBlobService blobs, string workFolder, double? samplingRate, SmtSettlementBounds? bounds = null)
    {
        bounds ??= SmtSettlementBounds.DEFAULT;
        using var spool = new SmtSpool(workFolder, bounds.RowsBytes);

        var rate = samplingRate is { } stated ? SmtSampling.Clamp(stated) : (double?)null;

        var main = await SmtSettlement.ReadMainAsync(fanOut.Issued, fanOut.Returned, blobs, spool);
        var repaired = repair is { } again ? await SmtSettlement.ReadRepairAsync(again.Issued, again.Returned, blobs, spool) : null;
        var settled = dispute is { } thrice ? await SmtSettlement.ReadDisputeAsync(thrice.Issued, thrice.Returned, blobs, spool) : null;

        var borrowable = settled == null ? new Dictionary<string, int>(StringComparer.Ordinal) : SmtRulings.Borrowable(settled);
        var mainTickets = await TicketsOfUnansweredAsync(main, blobs);
        var repairTickets = repaired == null ? new Dictionary<int, string>() : await TicketsOfUnansweredAsync(repaired, blobs);
        var sampled = SmtRulings.Sampled(main, rate);

        var facts = new SmtIntegrityFacts { SamplingRate = rate };
        var report = new SmtReportFacts();
        var provenance = new SmtRunProvenance(SmtOptionsRules.Resolve(options), rate, SmtPackageCodec.HashFile(problemsPath), new FileInfo(problemsPath).Length);
        var rounds = new[] { main, repaired, settled }.OfType<SmtRound>().ToList();

        var stem = Path.Combine(workFolder, $"smt-settle-{Guid.NewGuid():N}");
        var sections = new[] { RESULTS_EXTENSION, REPORT_EXTENSION, PROVENANCE_EXTENSION, INTEGRITY_EXTENSION, MODELS_EXTENSION }.Select(extension => stem + extension).ToList();
        var ledgerPath = stem + LEDGER_EXTENSION;
        try
        {
            SmtProblemSetSummary set;
            using (var results = Create(sections[SmtLedger.RESULTS]))
            using (var models = Create(sections[SmtLedger.MODELS]))
            using (var stream = SmtProblemSetScan.Open(problemsPath))
            {
                var reader = new SmtProblemSetReader(stream, options);
                while (reader.TryRead(out var row))
                {
                    if (!row.IsAccepted)
                    {
                        SmtResultsWriter.WriteRefused(results, row.Index, row.Refusal!);
                        report.CountRefused();
                        continue;
                    }

                    var evaluator = new SmtHostEvaluator(row.Problem, row.Data);
                    var checks = new Dictionary<SmtDigestKey, SmtModelCheck>();
                    SmtModelCheck Check(SmtCopy copy)
                    {
                        if (!copy.ClaimsSat)
                            return SmtModelCheck.NONE;

                        if (!checks.TryGetValue(copy.Digest, out var check))
                            checks[copy.Digest] = check = evaluator.Check(spool.Read(copy));

                        return check;
                    }

                    var index = row.Index;
                    var ruling = SmtRulings.Rule(index, main, repaired, settled, borrowable, Check,
                        () => ReasonIn(repaired ?? main, index, repaired == null ? mainTickets : repairTickets));

                    Count(facts, index, ruling, rounds, Check);
                    CountRepair(facts, index, main, repaired, mainTickets, repairTickets, sampled);

                    var accepted = ruling.Accepted == null ? null : spool.Read(ruling.Accepted);
                    SmtResultsWriter.WriteTaken(results, models, row.Data, row.Problem, ruling, accepted);
                    report.CountTaken(row.Data, row.Problem, ruling, accepted);

                    // Looked at after each row: the section passes its bound by one row's models at the most.
                    if (models.Length > bounds.ModelsBytes)
                        throw new SmtSettlementBoundException(SmtSettlementBoundException.MODELS, bounds.ModelsBytes, "the full models of the settled rows");
                }

                set = reader.Summary;
            }

            facts.CompleteOneCopy = report.CompleteOneCopy.Count;

            CountRounds(facts, main, repaired, settled, rate, sampled.Count);

            using (var stream = Create(sections[SmtLedger.REPORT]))
                SmtReportWriter.Write(stream, provenance, set, report, facts.Sampling, facts.UnansweredPackages, facts.Binding);

            using (var stream = Create(sections[SmtLedger.PROVENANCE]))
                SmtProvenanceWriter.Write(stream, provenance, set.Rows);

            using (var stream = Create(sections[SmtLedger.INTEGRITY]))
                SmtIntegrityWriter.Write(stream, facts);

            SmtLedger.Write(ledgerPath, sections);
            return (ledgerPath, facts);
        }
        catch
        {
            SmtSettlement.TryDelete(ledgerPath);
            throw;
        }
        finally
        {
            foreach (var section in sections)
                SmtSettlement.TryDelete(section);
        }
    }

    #endregion

    #region Tools

    private static FileStream Create(string path)
    {
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, WRITE_BUFFER_BYTES);
    }

    /// <summary>
    /// The package of every problem of a round whose package no taken result answers: read from
    /// the host's own packages, the indices only.
    /// </summary>
    private static async Task<Dictionary<int, string>> TicketsOfUnansweredAsync(SmtRound round, IWitBlobService blobs)
    {
        var tickets = new Dictionary<int, string>();
        foreach (var unit in round.Unanswered)
        {
            foreach (var index in (await SmtTaskBounds.LoadAsync(unit, blobs)).Indices)
                tickets[index] = unit.Ticket;
        }

        return tickets;
    }

    /// <summary>
    /// What a round said of a problem it gave no answer for: when it took copies that are no
    /// answers, <see cref="SmtRunWords.NODE_REFUSED"/> for a node's refusal and the code of its
    /// node's fault for a fault (the copy first in the order of their digests); else what became of
    /// its package (a node's fault code, the reason a result was refused, or no answer at all).
    /// </summary>
    private static string ReasonIn(SmtRound round, int index, IReadOnlyDictionary<int, string> unanswered)
    {
        var faults = round.Of(index);
        if (faults.Count > 0)
            return faults[0].IsRefusal ? SmtRunWords.NODE_REFUSED : faults[0].Code.Length > 0 ? faults[0].Code : SmtResultWords.NODE_FAULT;

        if (round.UnitOf.TryGetValue(index, out var unit))
            return round.FateOf(unit.Ticket) is { Length: > 0 } fate ? fate : SmtRunWords.NO_ANSWER;

        return unanswered.TryGetValue(index, out var ticket) ? round.FateOf(ticket) is { Length: > 0 } fateOf ? fateOf : SmtRunWords.NO_ANSWER : SmtRunWords.NO_ANSWER;
    }

    private static void Count(SmtIntegrityFacts facts, int index, SmtRuling ruling, IReadOnlyList<SmtRound> rounds, Func<SmtCopy, SmtModelCheck> check)
    {
        facts.Problems++;

        foreach (var round in rounds)
        {
            foreach (var answer in round.Answers(index))
            {
                facts.CopiesMade++;
                if (check(answer).IsFailed)
                    facts.FailedModels++;
            }
        }

        if (ruling.Dispute != null)
            facts.Disputes.Add(ruling.Dispute);

        if (ruling.Lies > 0)
        {
            facts.Lies += ruling.Lies;
            facts.ProblemsWithLies++;
        }

        if (ruling.HostStatus == SmtRunWords.NO_RESULT)
        {
            facts.NoResult++;
            SmtIntegrityFacts.Count(facts.NoResultReasons, ruling.Reason);
        }

        if (ruling.SettledBy == SmtRunWords.ONE_COPY)
        {
            facts.OneCopy++;
            if (ruling.Accepted?.Verdict == SmtResultWords.UNSAT)
                facts.UnsatOneCopy++;
        }

        if (ruling.SettledBy == SmtRunWords.AGREEMENT)
            facts.EvaluatorDisagreements.Add((index, ruling.Check.Reason, ruling.Check.Detail));
    }

    /// <summary>
    /// What the first and the repair round did for one problem: whether the repair round sent it
    /// (its packages say so, whatever came back) and why, how its first answers compared, and
    /// whether a problem of a package asked for twice ended without two answers of one fan-out.
    /// </summary>
    private static void CountRepair(SmtIntegrityFacts facts, int index, SmtRound main, SmtRound? repair,
        IReadOnlyDictionary<int, string> mainTickets, IReadOnlyDictionary<int, string> repairTickets, IReadOnlySet<string> sampled)
    {
        var answers = main.Answers(index).Count;
        var again = repair?.Answers(index).Count ?? 0;

        if (repair != null && (repair.UnitOf.ContainsKey(index) || repairTickets.ContainsKey(index)))
        {
            facts.Repairs++;
            SmtIntegrityFacts.Count(facts.RepairReasons, answers == 0 ? ReasonIn(main, index, mainTickets) : SmtRulings.COPIES_SHORT);
        }

        var first = SmtRulings.First(index, main, repair).Answers;
        if (first.Count >= SmtRulings.SAMPLE_COPIES)
        {
            facts.Compared++;
            if (first.All(answer => answer.Digest == first[0].Digest))
                facts.Agreements++;
            else
                facts.Disagreements++;
        }

        // A problem without any answer is no answer the sampling failed to check: it is counted
        // as a problem without a result.
        var ticket = main.UnitOf.TryGetValue(index, out var unit) ? unit.Ticket : mainTickets.GetValueOrDefault(index);
        if (ticket != null && sampled.Contains(ticket) && answers + again > 0 && answers < SmtRulings.SAMPLE_COPIES && again < SmtRulings.SAMPLE_COPIES)
            facts.SampledShort++;
    }

    private static void CountRounds(SmtIntegrityFacts facts, SmtRound main, SmtRound? repair, SmtRound? dispute, double? rate, int sampled)
    {
        foreach (var round in new[] { main, repair, dispute }.OfType<SmtRound>())
        {
            foreach (var (reason, count) in round.Rejected)
                facts.Rejected[reason] = facts.Rejected.GetValueOrDefault(reason) + count;

            foreach (var (code, count) in round.Faults)
                facts.Faults[code] = facts.Faults.GetValueOrDefault(code) + count;

            facts.BoundResults += round.BoundResults;
            facts.UnboundResults += round.UnboundResults;
        }

        // What each fan-out was asked for and what came back, in packages. The repair round asks
        // two machines for every one of its packages.
        int? asked = rate is { } stated ? SmtSampling.Selected(main.Issued.Count, stated) : null;
        facts.FanOuts.Add(FanOut(main, SmtRulings.SAMPLE_COPIES, asked));

        if (repair != null)
            facts.FanOuts.Add(FanOut(repair, SmtRulings.SAMPLE_COPIES, repair.Issued.Count));

        if (dispute != null)
            facts.FanOuts.Add(FanOut(dispute, SmtRulings.DISPUTE_COPIES, dispute.Issued.Count));

        facts.Sampling = SmtSampling.State(rate, asked ?? 0, sampled, facts.SampledShort);
    }

    /// <summary>One fan-out in numbers: the packages that came back with the answers asked of them, and those without any.</summary>
    private static SmtFanOutRecord FanOut(SmtRound round, int copies, int? selected)
    {
        var answers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var package in round.Accepted)
            answers[package.Unit.Ticket] = answers.GetValueOrDefault(package.Unit.Ticket) + 1;

        return new SmtFanOutRecord(round.Name, round.Issued.Count, round.Issued.Sum(unit => (long)unit.ProblemCount), copies, selected,
            answers.Values.Count(count => count >= copies), round.Issued.Count(unit => !answers.ContainsKey(unit.Ticket)));
    }

    #endregion
}
