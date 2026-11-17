using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Signatures;

namespace OutWit.Controller.AI.Corpus.Trust;

/// <summary>
/// The server's own account of a run: which result stands for which shard, decided from what the
/// server knows and never from what a node says about itself.
/// <list type="bullet">
/// <item><b>A result is bound to its task.</b> Every task of every round carries a ticket the
/// server drew at random; a value that comes back is taken only under a ticket of that round, and
/// only when it names that ticket's shard (index and URL), carries the server's controller version
/// and fits the task's pipeline. Anything else is dropped and counted; a node knows the tickets of
/// its own tasks only, so it cannot answer for a shard it was not given.</item>
/// <item><b>No execution is chosen by its own words.</b> A shard's copies are compared on
/// everything an honest node cannot vary (<see cref="Differences"/>). Copies that agree are
/// accepted. Copies that disagree, or more values than the round asked for, are a dispute: the
/// shard runs again on three machines and the result returned by the most of them, two at least
/// and without a tie, is accepted; otherwise the shard is disputed and has no product.</item>
/// <item><b>A node's own trouble is no verdict.</b> A shard returned as a node fault, or whose
/// results were all dropped, is sent out once more; if that brings nothing — or more than the one
/// value that was asked for, of which none is then taken — the shard has no result, with a fixed
/// reason.</item>
/// </list>
/// The same ledger answers at every step of the script: which shards the repair round takes,
/// which the dispute round, and what the report and the integrity document say.
/// <para>
/// <b>Two machines, not two values.</b> The server sees values, not machines: the platform keeps
/// the copies of a shard on different machines and hands over what they returned without saying
/// which task each value answered. A machine that holds two tasks of a round can spend the slot of
/// one on the other's ticket, which leaves the one short. So in the dispute round, where every
/// shard is asked for in three copies, a value counts towards "two machines" only as far as it
/// cannot be such a second answer: of a shard's leading result, as many values as the other
/// shards of the round are short of may be one machine's second, and do not count. With one lying
/// machine among the copies, whatever the others lose, the result that is accepted was returned
/// by an honest machine; and three machines that agree still settle their shard when another
/// shard of the round lost a copy. Counting values as machines rests on the platform: Grid 1.3.0
/// and later keep, of what a node's batch returns, one value at most for each return variable its
/// tasks asked for. With an older Grid a node could return several values for one task and this
/// ruling would count them as machines.
/// </para>
/// <para>
/// What is left: a shard that was not sampled rests on the machine that ran it, and a machine
/// that holds a sampled shard's copy and a shard of its own can spend the copy's slot on its own
/// shard, which then shows two agreeing executions that are one machine's. No product changes by
/// it (that shard was one machine's word either way); the count of executions overstates how it
/// was checked.
/// </para>
/// </summary>
public sealed class CorpusLedger
{
    #region Constants

    /// <summary>The most results a shard has in the first round: itself and one copy.</summary>
    public const int FIRST_ROUND_COPIES = 2;

    /// <summary>The machines a disputed shard is run on.</summary>
    public const int DISPUTE_COPIES = 3;

    /// <summary>The fewest machines that must return the same result for it to settle a dispute.</summary>
    public const int DISPUTE_QUORUM = 2;

    /// <summary>The values the repair round asks for of a shard: one.</summary>
    public const int REPAIR_COPIES = 1;

    /// <summary>A result under no ticket of the round it came back in: another job's, another round's, another shard's the node was not given, or none.</summary>
    public const string REJECTED_UNKNOWN_TICKET = "unknown_ticket";

    /// <summary>A value that is no result, or a result not shaped as an honest node shapes one (<see cref="CorpusResultCheck"/>).</summary>
    public const string REJECTED_UNREADABLE = "unreadable";

    /// <summary>A result that claims another controller version than the server's.</summary>
    public const string REJECTED_VERSION = "version";

