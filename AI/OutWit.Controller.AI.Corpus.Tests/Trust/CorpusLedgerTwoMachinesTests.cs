using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Trust;

/// <summary>
/// Two machines, not two values: the server sees the values a round returned and not the machines
/// that returned them. The platform keeps one value for each task of a node's batch (Grid 1.3.0),
/// so a machine cannot add values; it can still spend the slot of one task it holds on the ticket
/// of another, which leaves the one short. These tests hold the ruling to that: a value counts
/// towards two machines only as far as it cannot be such a second answer, and in the dispute
/// round a ticket with more values than the round asked for settles nothing. In every scenario
/// 'a' is the honest result and '0' the forged one.
/// </summary>
[TestFixture]
public sealed class CorpusLedgerTwoMachinesTests
{
    #region Disputes

    [Test]
    public void ASecondAnswerOfOneMachineDoesNotWinADisputeTest()
    {
        // Two disputed shards on three machines. The liar holds a copy of each and spends its
        // slot of shard 1 on shard 0: its forged result is there twice, against one honest value.
        var run = Disputed(2);
        var ledger = Settle(run, (0, 'a'), (0, '0'), (0, '0'), (1, 'a'), (1, 'a'));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var disputes = integrity.RootElement.GetProperty("disputes");
        var shards = ledger.Shards.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(shards[0].Outcome, Is.EqualTo(CorpusShardOutcome.Disputed), "two values of one machine are not two machines");
            Assert.That(shards[0].Accepted, Is.Null);
            Assert.That(disputes[0].GetProperty("outcome").GetString(), Is.EqualTo(CorpusLedger.DISPUTE_SURPLUS_COPIES));
            Assert.That(disputes[0].GetProperty("copies").GetInt32(), Is.EqualTo(3));
            Assert.That(disputes[0].GetProperty("agreeing").GetInt32(), Is.EqualTo(2));
            Assert.That(disputes[0].TryGetProperty("accepted", out _), Is.False);

            // Shard 0 has its three values, so nobody had a slot to spend on shard 1: its two are two machines'.
            Assert.That(shards[1].Accepted!.ContentSha256, Is.EqualTo(CorpusTestRun.Hash('a')));
            Assert.That(shards[1].Copies, Is.EqualTo(2));
            Assert.That(disputes[1].GetProperty("outcome").GetString(), Is.EqualTo(CorpusLedger.DISPUTE_RESOLVED));
            Assert.That(ledger.RejectedResults, Is.Zero, "nothing was dropped: the values are counted, and counted for less");
        });
    }

    [Test]
    public void ARoundShortOfAnswersSettlesNothingByTwoTest()
    {
        // Two disputed shards, two values each and one lost each. Either pair may be one
        // machine's: the one that held a copy of both and answered one of them twice.
        var run = Disputed(2);
        var ledger = Settle(run, (0, '0'), (0, '0'), (1, 'a'), (1, 'a'));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var disputes = integrity.RootElement.GetProperty("disputes").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Select(shard => shard.Outcome), Is.All.EqualTo(CorpusShardOutcome.Disputed));
            Assert.That(disputes.Select(dispute => dispute.GetProperty("outcome").GetString()), Is.All.EqualTo(CorpusLedger.DISPUTE_SURPLUS_COPIES));
            Assert.That(disputes.Select(dispute => dispute.GetProperty("agreeing").GetInt32()), Is.All.EqualTo(2));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("disputes_resolved").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public void OneDisputedShardIsSettledByTheTwoCopiesThatCameBackTest()
    {
        // The only shard of the round: no machine holds another ticket whose slot it could spend
        // here, so the two values that came back are two machines'.
        var run = Disputed(1);
        var ledger = Settle(run, (0, 'a'), (0, 'a'));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var dispute = integrity.RootElement.GetProperty("disputes")[0];
        var shard = ledger.Shards.Single();

        Assert.Multiple(() =>
        {
            Assert.That(shard.Outcome, Is.EqualTo(CorpusShardOutcome.Accepted));
            Assert.That(shard.Accepted!.ContentSha256, Is.EqualTo(CorpusTestRun.Hash('a')));
            Assert.That(shard.Copies, Is.EqualTo(2));
            Assert.That(dispute.GetProperty("outcome").GetString(), Is.EqualTo(CorpusLedger.DISPUTE_RESOLVED));
            Assert.That(dispute.GetProperty("first_copies").GetInt32(), Is.EqualTo(2));
            Assert.That(dispute.GetProperty("copies").GetInt32(), Is.EqualTo(2));
            Assert.That(dispute.GetProperty("agreeing").GetInt32(), Is.EqualTo(2));
        });
    }

    [Test]
    public void ThreeAgreeingAnswersStillSettleAShardWhenAnotherShardOfTheRoundLostOneCopyTest()
    {
        // Shard 1 is one value short, so one of shard 0's three may be a machine's second: two
        // of them are two machines' still, and nothing stands against them.
        var run = Disputed(2);
        var ledger = Settle(run, (0, 'a'), (0, 'a'), (0, 'a'), (1, 'a'), (1, 'a'));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var disputes = integrity.RootElement.GetProperty("disputes").EnumerateArray().ToList();
        var shards = ledger.Shards.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(shards.Select(shard => shard.Outcome), Is.All.EqualTo(CorpusShardOutcome.Accepted), "a copy lost elsewhere does not unsettle three machines that agree");
            Assert.That(shards.Select(shard => shard.Copies), Is.EqualTo(new[] { 3, 2 }));
            Assert.That(disputes.Select(dispute => dispute.GetProperty("outcome").GetString()), Is.All.EqualTo(CorpusLedger.DISPUTE_RESOLVED));
            Assert.That(disputes.Select(dispute => dispute.GetProperty("agreeing").GetInt32()), Is.EqualTo(new[] { 3, 2 }));
            Assert.That(ledger.DisputesResolved, Is.EqualTo(2));
        });
    }

    [Test]
    public void TwoAgainstOneIsNotSettledWhenAnotherShardOfTheRoundLostACopyTest()
    {
        // Shard 0 came back as two against one while shard 1 is a value short: one of the two
        // may be the second answer of the machine that left shard 1 short, whichever side it is on.
        var run = Disputed(2);
        var ledger = Settle(run, (0, 'a'), (0, 'a'), (0, '0'), (1, 'a'), (1, 'a'));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.First().Outcome, Is.EqualTo(CorpusShardOutcome.Disputed));
            Assert.That(integrity.RootElement.GetProperty("disputes")[0].GetProperty("outcome").GetString(), Is.EqualTo(CorpusLedger.DISPUTE_SURPLUS_COPIES));
            Assert.That(ledger.Shards.Last().Outcome, Is.EqualTo(CorpusShardOutcome.Accepted), "shard 0 has its three values: shard 1's two are two machines'");
        });
    }

    [Test]
    public void MoreThanThreeAnswersToADisputeSettleNothingAndAreRejectedAsSurplusTest()
    {
        // Three machines were asked and four values came back: which three are the machines'
        // cannot be told, so none is taken.
        var run = Disputed(1);
        var ledger = Settle(run, (0, '0'), (0, '0'), (0, '0'), (0, '0'));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var dispute = integrity.RootElement.GetProperty("disputes")[0];

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Single().Outcome, Is.EqualTo(CorpusShardOutcome.Disputed));
            Assert.That(ledger.Shards.Single().Accepted, Is.Null);
            Assert.That(dispute.GetProperty("outcome").GetString(), Is.EqualTo(CorpusLedger.DISPUTE_TOO_FEW_COPIES));
            Assert.That(dispute.GetProperty("copies").GetInt32(), Is.Zero);
            Assert.That(dispute.GetProperty("agreeing").GetInt32(), Is.Zero);
            Assert.That(dispute.GetProperty("results").EnumerateArray().Select(side => side.GetProperty("kind").GetString()), Is.All.EqualTo("rejected"));
            Assert.That(dispute.GetProperty("results").EnumerateArray().Select(side => side.GetProperty("code").GetString()), Is.All.EqualTo(CorpusLedger.REJECTED_SURPLUS));
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_SURPLUS] = 4 }));
            Assert.That(integrity.RootElement.GetProperty("rejected").GetProperty("surplus").GetInt32(), Is.EqualTo(4));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("rejected_results").GetInt32(), Is.EqualTo(4));
        });
    }

    [Test]
    public void AValueDroppedOnArrivalDoesNotCrowdADisputeTest()
    {
        // A fourth value that is no result is dropped for what it is; the three that were taken
        // are not more than three.
        var run = Disputed(1);
        var unreadable = CorpusTestRun.Completed(run.Disputes[0], 'a');
        unreadable.ContentSha256 = "";

        var ledger = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []),
            (run.Disputes, [unreadable, CorpusTestRun.Completed(run.Disputes[0], 'a'), CorpusTestRun.Completed(run.Disputes[0], 'a'), CorpusTestRun.Completed(run.Disputes[0], '0')]));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Single().Accepted!.ContentSha256, Is.EqualTo(CorpusTestRun.Hash('a')));
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_UNREADABLE] = 1 }));
        });
    }

    #endregion

    #region Repairs

    [Test]
    public void TwoAnswersToARepairThatAgreeAreAPairTest()
    {
        // The repair round sends a shard to two machines when the run samples, so two values
        // under its ticket are what it asked for: agreeing, they are accepted as a pair. (Up to
        // 0.6.0 the round asked one machine, and two values left the shard without a result.)
        var tasks = CorpusTestRun.Tasks(1);
        var repairs = CorpusTestRun.Ledger(tasks, []).RepairTasks();

        var ledger = CorpusTestRun.Ledger(tasks, [], (repairs, [CorpusTestRun.Completed(repairs[0], 'a'), CorpusTestRun.Completed(repairs[0], 'a')]));
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(ledger));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var shard = report.RootElement.GetProperty("shards")[0];
        var summary = integrity.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(shard.GetProperty("verdict").GetString(), Is.EqualTo("Completed"));
            Assert.That(shard.GetProperty("content_sha256").GetString(), Is.EqualTo(CorpusTestRun.Hash('a')));
            Assert.That(shard.GetProperty("executions").GetInt32(), Is.EqualTo(2));
            Assert.That(ledger.RejectedResults, Is.Zero, "nothing is surplus");
            Assert.That(ledger.DisputeTasks(), Is.Empty);
            Assert.That(ledger.SingleExecutions, Is.Zero);
            Assert.That(summary.GetProperty("repaired").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("repair_pairs").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("repair_compared").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("repair_mismatches").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.Zero, "the first round's sample is counted apart");
            Assert.That(integrity.RootElement.GetProperty("pairs")[0].GetProperty("round").GetString(), Is.EqualTo("repair"));
            Assert.That(integrity.RootElement.GetProperty("pairs")[0].GetProperty("match").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void ARepairOfOneResultAndOneFaultIsOneMachinesWordTest()
    {
        // Two machines were asked; one returned the shard as a fault of its own. The other's
        // result stands, as one machine's word, and both faults are named.
        var tasks = CorpusTestRun.Tasks(1);
        var first = new object?[] { CorpusTestRun.Fault(tasks[0]) };
        var repairs = CorpusTestRun.Ledger(tasks, first).RepairTasks();

        var ledger = CorpusTestRun.Ledger(tasks, first, (repairs, [CorpusTestRun.Completed(repairs[0], 'a'), CorpusTestRun.Fault(repairs[0], CorpusResultCodes.FAULT_NETWORK)]));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var shard = ledger.Shards.Single();

        Assert.Multiple(() =>
        {
            Assert.That(shard.Outcome, Is.EqualTo(CorpusShardOutcome.Accepted));
            Assert.That(shard.Copies, Is.EqualTo(1));
            Assert.That(shard.Faults, Is.EqualTo(new[] { CorpusResultCodes.FAULT_DISK, CorpusResultCodes.FAULT_NETWORK }));
            Assert.That(ledger.RejectedResults, Is.Zero);
            Assert.That(ledger.NodeFaults, Is.EqualTo(2));
            Assert.That(ledger.SingleExecutions, Is.EqualTo(1));
            Assert.That(ledger.DisputeTasks(), Is.Empty);
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("repair_pairs").GetInt32(), Is.EqualTo(1));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("repair_compared").GetInt32(), Is.Zero, "a pair with a fault is not compared");
            Assert.That(integrity.RootElement.GetProperty("pairs")[0].GetProperty("incomplete").GetBoolean(), Is.True);
        });
    }

    [Test]
    public void AValueDroppedOnArrivalDoesNotCrowdARepairTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var repairs = CorpusTestRun.Ledger(tasks, []).RepairTasks();
        var foreign = CorpusTestRun.Completed(repairs[0], '0');
        foreign.ControllerVersion = "9.9.9";
        var honest = CorpusTestRun.Completed(repairs[0], 'a');

        var ledger = CorpusTestRun.Ledger(tasks, [], (repairs, [foreign, honest]));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Single().Accepted, Is.SameAs(honest));
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_VERSION] = 1 }));
        });
    }

    #endregion

    #region Tools

    /// <summary>A run whose every shard came back from the first round as two copies that disagree.</summary>
    private static (List<CorpusTaskData> Tasks, object?[] First, List<CorpusTaskData> Disputes) Disputed(int shards)
    {
        var tasks = CorpusTestRun.Tasks(shards);
        var first = tasks.SelectMany(task => new object?[] { CorpusTestRun.Completed(task, 'a'), CorpusTestRun.Completed(task, '0') }).ToArray();

        return (tasks, first, CorpusTestRun.Ledger(tasks, first).DisputeTasks());
    }

    /// <summary>The run's ledger after a dispute round that returned these completed results: the shard each is for, and its content.</summary>
    private static CorpusLedger Settle((List<CorpusTaskData> Tasks, object?[] First, List<CorpusTaskData> Disputes) run, params (int Shard, char Content)[] answers)
    {
        return CorpusTestRun.Ledger(run.Tasks, run.First, ([], []),
            (run.Disputes, answers.Select(answer => (object?)CorpusTestRun.Completed(run.Disputes[answer.Shard], answer.Content))));
    }

    #endregion
}
