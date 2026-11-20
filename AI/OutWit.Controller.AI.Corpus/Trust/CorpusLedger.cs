using OutWit.Controller.AI.Corpus.Model;

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
/// results were all dropped, is sent out once more; if that brings nothing the shard has no
/// result, with a fixed reason.</item>
/// <item><b>A round that came back short keeps its checking.</b> The server does not learn which
/// packages the platform picked for a second machine, so it cannot tell a package that was
/// picked and lost both answers from one that was never picked. When the run asked for
/// sampling at all, the script therefore sends every shard of the repair round to two machines,
/// and the repair round is judged as the first: copies that agree are accepted, copies that
/// disagree (or more values than two) are a dispute. A machine that returned nothing in the
/// first round, or made the first round's answers unusable, does not get the shards back to
/// answer them alone. What the run asked for and how each round came back is in
/// <see cref="Sampling"/> and in the rounds' answers, and both documents state it.</item>
/// </list>
/// The same ledger answers at every step of the script: which shards the repair round takes,
/// which the dispute round, and what the report and the integrity document say.
/// <para>
/// <b>Nothing of a run is held.</b> The ledger reads its rounds from their sources
/// (<see cref="CorpusRoundSource"/>), one task package at a time, judges each shard as its values
/// are read and hands it over; what it keeps is counts. A question that needs the shards (the
/// report, the next round's tasks) reads the run again in the same way, so the memory a run of any
/// length takes is that of one package.
/// </para>
/// <para>
/// <b>Two machines, not two values.</b> The server sees values, not machines: the platform keeps
/// the copies of a package on different machines and hands over what they returned without saying
/// which task each value answered. A machine that holds two packages of a round can spend the
/// slot of one on the other's ticket, which leaves the one short. So in the dispute round, where
/// every package is asked for in three copies, a value counts towards "two machines" only as far
/// as it cannot be such a second answer: of a shard's leading result, as many values as the other
/// packages of the round are short of may be one machine's second, and do not count. With one
/// lying machine among the copies, whatever the others lose, the result that is accepted was
/// returned by an honest machine; and three machines that agree still settle their shard when
/// another package of the round lost a copy. Counting values as machines rests on two things: an
/// answer gives a shard one value at most (<see cref="CorpusRoundSourceBlobs"/>), and the
/// platform — Grid 1.3.0 and later — keeps, of what a node's batch returns, one value at most for
/// each return variable its tasks asked for. With an older Grid a node could return several
/// answers for one task and this ruling would count them as machines.
/// </para>
/// <para>
/// <b>A machine that answers nothing.</b> A value that came back under no ticket of the round, or
/// that is no answer at all, was given by a slot that lent nothing to another package: of a
/// shard's leading result no more values count as borrowed than the round's shortfall that such
/// values do not explain (<see cref="CorpusRoundSource.Unexplained"/>: the slots that brought
/// nothing, and the answers past three a package got, since an honest copy that came back
/// rejected hides a lent value as a silent one does). A machine that leaves the slots of two dispute packages it holds without any answer still
/// cannot be told from honest machines that fell silent, and every dispute of that round may then
/// end <c>surplus_copies</c>: such disputes, and the ones that came back with too few copies, go
/// out once more on three machines (<see cref="CorpusRoundKind.DisputeAgain"/>), where the platform
/// draws the machines anew. A machine that holds two packages of that round as well and answers
/// neither leaves them disputed: the shards have no product, never a wrong one. Closing it needs a
/// ticket for each copy of a package, which the platform does not give.
/// </para>
/// <para>
/// What is left: a shard that was not sampled rests on the machine that ran it, and a machine
/// that holds a sampled package's copy and a package of its own can spend the copy's slot on its
/// own package, whose shards then show two agreeing executions that are one machine's. No product
/// changes by it (those shards were one machine's word either way); the count of executions
/// overstates how they were checked. The same holds in the repair round, and there also for a
/// run that asked for no sampling: its repair round goes out on one machine, and two values
/// that agree under one of its tickets are one machine's.
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

    /// <summary>The most results a shard has in the repair round: itself and one copy, when the run asked for sampling (one otherwise).</summary>
    public const int REPAIR_COPIES = 2;

    /// <summary>A value under no ticket of the round it came back in: another job's, another round's, another package's the node was not given, or none.</summary>
    public const string REJECTED_UNKNOWN_TICKET = "unknown_ticket";

    /// <summary>A value that is no result, or a result not shaped as an honest node shapes one (<see cref="CorpusResultCheck"/>).</summary>
    public const string REJECTED_UNREADABLE = "unreadable";

    /// <summary>A result that claims another controller version than the server's.</summary>
    public const string REJECTED_VERSION = "version";

    /// <summary>
    /// A result that does not hold what its task asked for: its ticket is one shard's and its
    /// index or URL another's, or it is at odds with the task (another output mode, other MinHash
    /// parameters, a product where none is kept, more bytes than the shard was to be held to,
    /// languages the pipeline cannot label, PDF counts where the task runs no PDF stage).
    /// </summary>
    public const string REJECTED_CONTENT = "content";

    /// <summary>
    /// More values under one ticket than are taken: in the dispute round, which settles a shard
    /// for good, none of them is the answer then; an answer beyond the ones a round opens for a
    /// package; two results for one shard in one answer.
    /// </summary>
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
    /// one machine's second are left out: other packages of the round are short of answers.
    /// </summary>
    public const string DISPUTE_SURPLUS_COPIES = "surplus_copies";

    /// <summary>Every way a dispute ends.</summary>
    public static readonly IReadOnlyList<string> DISPUTE_OUTCOMES = [DISPUTE_RESOLVED, DISPUTE_NO_MAJORITY, DISPUTE_TOO_FEW_COPIES, DISPUTE_SURPLUS_COPIES];

    private const string FIELD_SIGNATURES = "signatures";

    #endregion

    #region Fields

    private readonly CorpusRoundSource m_first;
    private readonly CorpusRoundSource? m_repair;
    private readonly CorpusRoundSource? m_dispute;
    private readonly CorpusRoundSource? m_again;
    private readonly CorpusLedgerShards m_shards;
    private readonly CorpusLedgerTally m_tally;

    #endregion

    #region Constructors

    private CorpusLedger(string controllerVersion, CorpusRoundSource first, CorpusRoundSource? repair, CorpusRoundSource? dispute, double? samplingRate, CorpusRoundSource? again)
    {
        ControllerVersion = controllerVersion;
        m_first = first;
        m_repair = repair;
        m_dispute = dispute;
        m_again = again;
        m_shards = new CorpusLedgerShards(first.Shards, () => Judge(new CorpusLedgerTally()));

        // One reading of the run for everything that is a count: the shards themselves are not kept.
        m_tally = new CorpusLedgerTally();
        foreach (var shard in Judge(m_tally))
            m_tally.Add(shard);

        Sampling = CorpusSampling.Of(samplingRate, first.Answers, m_tally.Compared);
    }

    #endregion

    #region Functions

    /// <summary>Builds the ledger of a run held in memory from the rounds that have been run so far.</summary>
    /// <param name="controllerVersion">The server's controller version: the only one a result may carry.</param>
    /// <param name="first">The first round: the job's tasks and what came back.</param>
    /// <param name="repair">The repair round, or null when it has not run.</param>
    /// <param name="dispute">The dispute round, or null when it has not run.</param>
    /// <param name="samplingRate">The share of the packages the run asked to run twice, 0 to 1 (<see cref="CorpusSampling.RateOf"/>); null when the caller does not know it. No ruling reads it: it is stated beside what came back (<see cref="Sampling"/>).</param>
    /// <param name="again">The second dispute round, or null when it has not run.</param>
    /// <returns>The ledger.</returns>
    public static CorpusLedger Build(string controllerVersion, CorpusRound first, CorpusRound? repair = null, CorpusRound? dispute = null, double? samplingRate = null, CorpusRound? again = null)
    {
        return Build(
            controllerVersion,
            new CorpusRoundSourceMemory(first),
            repair == null ? null : new CorpusRoundSourceMemory(repair),
            dispute == null ? null : new CorpusRoundSourceMemory(dispute),
            samplingRate,
            again == null ? null : new CorpusRoundSourceMemory(again));
    }

    /// <summary>
    /// Builds the ledger of a run from the sources of its rounds. The run is read once here, one
    /// task package at a time, for its counts; every later question (the shards, the tasks of the
    /// next round) reads it again in the same way.
    /// </summary>
    /// <param name="controllerVersion">The server's controller version: the only one a result may carry.</param>
    /// <param name="first">The first round.</param>
    /// <param name="repair">The repair round, or null when it has not run.</param>
    /// <param name="dispute">The dispute round, or null when it has not run.</param>
    /// <param name="samplingRate">The share of the packages the run asked to run twice, 0 to 1 (<see cref="CorpusSampling.RateOf"/>); null when the caller does not know it. No ruling reads it: it is stated beside what came back (<see cref="Sampling"/>).</param>
    /// <param name="again">The second dispute round, or null when it has not run.</param>
    /// <returns>The ledger.</returns>
    /// <exception cref="InvalidOperationException">A round's task packages are not in ascending shard order: they are not the ones this controller wrote.</exception>
    public static CorpusLedger Build(string controllerVersion, CorpusRoundSource first, CorpusRoundSource? repair = null, CorpusRoundSource? dispute = null, double? samplingRate = null, CorpusRoundSource? again = null)
    {
        return new CorpusLedger(controllerVersion, first, repair, dispute, samplingRate, again);
    }

    /// <summary>Whether a round's values for a shard are a pair that was compared: the first two are both results with a verdict.</summary>
    /// <param name="entries">What came back under the shard's ticket in one round, checked.</param>
    /// <returns>True when two machines' results stand side by side.</returns>
    public static bool IsCompared(IReadOnlyList<CorpusLedgerEntry> entries)
    {
        return entries.Count >= 2 && entries[0].Kind == CorpusLedgerEntryKind.Execution && entries[1].Kind == CorpusLedgerEntryKind.Execution;
    }

    /// <summary>The tasks of the repair round: the shards the first round left without a result, each under a new ticket.</summary>
    /// <returns>The tasks, in shard order; empty when every shard has a result or a dispute.</returns>
    public List<CorpusTaskData> RepairTasks()
    {
        return EnumerateRepairTasks().ToList();
    }

    /// <summary>The tasks of the repair round, made one at a time as they are asked for (<see cref="PendingRepairs"/> says how many there are).</summary>
    /// <returns>The tasks, in shard order.</returns>
    public IEnumerable<CorpusTaskData> EnumerateRepairTasks()
    {
        return m_shards.Where(shard => shard.NeedsRepair && !shard.SentToRepair).Select(Reissue);
    }

    /// <summary>The tasks of the dispute round: the shards whose copies disagreed, each under a new ticket.</summary>
    /// <returns>The tasks, in shard order; empty when no copies disagreed.</returns>
    public List<CorpusTaskData> DisputeTasks()
    {
        return EnumerateDisputeTasks().ToList();
    }

    /// <summary>The tasks of the dispute round, made one at a time as they are asked for (<see cref="PendingDisputes"/> says how many there are).</summary>
    /// <returns>The tasks, in shard order.</returns>
    public IEnumerable<CorpusTaskData> EnumerateDisputeTasks()
    {
        return m_shards.Where(shard => shard.NeedsDispute && !shard.SentToDispute).Select(Reissue);
    }

    /// <summary>
    /// The tasks of the second dispute round, made one at a time as they are asked for
    /// (<see cref="PendingDisputesAgain"/> says how many there are): the disputes the dispute
    /// round left with too few copies or with a lead that may be one machine's second answers,
    /// each under a new ticket.
    /// </summary>
    /// <returns>The tasks, in shard order.</returns>
    public IEnumerable<CorpusTaskData> EnumerateDisputeAgainTasks()
    {
        return m_shards.Where(shard => shard.NeedsDisputeAgain && !shard.SentToDisputeAgain).Select(Reissue);
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

    #region Reading

    /// <summary>
    /// Reads the run from its start and judges it shard by shard: the first round's packages in
    /// order, and beside them the repair and the dispute round, which hold shards of the same job
    /// in the same order. Every shard is complete when it is handed over, and is not kept.
    /// </summary>
    /// <param name="tally">Receives what is dropped on the way.</param>
    private IEnumerable<CorpusLedgerShard> Judge(CorpusLedgerTally tally)
    {
        tally.Add(m_first.Unbound);
        tally.Add(m_repair?.Unbound);
        tally.Add(m_dispute?.Unbound);
        tally.Add(m_again?.Unbound);

        using var repair = new CorpusLedgerCursor(m_repair, tally);
        using var dispute = new CorpusLedgerCursor(m_dispute, tally);
        using var again = new CorpusLedgerCursor(m_again, tally);
        var last = -1;

        foreach (var package in m_first.Read())
        {
            tally.Add(package.Dropped);

            var at = 0;
            while (at < package.Slots.Count)
            {
                // A shard is its first task; a second task for the same shard binds to it too.
                var task = package.Slots[at].Task;
                if (task.TaskIndex <= last)
                    throw new InvalidOperationException(CorpusRoundSourceBlobs.ERROR_NOT_TASK_PACKAGES);

                last = task.TaskIndex;
                var shard = new CorpusLedgerShard(task);
                for (; at < package.Slots.Count && package.Slots[at].Task.TaskIndex == last; at++)
                    CorpusLedgerIntake.Take(package.Slots[at], shard.First, tally, ControllerVersion);

                CorpusLedgerRuling.JudgeFirst(shard);

                if (repair.Take(last, out var repairSlots, out _))
                {
                    shard.SentToRepair = true;
                    foreach (var slot in repairSlots)
                        CorpusLedgerIntake.Take(slot, shard.Repair, tally, ControllerVersion);

                    if (shard.NeedsRepair)
                        CorpusLedgerRuling.JudgeRepair(shard);
                }

                if (dispute.Take(last, out var disputeSlots, out var disputePackage))
                {
                    shard.SentToDispute = true;
                    foreach (var slot in disputeSlots)
                        CorpusLedgerIntake.Take(slot, shard.Dispute, tally, ControllerVersion);

                    if (shard.NeedsDispute)
                        CorpusLedgerRuling.JudgeDispute(shard, Borrowable(m_dispute!, disputePackage!), tally);
                }

                if (again.Take(last, out var againSlots, out var againPackage))
                {
                    shard.SentToDisputeAgain = true;
                    foreach (var slot in againSlots)
                        CorpusLedgerIntake.Take(slot, shard.Again, tally, ControllerVersion);

                    if (shard.NeedsDisputeAgain)
                        CorpusLedgerRuling.JudgeDisputeAgain(shard, Borrowable(m_again!, againPackage!), tally);
                }

                CorpusLedgerRuling.Close(shard);
                yield return shard;
            }
        }

        repair.Finish();
        dispute.Finish();
        again.Finish();
    }

    /// <summary>
    /// Of a dispute's leading result, how many values may be one machine's second answer: what the
    /// other packages of the round are short of (every one was asked for in three copies), and no
    /// more than the round's shortfall that values under no ticket do not explain
    /// (<see cref="CorpusRoundSource.Unexplained"/>). A value under no ticket lent nothing; one
    /// machine can only borrow for a shard where an honest copy of it fell silent or came back as
    /// a value that is not taken, and either leaves the round a slot short or a package an answer
    /// over its three.
    /// </summary>
    private static int Borrowable(CorpusRoundSource round, CorpusRoundPackage package)
    {
        var shortElsewhere = round.Missing - Math.Max(0, DISPUTE_COPIES - package.Returned);
        return round.Unexplained is { } unexplained ? Math.Min(shortElsewhere, unexplained) : shortElsewhere;
    }

    #endregion

    #region Tools

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

    /// <summary>
    /// Every shard of the job, in index order. The shards are not held: each enumeration reads the
    /// run again, one task package at a time, and hands over each shard as it is judged; the
    /// count is known without reading.
    /// </summary>
    public IReadOnlyCollection<CorpusLedgerShard> Shards => m_shards;

    /// <summary>Values dropped over every round.</summary>
    public int RejectedResults => m_tally.RejectedResults;

    /// <summary>The dropped values by reason, reasons in ordinal order.</summary>
    public IReadOnlyDictionary<string, int> Rejected => m_tally.Rejected;

    /// <summary>Shards returned as a node fault over every round (not the ones dropped as surplus).</summary>
    public int NodeFaults => m_tally.NodeFaults;

    /// <summary>Shards the repair round took.</summary>
    public int Repairs => m_tally.Repairs;

    /// <summary>Shards the repair round gave a result.</summary>
    public int Repaired => m_tally.Repaired;

    /// <summary>Shards whose copies disagreed.</summary>
    public int Disputes => m_tally.Disputes;

    /// <summary>Of those, the ones the dispute round settled.</summary>
    public int DisputesResolved => m_tally.DisputesResolved;

    /// <summary>Shards accepted on one machine's word.</summary>
    public int SingleExecutions => m_tally.SingleExecutions;

    /// <summary>Shards the first round left without a result that no repair round has taken yet: how many tasks <see cref="EnumerateRepairTasks"/> gives.</summary>
    public int PendingRepairs => m_tally.PendingRepairs;

    /// <summary>Shards whose copies disagreed that no dispute round has taken yet: how many tasks <see cref="EnumerateDisputeTasks"/> gives.</summary>
    public int PendingDisputes => m_tally.PendingDisputes;

    /// <summary>Disputes the dispute round left with too few copies or a lead that may be one machine's, that no second dispute round has taken yet: how many tasks <see cref="EnumerateDisputeAgainTasks"/> gives.</summary>
    public int PendingDisputesAgain => m_tally.PendingDisputesAgain;

    /// <summary>Disputes that went out once more, to the second dispute round.</summary>
    public int DisputesAgain => m_tally.DisputesAgain;

    /// <summary>Of those, the ones the second dispute round settled.</summary>
    public int DisputesAgainResolved => m_tally.DisputesAgainResolved;

    /// <summary>What the run asked for in second executions and what came back of it in the first round.</summary>
    public CorpusSampling Sampling { get; }

    /// <summary>How the first round's fan-out came back: its packages by the answers each got.</summary>
    public CorpusRoundAnswers FirstRound => m_first.Answers;

    /// <summary>How the repair round's fan-out came back; no package when the round has not run.</summary>
    public CorpusRoundAnswers RepairRound => m_repair?.Answers ?? CorpusRoundAnswers.NONE;

    /// <summary>How the dispute round's fan-out came back; no package when the round has not run.</summary>
    public CorpusRoundAnswers DisputeRound => m_dispute?.Answers ?? CorpusRoundAnswers.NONE;

    /// <summary>How the second dispute round's fan-out came back; no package when the round has not run.</summary>
    public CorpusRoundAnswers DisputeAgainRound => m_again?.Answers ?? CorpusRoundAnswers.NONE;

    #endregion
}