    /// <summary>
    /// A result that does not hold what its task asked for: its ticket is one shard's and its
    /// index or URL another's, or it is at odds with the task's pipeline (another output mode,
    /// other MinHash parameters, a product where none is kept, more bytes than the cap, languages
    /// the pipeline cannot label).
    /// </summary>
    public const string REJECTED_CONTENT = "content";

    /// <summary>More values under one ticket than the round asked for, in a round that takes none of them then (the repair round, the dispute round): which of them is the answer cannot be told.</summary>
    public const string REJECTED_SURPLUS = "surplus";

    /// <summary>Every reason a value is dropped for, in the order the documents list them.</summary>
    public static readonly IReadOnlyList<string> REJECTION_REASONS =
    [
        REJECTED_UNKNOWN_TICKET, REJECTED_UNREADABLE, REJECTED_VERSION, REJECTED_CONTENT, REJECTED_SURPLUS
    ];

    /// <summary>No result: a machine that held the shard returned it as a fault of its own.</summary>
    public const string NO_RESULT_NODE_FAULT = "node_fault";

    /// <summary>No result: nothing that was accepted came back for the shard (nothing at all, or only values that were dropped).</summary>
    public const string NO_RESULT_NO_ANSWER = "no_answer";

    /// <summary>Every reason a shard has no result for.</summary>
    public static readonly IReadOnlyList<string> NO_RESULT_REASONS = [NO_RESULT_NODE_FAULT, NO_RESULT_NO_ANSWER];

    /// <summary>A dispute settled: one result was returned by the most machines, two at least, with no tie.</summary>
    public const string DISPUTE_RESOLVED = "resolved";

    /// <summary>A dispute in which no result was returned by more copies than any other, and by two at least.</summary>
    public const string DISPUTE_NO_MAJORITY = "no_majority";

    /// <summary>A dispute for which fewer than two copies came back with a verdict (more values than three among them: none is taken then).</summary>
    public const string DISPUTE_TOO_FEW_COPIES = "too_few_copies";

    /// <summary>
    /// A dispute whose leading result does not lead by two machines once the values that may be
    /// one machine's second are left out: other shards of the round are short of answers.
    /// </summary>
    public const string DISPUTE_SURPLUS_COPIES = "surplus_copies";

    /// <summary>Every way a dispute ends.</summary>
    public static readonly IReadOnlyList<string> DISPUTE_OUTCOMES = [DISPUTE_RESOLVED, DISPUTE_NO_MAJORITY, DISPUTE_TOO_FEW_COPIES, DISPUTE_SURPLUS_COPIES];

    private const string FIELD_SIGNATURES = "signatures";

    #endregion

    #region Fields

    private readonly SortedDictionary<int, CorpusLedgerShard> m_shards = [];
    private readonly SortedDictionary<string, int> m_rejected = new(StringComparer.Ordinal);

    #endregion

    #region Constructors

    private CorpusLedger(string controllerVersion)
    {
        ControllerVersion = controllerVersion;
    }

    #endregion

    #region Functions

