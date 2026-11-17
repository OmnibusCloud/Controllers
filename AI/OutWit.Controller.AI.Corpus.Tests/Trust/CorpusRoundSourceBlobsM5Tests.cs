using System.Text.Json;
using OutWit.Controller.AI.Corpus.Delivery;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Tests.Trust;

/// <summary>
/// Audit finding M-5, at the server's reading of a run: the tasks and the results of a round are
/// blobs, an answer is bound to its task package by the package's ticket before anything of it is
/// opened, an opened answer is read within bounds and gives a shard one value at most, and the
/// ledger over blobs rules exactly as the ledger over a run held in memory. What a dishonest node
/// can put into an answer costs that answer, never the run.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class CorpusRoundSourceBlobsM5Tests
{
    #region Fields

    private readonly Dictionary<string, string> m_packageOfShard = new(StringComparer.Ordinal);
    private string m_directory = null!;
    private CountingBlobs m_blobs = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_rounds_{Guid.NewGuid():N}");
        m_blobs = new CountingBlobs(new CorpusTestBlobService(m_directory));
    }

    [TearDown]
    public void TearDown()
    {
        CorpusTestFolders.Delete(m_directory);
    }

    #endregion

    #region Binding

    [Test]
    public async Task M5AnAnswerUnderNoTicketOfTheRoundIsDroppedUnopenedTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        var packages = CorpusTestPackages.Packages(m_blobs, tasks);
        var stranger = new CorpusResultPackageData { Ticket = CorpusTickets.New(), ResultsBlobId = Guid.NewGuid() };
        var shapeless = new CorpusResultPackageData { Ticket = "", ResultsBlobId = Guid.NewGuid() };
        m_blobs.Asked.Clear();

        var ledger = await LedgerAsync(packages, [Answer(packages[0], CorpusTestRun.Completed(tasks[0])), stranger, shapeless, "not an answer", null]);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Rejected[CorpusLedger.REJECTED_UNKNOWN_TICKET], Is.EqualTo(2));
            Assert.That(ledger.Rejected[CorpusLedger.REJECTED_UNREADABLE], Is.EqualTo(2));
            Assert.That(m_blobs.Asked, Does.Not.Contain(stranger.ResultsBlobId).And.Not.Contain(shapeless.ResultsBlobId), "a blob named under no ticket of the round is never asked for");
            Assert.That(ledger.Shards.First().Outcome, Is.EqualTo(CorpusShardOutcome.Accepted));
            Assert.That(ledger.RepairTasks().Select(task => task.TaskIndex), Is.EqualTo(new[] { 1 }));
        });
    }

    [Test]
    public async Task M5AnswersBeyondOneMoreThanTheRoundAsksAreDroppedUnopenedTest()
    {
        // One machine answers a package in every slot it holds. The first round asks for two:
        // three are opened (enough to see that more came back than were asked), the rest are not.
        var tasks = CorpusTestRun.Tasks(1);
        var packages = CorpusTestPackages.Packages(m_blobs, tasks);
        var answers = Enumerable.Range(0, 6).Select(_ => (object?)Answer(packages[0], CorpusTestRun.Completed(tasks[0], '0'))).ToList();
        m_blobs.Asked.Clear();

        var ledger = await LedgerAsync(packages, answers);
        var shard = ledger.Shards.Single();

        Assert.Multiple(() =>
        {
            Assert.That(shard.First, Has.Count.EqualTo(CorpusRoundSource.OpenedOf(CorpusRoundKind.First)));
            Assert.That(shard.NeedsDispute, Is.True, "more answers than the round sent out");
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_SURPLUS] = 3 }));
            Assert.That(m_blobs.Asked.Count(id => id != packages[0].TasksBlobId), Is.EqualTo(3), "three answers opened, three never asked for");
        });
    }

    [Test]
    public async Task M5TwoResultsForOneShardInOneAnswerAreNotTakenTest()
    {
        // Counting values as machines needs an answer to give a shard one value at most. A
        // package of two shards: one answer holds two results for the first and none for the second.
        var tasks = CorpusTestRun.Tasks(2);
        var packages = Packages(tasks, perPackage: 2);
        var twice = CorpusTestPackages.Answer(m_blobs, packages[0].Ticket, [CorpusTestRun.Completed(tasks[0], '0'), CorpusTestRun.Completed(tasks[0], '0')]);

        var ledger = await LedgerAsync(packages, [twice, Answer(packages[0], CorpusTestRun.Completed(tasks[0], 'a'), CorpusTestRun.Completed(tasks[1]))]);
        var shard = ledger.Shards.First();

        Assert.Multiple(() =>
        {
            Assert.That(shard.First.Select(entry => entry.Kind), Is.EqualTo(new[] { CorpusLedgerEntryKind.Rejected, CorpusLedgerEntryKind.Execution }), "the double answer is one entry, and it is no result");
            Assert.That(shard.First[0].Code, Is.EqualTo(CorpusLedger.REJECTED_SURPLUS));
            Assert.That(shard.Accepted!.ContentSha256, Is.EqualTo(CorpusTestRun.Hash('a')), "the other machine's word stands, as one execution");
            Assert.That(shard.Copies, Is.EqualTo(1));
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_SURPLUS] = 2 }), "both values of the double answer are counted");
        });
    }

    [Test]
    public async Task M5AResultUnderATicketOfAnotherPackageAnswersNothingTest()
    {
        // A node that holds both packages (two shards each) answers the first with a result for
        // a shard of the second in the place of one of its own.
        var tasks = CorpusTestRun.Tasks(4);
        var packages = Packages(tasks, perPackage: 2);
        var foreign = CorpusTestPackages.Answer(m_blobs, packages[0].Ticket, [CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[2], '0')]);

        var ledger = await LedgerAsync(packages, [foreign, Answer(packages[1], CorpusTestRun.Completed(tasks[2], 'a'), CorpusTestRun.Completed(tasks[3], 'a'))]);
        var shards = ledger.Shards.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(shards[2].First, Has.Count.EqualTo(1), "the forged result did not reach the other package's shard");
            Assert.That(shards[2].Accepted!.ContentSha256, Is.EqualTo(CorpusTestRun.Hash('a')));
            Assert.That(shards[1].Reason, Is.EqualTo(CorpusLedger.NO_RESULT_NO_ANSWER), "the shard whose place it took has no answer");
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_UNKNOWN_TICKET] = 1 }));
        });

        // In a package of one shard an answer has room for one result: a second is past the bound.
        var single = CorpusTestRun.Tasks(2);
        var singles = CorpusTestPackages.Packages(m_blobs, single);
        var crowded = CorpusTestPackages.Answer(m_blobs, singles[0].Ticket, [CorpusTestRun.Completed(single[0]), CorpusTestRun.Completed(single[1], '0')]);
        var bounded = await LedgerAsync(singles, [crowded, Answer(singles[1], CorpusTestRun.Completed(single[1], 'a'))]);

        Assert.Multiple(() =>
        {
            Assert.That(bounded.Shards.Last().First, Has.Count.EqualTo(1));
            Assert.That(bounded.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_UNREADABLE] = 1 }), "the reading stops at as many results as the package has shards");
        });
    }

    #endregion

    #region Faults and damage

    [Test]
    public async Task M5APackageReturnedAsAFaultFaultsEveryShardOfItTest()
    {
        var tasks = CorpusTestRun.Tasks(6);
        var packages = Packages(tasks, perPackage: 3);
        var fault = new CorpusResultPackageData { Ticket = packages[0].Ticket, Fault = CorpusResultCodes.FAULT_TASKS };

        var ledger = await LedgerAsync(packages, [fault, Answer(packages[1], tasks.Skip(3).Select(task => CorpusTestRun.Completed(task)).ToArray())]);
        var shards = ledger.Shards.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(packages, Has.Count.EqualTo(2));
            Assert.That(shards.Take(3).Select(shard => shard.Reason), Is.All.EqualTo(CorpusLedger.NO_RESULT_NODE_FAULT));
            Assert.That(shards.Take(3).Select(shard => shard.Faults.Single()), Is.All.EqualTo(CorpusResultCodes.FAULT_TASKS));
            Assert.That(shards.Skip(3).Select(shard => shard.Outcome), Is.All.EqualTo(CorpusShardOutcome.Accepted));
            Assert.That(ledger.NodeFaults, Is.EqualTo(3));
            Assert.That(ledger.RejectedResults, Is.Zero, "a fault is no dropped value");
            Assert.That(ledger.RepairTasks().Select(task => task.TaskIndex), Is.EqualTo(new[] { 0, 1, 2 }), "the package's shards are sent out once more");
        });
    }

    [Test]
    public async Task M5AnAnswerThatIsNeitherResultsNorAFaultIsUnreadableTest()
    {
        var tasks = CorpusTestRun.Tasks(4);
        var packages = CorpusTestPackages.Packages(m_blobs, tasks);
        var honest = Answer(packages[2], CorpusTestRun.Completed(tasks[2]));

        var ledger = await LedgerAsync(packages,
        [
            new CorpusResultPackageData { Ticket = packages[0].Ticket, Fault = @"C:\Users\someone\AppData\Local\Temp is full" },
            new CorpusResultPackageData { Ticket = packages[1].Ticket, Fault = CorpusResultCodes.FAULT_DISK, ResultsBlobId = honest.ResultsBlobId },
            honest,
            new CorpusResultPackageData { Ticket = packages[3].Ticket, ResultsBlobId = Guid.NewGuid() }
        ]);
        var report = System.Text.Encoding.UTF8.GetString(CorpusReportWriter.Write(ledger)) + System.Text.Encoding.UTF8.GetString(CorpusIntegrityWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_UNREADABLE] = 3 }), "free text for a fault, a fault with a blob, a blob the server does not hold");
            Assert.That(ledger.NodeFaults, Is.Zero, "a fault is a fixed code or nothing");
            Assert.That(report, Does.Not.Contain("someone").And.Not.Contain("AppData"));
            Assert.That(ledger.Shards.Select(shard => shard.Outcome), Is.EqualTo(new[] { CorpusShardOutcome.NoResult, CorpusShardOutcome.NoResult, CorpusShardOutcome.Accepted, CorpusShardOutcome.NoResult }));
        });
    }

    [Test]
    public async Task M5AnAnswerLargerThanTheResultsOfItsPackageCanBeIsNotReadTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var packages = CorpusTestPackages.Packages(m_blobs, tasks);
        var huge = new byte[CorpusPackageCodec.MaxResultsBlobBytes(1) + 1];
        var bomb = new CorpusResultPackageData { Ticket = packages[0].Ticket, ResultsBlobId = await m_blobs.UploadBytesAsync(huge, CorpusPackageCodec.RESULTS_FILE_NAME) };

        var ledger = await LedgerAsync(packages, [bomb]);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_UNREADABLE] = 1 }));
            Assert.That(ledger.Shards.Single().Reason, Is.EqualTo(CorpusLedger.NO_RESULT_NO_ANSWER));
        });
    }

    [Test]
    public async Task M5ADamagedAnswerCostsThatAnswerNotTheRunTest()
    {
        // A results blob cut in the middle, and one of bytes that are no package at all: the run
        // has its report, the shards the damage took are sent out once more.
        var tasks = CorpusTestRun.Tasks(6);
        var packages = Packages(tasks, perPackage: 3);
        var whole = Answer(packages[0], tasks.Take(3).Select(task => CorpusTestRun.Completed(task)).ToArray());
        var bytes = await File.ReadAllBytesAsync(await m_blobs.GetLocalPathAsync(whole.ResultsBlobId));
        var cut = new CorpusResultPackageData { Ticket = packages[0].Ticket, ResultsBlobId = await m_blobs.UploadBytesAsync(bytes[..(bytes.Length / 2)], "cut.wcp") };
        var noise = new CorpusResultPackageData { Ticket = packages[1].Ticket, ResultsBlobId = await m_blobs.UploadBytesAsync([1, 2, 3, 4, 5, 6, 7, 8, 9], "noise.wcp") };

        var ledger = await LedgerAsync(packages, [cut, noise]);
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("tasks").GetInt32(), Is.EqualTo(6), "the report is written");
            Assert.That(ledger.Rejected.Keys, Is.EqualTo(new[] { CorpusLedger.REJECTED_UNREADABLE }));
            Assert.That(ledger.RepairTasks().Select(task => task.TaskIndex), Is.SupersetOf(new[] { 3, 4, 5 }), "the package of the noise has no result at all");
            Assert.That(ledger.PendingRepairs + ledger.Shards.Count(shard => shard.Outcome == CorpusShardOutcome.Accepted), Is.EqualTo(6));
        });
    }

    [Test]
    public void M5TaskPackagesThatAreNotTheServersAreRefusedInFixedWordsTest()
    {
        var tasks = CorpusTestRun.Tasks(3);
        var packages = CorpusTestPackages.Packages(m_blobs, tasks);
        var noTicket = packages[0].Clone();
        noTicket.Ticket = "";

        Assert.Multiple(() =>
        {
            // Out of order, not a package, a package without a ticket, the same package twice.
            foreach (var bad in new object?[][] { [packages[1], packages[0]], ["tasks"], [noTicket], [packages[0], packages[0]] })
            {
                var failure = Assert.ThrowsAsync<InvalidOperationException>(async () => await LedgerAsync(bad, []));
                Assert.That(failure!.Message, Is.EqualTo(CorpusRoundSourceBlobs.ERROR_NOT_TASK_PACKAGES));
            }
        });
    }

    #endregion

    #region The same rulings

    [Test]
    public async Task M5TheSameRunThroughBlobsGivesTheSameDocumentsTest()
    {
        // A run with everything in it: pairs that agree and that do not, a fault, a dropped value,
        // a repair, a dispute settled and one that is not.
        var tasks = CorpusTestRun.Tasks(8, CorpusTestRun.Signed());
        var first = new List<CorpusResultData>
        {
            CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[0]),
            CorpusTestRun.Completed(tasks[1], 'a'), CorpusTestRun.Completed(tasks[1], '0'),
            CorpusTestRun.Fault(tasks[2]),
            CorpusTestRun.Completed(tasks[3], drops: [("min_words", 4)]),
            CorpusTestRun.Completed(tasks[4], 'a'), CorpusTestRun.Completed(tasks[4], 'b'),
            CorpusTestRun.Completed(tasks[6]),
            CorpusTestRun.Verdict(tasks[7], CorpusShardVerdict.FetchFailed, "http_404")
        };
        first[5].ControllerVersion = "9.9.9";

        var before = CorpusTestRun.Ledger(tasks, first);
        var repairs = before.RepairTasks();
        var disputes = before.DisputeTasks();
        var repaired = new List<CorpusResultData> { CorpusTestRun.Completed(repairs[0], 'c'), CorpusTestRun.Fault(repairs[2], CorpusResultCodes.FAULT_NETWORK) };
        var resolved = new List<CorpusResultData>
        {
            CorpusTestRun.Completed(disputes[0], 'a'), CorpusTestRun.Completed(disputes[0], 'a'), CorpusTestRun.Completed(disputes[0], '0'),
            CorpusTestRun.Completed(disputes[1], 'a'), CorpusTestRun.Completed(disputes[1], 'b'), CorpusTestRun.Completed(disputes[1], 'c')
        };

        var memory = CorpusTestRun.Ledger(tasks, first, (repairs, repaired), (disputes, resolved));
        var firstPackages = OneShardPackages(tasks);
        var repairPackages = OneShardPackages(repairs);
        var disputePackages = OneShardPackages(disputes);
        var blobs = await LedgerAsync(firstPackages, Answers(first), (repairPackages, Answers(repaired)), (disputePackages, Answers(resolved)));

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Select(task => task.TaskIndex), Is.EqualTo(new[] { 2, 3, 5 }));
            Assert.That(disputes.Select(task => task.TaskIndex), Is.EqualTo(new[] { 1, 4 }));
            Assert.That(Blobless(CorpusReportWriter.Write(blobs)), Is.EqualTo(Blobless(CorpusReportWriter.Write(memory))), "the report");
            Assert.That(CorpusIntegrityWriter.Write(blobs), Is.EqualTo(CorpusIntegrityWriter.Write(memory)), "the integrity document");
            Assert.That(blobs.Rejected, Is.EqualTo(memory.Rejected));
        });
    }

    [Test]
    public async Task M5InPackagesOfSeveralShardsEveryShardIsJudgedAloneTest()
    {
        // Two copies of a package of three shards; one copy lies about the second shard only.
        var tasks = CorpusTestRun.Tasks(6);
        var packages = Packages(tasks, perPackage: 3);
        var honest = tasks.Take(3).Select(task => CorpusTestRun.Completed(task, 'a')).ToArray();
        var liar = tasks.Take(3).Select(task => CorpusTestRun.Completed(task, task.TaskIndex == 1 ? '0' : 'a')).ToArray();

        var ledger = await LedgerAsync(packages, [Answer(packages[0], honest), Answer(packages[0], liar), Answer(packages[1], tasks.Skip(3).Select(task => CorpusTestRun.Completed(task)).ToArray())]);
        var shards = ledger.Shards.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(shards.Select(shard => shard.NeedsDispute), Is.EqualTo(new[] { false, true, false, false, false, false }));
            Assert.That(shards.Select(shard => shard.Copies), Is.EqualTo(new[] { 2, 0, 2, 1, 1, 1 }));
            Assert.That(ledger.DisputeTasks().Select(task => task.TaskIndex), Is.EqualTo(new[] { 1 }), "only the shard whose copies disagree runs again");
            Assert.That(ledger.SingleExecutions, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task M5ASecondAnswerOfOneMachineForAPackageDoesNotWinItsDisputeTest()
    {
        // The dispute round of two packages of two shards. The liar holds a copy of each and
        // answers the first in both its slots: the first has four answers, the second two.
        var tasks = CorpusTestRun.Tasks(4);
        var first = tasks.SelectMany(task => new[] { CorpusTestRun.Completed(task, 'a'), CorpusTestRun.Completed(task, '0') }).ToList();
        var packages = Packages(tasks, perPackage: 2);
        var copies = packages.SelectMany(package => new object?[]
        {
            Answer(package, CorpusTestPackages.Tasks(m_blobs, package).Select(task => CorpusTestRun.Completed(task, 'a')).ToArray()),
            Answer(package, CorpusTestPackages.Tasks(m_blobs, package).Select(task => CorpusTestRun.Completed(task, '0')).ToArray())
        }).ToList();
        var before = await LedgerAsync(packages, copies);
        var disputes = Packages(before.DisputeTasks(), perPackage: 2);
        var disputed = disputes.Select(package => CorpusTestPackages.Tasks(m_blobs, package)).ToList();

        object?[] Round(params (int Package, char Content)[] answers)
        {
            return answers.Select(answer => (object?)Answer(disputes[answer.Package], disputed[answer.Package].Select(task => CorpusTestRun.Completed(task, answer.Content)).ToArray())).ToArray();
        }

        // Two lies and one honest answer for the first package; the second is one answer short.
        var short1 = await LedgerAsync(packages, copies, null, (disputes, Round((0, '0'), (0, '0'), (0, 'a'), (1, 'a'), (1, 'a'))));
        // The same with every package answered three times: the two that agree are two machines.
        var full = await LedgerAsync(packages, copies, null, (disputes, Round((0, 'a'), (0, 'a'), (0, '0'), (1, 'a'), (1, 'a'), (1, 'a'))));

        Assert.Multiple(() =>
        {
            Assert.That(before.PendingDisputes, Is.EqualTo(4));
            Assert.That(short1.Shards.Take(2).Select(shard => shard.DisputeOutcome), Is.All.EqualTo(CorpusLedger.DISPUTE_SURPLUS_COPIES), "two values that may be one machine's are not two machines");
            Assert.That(short1.Shards.Take(2).Select(shard => shard.Accepted), Is.All.Null);
            Assert.That(short1.Shards.Skip(2).Select(shard => shard.Accepted!.ContentSha256), Is.All.EqualTo(CorpusTestRun.Hash('a')), "the first package has its three answers: the second's two are two machines'");
            Assert.That(full.Shards.Select(shard => shard.Accepted!.ContentSha256), Is.All.EqualTo(CorpusTestRun.Hash('a')));
            Assert.That(full.DisputesResolved, Is.EqualTo(4));
        });
    }

    #endregion

    #region Memory

    [Test]
    public async Task M5TheLedgerOfALongRunHoldsCountsNotShardsTest()
    {
        // 20,000 shards in packages of 20, each result with its statistics: megabytes of results
        // on disk. The ledger is built, read twice more for its documents, and keeps none of what
        // it judged: every shard and result it handed over is gone once nobody else holds it.
        // (The collector's own figures are printed, not asserted: they move by megabytes from one
        // platform to the next.)
        const int shards = 20_000;
        var pipeline = CorpusTestRun.Labelled();
        var packages = new List<CorpusTaskPackageData>();
        var answers = new List<object?>();
        long resultBytes = 0;

        for (var start = 0; start < shards; start += 20)
        {
            var tasks = Enumerable.Range(start, 20).Select(index => CorpusTestRun.Task(index, pipeline)).ToList();
            var package = CorpusTaskPackages.WriteAsync(m_blobs, tasks, 20 * CorpusPackagePlanner.MAX_PACKAGES).GetAwaiter().GetResult().Single();
            var answer = CorpusTestPackages.Answer(m_blobs, package.Ticket, tasks.Select(task => CorpusTestRun.Completed(task, languages: [("deu", 2), ("eng", 3)], drops: [("min_words", 7)])));
            packages.Add(package);
            answers.Add(answer);
            resultBytes += MemoryPack.MemoryPackSerializer.Serialize(CorpusTestRun.Completed(tasks[0], languages: [("deu", 2), ("eng", 3)], drops: [("min_words", 7)])).Length * 20L;
        }

        var before = GC.GetTotalMemory(forceFullCollection: true);
        var ledger = await LedgerAsync(packages, answers);
        var held = GC.GetTotalMemory(forceFullCollection: true) - before;

        using var report = new CountingStream();
        CorpusReportWriter.Write(ledger, report);
        using var integrity = new CountingStream();
        CorpusIntegrityWriter.Write(ledger, integrity);
        var after = GC.GetTotalMemory(forceFullCollection: true) - before;

        var (handed, seen) = Handed(ledger);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        TestContext.Out.WriteLine($"{shards} shards: results {resultBytes} bytes as values; the ledger holds about {held} bytes, {after} after the documents; report {report.Length} bytes, integrity {integrity.Length} bytes");

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards, Has.Count.EqualTo(shards));
            Assert.That(resultBytes, Is.GreaterThan(4_000_000));
            Assert.That(seen, Is.EqualTo(shards), "every shard was handed over");
            Assert.That(handed, Has.Count.EqualTo(2 * (shards / 1000)));
            Assert.That(handed.Count(reference => reference.IsAlive), Is.Zero, "the ledger keeps no shard and no result it judged: counts and references only");
            Assert.That(report.Length, Is.GreaterThan(10_000_000), "the report itself is long, and went to its stream as it was written");
            Assert.That(report.LargestWrite, Is.LessThan(1 << 20), "no write of the report is the report");
        });

        GC.KeepAlive(ledger);
    }

    #endregion

    #region Tools

    /// <summary>
    /// Reads the run once and keeps weak references to every thousandth shard and to its accepted
    /// result: what is still alive after a collection is what something other than this method holds.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (List<WeakReference> Handed, int Seen) Handed(CorpusLedger ledger)
    {
        var handed = new List<WeakReference>();
        var seen = 0;
        foreach (var shard in ledger.Shards)
        {
            if (seen++ % 1000 == 0)
            {
                handed.Add(new WeakReference(shard));
                handed.Add(new WeakReference(shard.Accepted));
            }
        }

        return (handed, seen);
    }

    private CorpusResultPackageData Answer(CorpusTaskPackageData package, params CorpusResultData[] results)
    {
        return CorpusTestPackages.Answer(m_blobs, package.Ticket, results);
    }

    /// <summary>One package a shard, as a job of up to 1,024 shards has them; remembers which package holds which shard's ticket.</summary>
    private List<object?> OneShardPackages(IReadOnlyList<CorpusTaskData> tasks)
    {
        var packages = CorpusTestPackages.Packages(m_blobs, tasks);
        for (var index = 0; index < tasks.Count; index++)
            m_packageOfShard[tasks[index].Ticket] = packages[index].Ticket;

        return packages.Cast<object?>().ToList();
    }

    /// <summary>Each result as an answer of its own to the one-shard package of its task: what a job of up to 1,024 shards returns.</summary>
    private List<object?> Answers(IEnumerable<CorpusResultData> results)
    {
        return results.Select(result => (object?)CorpusTestPackages.Answer(m_blobs, m_packageOfShard[result.Ticket], [result])).ToList();
    }

    /// <summary>Task packages of several shards each, as a job past 1,024 shards has them.</summary>
    private List<CorpusTaskPackageData> Packages(IReadOnlyList<CorpusTaskData> tasks, int perPackage)
    {
        return CorpusTaskPackages.WriteAsync(m_blobs, tasks, perPackage * CorpusPackagePlanner.MAX_PACKAGES).GetAwaiter().GetResult();
    }

    private async Task<CorpusLedger> LedgerAsync(IEnumerable<object?> packages, IEnumerable<object?> answers, (IEnumerable<object?> Packages, IEnumerable<object?> Answers)? repair = null, (IEnumerable<object?> Packages, IEnumerable<object?> Answers)? dispute = null)
    {
        var first = await CorpusRoundSourceBlobs.OpenAsync(CorpusRoundKind.First, packages, answers, m_blobs);
        var second = repair is { } repaired ? await CorpusRoundSourceBlobs.OpenAsync(CorpusRoundKind.Repair, repaired.Packages, repaired.Answers, m_blobs) : null;
        var third = dispute is { } disputed ? await CorpusRoundSourceBlobs.OpenAsync(CorpusRoundKind.Dispute, disputed.Packages, disputed.Answers, m_blobs) : null;

        return CorpusLedger.Build(CorpusTestRun.VERSION, first, second, third);
    }

    /// <summary>A report without its blob ids: two results made alike carry different random ids.</summary>
    private static string Blobless(byte[] report)
    {
        return System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(report), "\"(text|signatures)_blob\": \"[0-9a-f-]*\"", "\"$1_blob\": \"\"");
    }

    #endregion

    #region Types

    /// <summary>A blob service that remembers which blobs it was asked for.</summary>
    private sealed class CountingBlobs(CorpusTestBlobService inner) : IWitBlobService
    {
        public List<Guid> Asked { get; } = [];

        public Task<string> GetLocalPathAsync(Guid blobId)
        {
            lock (Asked)
                Asked.Add(blobId);

            return inner.GetLocalPathAsync(blobId);
        }

        public Task<Guid> UploadFileAsync(string localFilePath)
        {
            return inner.UploadFileAsync(localFilePath);
        }

        public Task<Guid> UploadBytesAsync(byte[] data, string fileName)
        {
            return inner.UploadBytesAsync(data, fileName);
        }
    }

    /// <summary>A stream that keeps nothing: how much was written, and the largest single write.</summary>
    private sealed class CountingStream : Stream
    {
        private long m_length;

        public int LargestWrite { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => m_length;

        public override long Position { get => m_length; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            m_length += count;
            LargestWrite = Math.Max(LargestWrite, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            m_length += buffer.Length;
            LargestWrite = Math.Max(LargestWrite, buffer.Length);
        }
    }

    #endregion
}