    /// <summary>Builds the ledger of a run from the rounds that have been run so far.</summary>
    /// <param name="controllerVersion">The server's controller version: the only one a result may carry.</param>
    /// <param name="first">The first round: the job's tasks and what came back.</param>
    /// <param name="repair">The repair round, or null when it has not run.</param>
    /// <param name="dispute">The dispute round, or null when it has not run.</param>
    /// <returns>The ledger.</returns>
    public static CorpusLedger Build(string controllerVersion, CorpusRound first, CorpusRound? repair = null, CorpusRound? dispute = null)
    {
        var ledger = new CorpusLedger(controllerVersion);

        foreach (var task in first.Tasks)
            ledger.m_shards.TryAdd(task.TaskIndex, new CorpusLedgerShard(task));

        ledger.Take(first, shard => shard.First, _ => { });
        foreach (var shard in ledger.m_shards.Values)
            JudgeFirst(shard);

        if (repair != null)
        {
            ledger.Take(repair, shard => shard.Repair, shard => shard.SentToRepair = true);
            foreach (var shard in ledger.m_shards.Values.Where(shard => shard.SentToRepair && shard.NeedsRepair))
                ledger.JudgeRepair(shard);
        }

        if (dispute != null)
        {
            ledger.Take(dispute, shard => shard.Dispute, shard => shard.SentToDispute = true);
            var sent = ledger.m_shards.Values.Where(shard => shard.SentToDispute && shard.NeedsDispute).ToList();

            // What the round is short of: every shard of it was asked for in three copies.
            var missing = sent.Sum(shard => Math.Max(0, DISPUTE_COPIES - shard.Dispute.Count));
            foreach (var shard in sent)
                ledger.JudgeDispute(shard, missing - Math.Max(0, DISPUTE_COPIES - shard.Dispute.Count));
        }

        foreach (var shard in ledger.m_shards.Values)
            Close(shard);

        ledger.NodeFaults = ledger.m_shards.Values.Sum(shard => shard.First.Concat(shard.Repair).Concat(shard.Dispute).Count(entry => entry.Kind == CorpusLedgerEntryKind.Fault));

        return ledger;
    }

    /// <summary>The tasks of the repair round: the shards the first round left without a result, each under a new ticket.</summary>
    /// <returns>The tasks, in shard order; empty when every shard has a result or a dispute.</returns>
    public List<CorpusTaskData> RepairTasks()
    {
        return m_shards.Values.Where(shard => shard.NeedsRepair && !shard.SentToRepair).Select(Reissue).ToList();
    }

    /// <summary>The tasks of the dispute round: the shards whose copies disagreed, each under a new ticket.</summary>
    /// <returns>The tasks, in shard order; empty when no copies disagreed.</returns>
    public List<CorpusTaskData> DisputeTasks()
    {
        return m_shards.Values.Where(shard => shard.NeedsDispute && !shard.SentToDispute).Select(Reissue).ToList();
    }

    /// <summary>The fields two results of one shard disagree on; empty when they agree.</summary>
    /// <param name="first">A checked result with a verdict on the shard.</param>
    /// <param name="second">Another one.</param>
    /// <returns>Field names: verdict, content, signatures, documents, drops, languages, pdf.</returns>
    public static List<string> Differences(CorpusResultData first, CorpusResultData second)
    {
        var fields = new List<string>();
        // A completed shard's note (a file that ends inside a record) is a function of its bytes;
        // the code of a shard that did not complete is how the origin answered that machine then.
        if (first.Verdict != second.Verdict || (first.Verdict == CorpusShardVerdict.Completed && !string.Equals(first.Error, second.Error, StringComparison.Ordinal)))
            fields.Add("verdict");
        if (!string.Equals(first.ContentSha256, second.ContentSha256, StringComparison.Ordinal))
            fields.Add("content");
        if (!string.Equals(first.SignaturesSha256, second.SignaturesSha256, StringComparison.Ordinal))
            fields.Add(FIELD_SIGNATURES);

        var firstStats = first.Stats ?? new CorpusShardStatsData();
        var secondStats = second.Stats ?? new CorpusShardStatsData();
        if (firstStats.DocumentsOut != secondStats.DocumentsOut || firstStats.DocumentsExtracted != secondStats.DocumentsExtracted || firstStats.RecordsIn != secondStats.RecordsIn)
            fields.Add("documents");
        // The breakdown of language_unsupported is part of the drops: a function of the shard.
        if (!SameCounts(firstStats.Drops, secondStats.Drops) || !SameCounts(firstStats.UnsupportedLanguages, secondStats.UnsupportedLanguages))
            fields.Add("drops");
        if (!SameCounts(firstStats.Languages, secondStats.Languages))
            fields.Add("languages");
        if (!SamePdf(firstStats.Pdf, secondStats.Pdf))
            fields.Add("pdf");

        return fields;
    }

    /// <summary>Whether a list of differences holds the signatures.</summary>
    /// <param name="differences">What <see cref="Differences"/> returned.</param>
    /// <returns>True when the signatures differ.</returns>
    public static bool SignaturesDiffer(IReadOnlyList<string> differences)
    {
        return differences.Contains(FIELD_SIGNATURES);
    }

    #endregion

    #region Binding

    /// <summary>Binds every value of a round to a shard by its ticket, or drops it.</summary>
    private void Take(CorpusRound round, Func<CorpusLedgerShard, List<CorpusLedgerEntry>> entries, Action<CorpusLedgerShard> sent)
    {
        var tickets = new Dictionary<string, (CorpusLedgerShard Shard, CorpusTaskData Task)>(StringComparer.Ordinal);
        foreach (var task in round.Tasks)
        {
            if (!CorpusTickets.IsWellFormed(task.Ticket) || !m_shards.TryGetValue(task.TaskIndex, out var shard))
                continue;

            if (tickets.TryAdd(task.Ticket, (shard, task)))
                sent(shard);
        }

        foreach (var value in round.Results)
        {
            if (value is not CorpusResultData result)
            {
                Reject(REJECTED_UNREADABLE);
                continue;
            }

            if (!CorpusTickets.IsWellFormed(result.Ticket) || !tickets.TryGetValue(result.Ticket, out var bound))
            {
                Reject(REJECTED_UNKNOWN_TICKET);
                continue;
            }

            entries(bound.Shard).Add(Entry(result, bound.Task));
        }
    }

    private CorpusLedgerEntry Entry(CorpusResultData result, CorpusTaskData task)
    {
        var reason = !CorpusResultCheck.IsWellFormed(result) ? REJECTED_UNREADABLE
            : result.TaskIndex != task.TaskIndex || !string.Equals(result.SourceUrl, task.Source?.Url ?? "", StringComparison.Ordinal) ? REJECTED_CONTENT
            : !string.Equals(result.ControllerVersion, ControllerVersion, StringComparison.Ordinal) ? REJECTED_VERSION
            : !Fits(result, task) ? REJECTED_CONTENT
            : null;

        if (reason != null)
        {
            Reject(reason);
            return new CorpusLedgerEntry(CorpusLedgerEntryKind.Rejected, null, reason);
        }

        return result.Verdict == CorpusShardVerdict.NodeFault
            ? new CorpusLedgerEntry(CorpusLedgerEntryKind.Fault, result, result.Error)
            : new CorpusLedgerEntry(CorpusLedgerEntryKind.Execution, result, "");
    }

    /// <summary>
    /// More values came back under a ticket than its round asked for, in a round that settles a
    /// shard: which of them is the answer cannot be told, so none is. Each value that would have
    /// been taken becomes a rejection (<see cref="REJECTED_SURPLUS"/>); one that was dropped for a
    /// reason of its own keeps it.
    /// </summary>
    private void DropSurplus(List<CorpusLedgerEntry> entries)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Kind == CorpusLedgerEntryKind.Rejected)
                continue;

            entries[index] = new CorpusLedgerEntry(CorpusLedgerEntryKind.Rejected, null, REJECTED_SURPLUS);
            Reject(REJECTED_SURPLUS);
        }
    }

    /// <summary>Whether a checked result is one the task's pipeline can have produced.</summary>
    private static bool Fits(CorpusResultData result, CorpusTaskData task)
    {
        var pipeline = task.Pipeline;
        if (pipeline == null)
            return result.Verdict != CorpusShardVerdict.Completed;

        if (result.Output != pipeline.Output)
            return false;

        if (result.Verdict != CorpusShardVerdict.Completed)
            return true;

        var keeps = pipeline.Output == CorpusOutputMode.Full;
        if ((result.TextBlobId != Guid.Empty) != keeps || (result.SignaturesBlobId != Guid.Empty) != (keeps && pipeline.Sign))
            return false;

        if (pipeline.Sign != (result.MinHash != null))
            return false;

        if (result.MinHash != null && MinHashParameters.Resolve(result.MinHash) != MinHashParameters.Resolve(pipeline.MinHash))
            return false;

        var stats = result.Stats!;
        if (stats.BytesFetched > CorpusTaskPlanner.NodeShardCap(task.MaxShardBytes))
            return false;

        if (!pipeline.IdentifyLanguage)
            return stats.UnsupportedLanguages.Count == 0 && stats.Languages.All(language => language.Name.Length == 0);

        if (stats.Languages.Any(language => language.Name.Length == 0))
            return false;

        return pipeline.TargetLanguages.Count == 0 || stats.Languages.All(language => pipeline.TargetLanguages.Contains(language.Name, StringComparer.Ordinal));
    }

    private void Reject(string reason)
    {
        m_rejected[reason] = m_rejected.GetValueOrDefault(reason) + 1;
        RejectedResults++;
    }

    #endregion

    #region Judging

    private static void JudgeFirst(CorpusLedgerShard shard)
    {
        var executions = Executions(shard.First);

        // More answers than the round sent out: one machine answered twice, and nothing says which.
        if (shard.First.Count > FIRST_ROUND_COPIES || !Agree(executions))
            shard.NeedsDispute = true;
        else if (executions.Count == 0)
            shard.NeedsRepair = true;
        else
            Accept(shard, executions[0], executions.Count);
    }

    private void JudgeRepair(CorpusLedgerShard shard)
    {
        shard.NeedsRepair = false;

        // The round asks for one value: with more that would be taken, which of them is the
        // answer cannot be told, so none is, and the shard has no result.
        if (Taken(shard.Repair) > REPAIR_COPIES)
        {
            DropSurplus(shard.Repair);
            return;
        }

        var executions = Executions(shard.Repair);
        if (executions.Count == 1)
            Accept(shard, executions[0], 1);
    }

    /// <summary>
    /// Settles one shard of the dispute round.
    /// </summary>
    /// <param name="shard">The shard.</param>
    /// <param name="borrowable">The values the other shards of the round are short of, together: as many of this shard's may be one machine's second.</param>
    private void JudgeDispute(CorpusLedgerShard shard, int borrowable)
    {
        // More values than machines were asked: none is taken.
        if (Taken(shard.Dispute) > DISPUTE_COPIES)
        {
            DropSurplus(shard.Dispute);
            Leave(shard, DISPUTE_TOO_FEW_COPIES, 0);
            return;
        }

        var executions = Executions(shard.Dispute);
        if (executions.Count < DISPUTE_QUORUM)
        {
            Leave(shard, DISPUTE_TOO_FEW_COPIES, executions.Count);
            return;
        }

        // The executions that agree with one another, the largest group first.
        var groups = new List<List<CorpusResultData>>();
        foreach (var execution in executions)
        {
            var same = groups.FirstOrDefault(group => Differences(group[0], execution).Count == 0);
            if (same == null)
                groups.Add([execution]);
            else
                same.Add(execution);
        }

        groups = groups.OrderByDescending(group => group.Count).ToList();
        var most = groups[0].Count;
        var next = groups.Count > 1 ? groups[1].Count : 0;

        if (most < DISPUTE_QUORUM || most <= next)
        {
            Leave(shard, DISPUTE_NO_MAJORITY, most);
            return;
        }

        // Two machines must have returned the leading result. Of its values, as many as the
        // other shards of the round are short of may be one machine's second, and do not count.
        var machines = most - borrowable;
        if (machines < DISPUTE_QUORUM || machines <= next)
        {
            Leave(shard, DISPUTE_SURPLUS_COPIES, most);
            return;
        }

        Accept(shard, groups[0][0], most);
        shard.DisputeOutcome = DISPUTE_RESOLVED;
        shard.DisputeAgreeing = most;
    }

    /// <summary>A dispute that was not settled: why, and how many of its executions agreed at the most.</summary>
    private static void Leave(CorpusLedgerShard shard, string outcome, int agreeing)
    {
        shard.DisputeOutcome = outcome;
        shard.DisputeAgreeing = agreeing;
    }

    /// <summary>What the shard ends as once no further round is coming.</summary>
    private static void Close(CorpusLedgerShard shard)
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
                shard.DisputeOutcome = DISPUTE_TOO_FEW_COPIES;

            shard.Outcome = CorpusShardOutcome.Disputed;
            return;
        }

        // What the nodes said: a fault of a node's own when one returned the shard so, and
        // "no answer" when nothing that was accepted came back.
        shard.Outcome = CorpusShardOutcome.NoResult;
        shard.Reason = shard.First.Concat(shard.Repair).Any(entry => entry.Kind == CorpusLedgerEntryKind.Fault) ? NO_RESULT_NODE_FAULT : NO_RESULT_NO_ANSWER;
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
            if (Differences(executions[0], executions[index]).Count > 0)
                return false;
        }

        return true;
    }

    /// <summary>The shard's task again, under a ticket of its own for the new round.</summary>
    private static CorpusTaskData Reissue(CorpusLedgerShard shard)
    {
        var task = shard.Task.Clone();
        task.Ticket = CorpusTickets.New();
        return task;
    }

    private static bool SamePdf(CorpusPdfStatsData? first, CorpusPdfStatsData? second)
    {
        // The counts an honest node cannot vary; the fuel is a cost (and a function of the same
        // inputs), compared through the documents it produced.
        if (first == null || second == null)
            return first == null && second == null;

        return first.In == second.In && first.Truncated == second.Truncated && first.Extracted == second.Extracted
               && first.Pages == second.Pages && first.Documents == second.Documents
               && first.PagesCut == second.PagesCut && first.RunningLines == second.RunningLines;
    }

    private static bool SameCounts(IReadOnlyList<CorpusCountData> first, IReadOnlyList<CorpusCountData> second)
    {
        if (first.Count != second.Count)
            return false;

        for (var index = 0; index < first.Count; index++)
        {
            if (first[index].Count != second[index].Count || !string.Equals(first[index].Name, second[index].Name, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    #endregion

    #region Properties

    /// <summary>The server's controller version.</summary>
    public string ControllerVersion { get; }

    /// <summary>Every shard of the job, in index order.</summary>
    public IReadOnlyCollection<CorpusLedgerShard> Shards => m_shards.Values;

    /// <summary>Values dropped over every round.</summary>
    public int RejectedResults { get; private set; }

    /// <summary>The dropped values by reason, reasons in ordinal order.</summary>
    public IReadOnlyDictionary<string, int> Rejected => m_rejected;

    /// <summary>Results returned as a node fault over every round (not the ones dropped as surplus).</summary>
    public int NodeFaults { get; private set; }

    /// <summary>Shards the repair round took.</summary>
    public int Repairs => m_shards.Values.Count(shard => shard.SentToRepair);

    /// <summary>Shards the repair round gave a result.</summary>
    public int Repaired => m_shards.Values.Count(shard => shard.SentToRepair && !shard.SentToDispute && shard.Outcome == CorpusShardOutcome.Accepted);

    /// <summary>Shards whose copies disagreed.</summary>
    public int Disputes => m_shards.Values.Count(shard => shard.NeedsDispute);

    /// <summary>Of those, the ones the dispute round settled.</summary>
    public int DisputesResolved => m_shards.Values.Count(shard => shard.NeedsDispute && shard.Outcome == CorpusShardOutcome.Accepted);

    /// <summary>Shards accepted on one machine's word.</summary>
    public int SingleExecutions => m_shards.Values.Count(shard => shard.Outcome == CorpusShardOutcome.Accepted && shard.Copies == 1);

    #endregion
}
