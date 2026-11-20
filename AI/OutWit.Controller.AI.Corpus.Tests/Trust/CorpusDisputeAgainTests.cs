using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Docs;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Trust;

/// <summary>
/// One machine and a whole dispute round. A dispute's leading result lost as many values as all
/// the other packages of the round were short of, so a machine that held copies of two disputes
/// and answered them under no ticket of the round left every dispute of the round unsettled,
/// three honest machines that agreed included. A value under no ticket lent nothing to another
/// package and no longer counts as a second answer; a machine that answers nothing at all still
/// cannot be told from honest machines that fell silent, so the disputes it leaves with too few
/// copies, or with a lead that may be its second answers, go out once more on three machines.
/// 'a' is the honest result and '0' the forged one.
/// </summary>
[TestFixture]
public sealed class CorpusDisputeAgainTests
{
    #region Dispute Round Tests

    [Test]
    public void AValueUnderNoTicketOfTheRoundUnsettlesNoOtherDisputeTest()
    {
        // The audit's case: five disputes, the liar holds a copy of shards 0 and 1 and answers
        // both under no ticket of the round. Every dispute ended surplus_copies, the three that
        // three honest machines agreed on included.
        var run = Disputed(5);
        var answers = Answers(run.Disputes, 'a', 3).ToList();
        answers.Remove(answers.First(answer => ((CorpusResultData)answer!).TaskIndex == 0));
        answers.Remove(answers.First(answer => ((CorpusResultData)answer!).TaskIndex == 1));
        answers.Add(CorpusTestRun.Completed(CorpusTestRun.Reissue(run.Disputes[0]), '0'));
        answers.Add(CorpusTestRun.Completed(CorpusTestRun.Reissue(run.Disputes[1]), '0'));

        var ledger = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []), (run.Disputes, answers));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Select(shard => shard.Outcome), Is.All.EqualTo(CorpusShardOutcome.Accepted), "every dispute is settled by the honest machines");
            Assert.That(ledger.Shards.Select(shard => shard.Accepted!.ContentSha256), Is.All.EqualTo(CorpusTestRun.Hash('a')));
            Assert.That(ledger.Shards.Select(shard => shard.Copies), Is.EqualTo(new[] { 2, 2, 3, 3, 3 }));
            Assert.That(ledger.Rejected[CorpusLedger.REJECTED_UNKNOWN_TICKET], Is.EqualTo(2), "the two values are counted where they belong");
            Assert.That(ledger.PendingDisputesAgain, Is.Zero, "nothing to send out again");
        });
    }

    [Test]
    public void AMachineThatAnswersNothingLeavesTheRoundButNotTheDisputesTest()
    {
        // The same machine answers nothing for its two copies: the round cannot tell it from
        // honest machines that fell silent, and settles nothing by two. The five go out once more
        // on three machines drawn anew, and are settled there.
        var run = Disputed(5);
        var answers = Answers(run.Disputes, 'a', 3).ToList();
        answers.Remove(answers.First(answer => ((CorpusResultData)answer!).TaskIndex == 0));
        answers.Remove(answers.First(answer => ((CorpusResultData)answer!).TaskIndex == 1));

        var before = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []), (run.Disputes, answers));
        var again = before.EnumerateDisputeAgainTasks().ToList();
        var ledger = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []), (run.Disputes, answers), again: (again, Answers(again, 'a', 3)));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var disputes = integrity.RootElement.GetProperty("disputes").EnumerateArray().ToList();
        var summary = integrity.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(before.Shards.Select(shard => shard.DisputeOutcome), Is.All.EqualTo(CorpusLedger.DISPUTE_SURPLUS_COPIES), "one machine's silence costs the round");
            Assert.That(before.PendingDisputesAgain, Is.EqualTo(5));
            Assert.That(again.Select(task => task.TaskIndex), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(again.Select(task => task.Ticket), Is.Unique);
            Assert.That(again.Select(task => task.Ticket).Intersect(run.Disputes.Select(task => task.Ticket)), Is.Empty, "each under a new ticket");

            Assert.That(ledger.Shards.Select(shard => shard.Outcome), Is.All.EqualTo(CorpusShardOutcome.Accepted), "not the disputes");
            Assert.That(ledger.Shards.Select(shard => shard.Accepted!.ContentSha256), Is.All.EqualTo(CorpusTestRun.Hash('a')));
            Assert.That(ledger.DisputesAgain, Is.EqualTo(5));
            Assert.That(ledger.DisputesAgainResolved, Is.EqualTo(5));
            Assert.That(ledger.DisputeAgainRound.Packages, Is.EqualTo(5));

            Assert.That(disputes.Select(dispute => dispute.GetProperty("outcome").GetString()), Is.All.EqualTo(CorpusLedger.DISPUTE_RESOLVED));
            Assert.That(disputes.Select(dispute => dispute.GetProperty("again").GetProperty("after").GetString()), Is.All.EqualTo(CorpusLedger.DISPUTE_SURPLUS_COPIES));
            Assert.That(disputes.Select(dispute => dispute.GetProperty("again").GetProperty("copies").GetInt32()), Is.All.EqualTo(3));
            Assert.That(disputes.Select(dispute => dispute.GetProperty("again").GetProperty("results").GetArrayLength()), Is.All.EqualTo(3));
            Assert.That(summary.GetProperty("disputes_again").GetInt32(), Is.EqualTo(5));
            Assert.That(summary.GetProperty("disputes_again_resolved").GetInt32(), Is.EqualTo(5));
            Assert.That(summary.GetProperty("disputed").GetInt32(), Is.Zero);
            Assert.That(integrity.RootElement.GetProperty("rounds").GetProperty("dispute_again").GetProperty("packages").GetInt32(), Is.EqualTo(5));
        });
    }

    [Test]
    public void ADisputeThatCameBackWithTooFewCopiesGoesOutOnceMoreTest()
    {
        var run = Disputed(1);
        var before = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []), (run.Disputes, Answers(run.Disputes, 'a', 1)));
        var again = before.EnumerateDisputeAgainTasks().ToList();
        var ledger = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []), (run.Disputes, Answers(run.Disputes, 'a', 1)), again: (again, Answers(again, 'a', 2)));
        var shard = ledger.Shards.Single();

        Assert.Multiple(() =>
        {
            Assert.That(before.Shards.Single().DisputeOutcome, Is.EqualTo(CorpusLedger.DISPUTE_TOO_FEW_COPIES));
            Assert.That(again, Has.Count.EqualTo(1));
            Assert.That(shard.Outcome, Is.EqualTo(CorpusShardOutcome.Accepted));
            Assert.That(shard.AgainAfter, Is.EqualTo(CorpusLedger.DISPUTE_TOO_FEW_COPIES));
            Assert.That(shard.DisputeOutcome, Is.EqualTo(CorpusLedger.DISPUTE_RESOLVED));
            Assert.That(shard.Copies, Is.EqualTo(2));
        });
    }

    [Test]
    public void ADisputeWhoseCopiesDisagreeIsNotSentOutAgainTest()
    {
        // No majority is the copies' own word: the disagreement stands.
        var run = Disputed(1);
        var answers = new object?[]
        {
            CorpusTestRun.Completed(run.Disputes[0], 'a'), CorpusTestRun.Completed(run.Disputes[0], '0'), CorpusTestRun.Completed(run.Disputes[0], '1')
        };
        var ledger = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []), (run.Disputes, answers));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Single().DisputeOutcome, Is.EqualTo(CorpusLedger.DISPUTE_NO_MAJORITY));
            Assert.That(ledger.PendingDisputesAgain, Is.Zero);
            Assert.That(ledger.EnumerateDisputeAgainTasks(), Is.Empty);
        });
    }

    #endregion

    #region Second Round Tests

    [Test]
    public void ASecondAnswerOfOneMachineDoesNotWinTheSecondRoundEitherTest()
    {
        // Two disputes left without a settlement; in the second round the liar holds a copy of
        // each, spends its slot of shard 1 on shard 0 while an honest copy of shard 0 fell
        // silent: its forged result is there twice against one honest value.
        var run = Disputed(2);
        var first = Answers(run.Disputes, 'a', 2).ToList();
        var before = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []), (run.Disputes, first));
        var again = before.EnumerateDisputeAgainTasks().ToList();
        var second = new object?[]
        {
            CorpusTestRun.Completed(again[0], 'a'), CorpusTestRun.Completed(again[0], '0'), CorpusTestRun.Completed(again[0], '0'),
            CorpusTestRun.Completed(again[1], 'a'), CorpusTestRun.Completed(again[1], 'a')
        };
        var ledger = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []), (run.Disputes, first), again: (again, second));
        var shards = ledger.Shards.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(again, Has.Count.EqualTo(2));
            Assert.That(shards[0].Outcome, Is.EqualTo(CorpusShardOutcome.Disputed), "two values of one machine are not two machines");
            Assert.That(shards[0].Accepted, Is.Null);
            Assert.That(shards[0].DisputeOutcome, Is.EqualTo(CorpusLedger.DISPUTE_SURPLUS_COPIES));
            Assert.That(shards[1].Accepted!.ContentSha256, Is.EqualTo(CorpusTestRun.Hash('a')));
            Assert.That(ledger.PendingDisputesAgain, Is.Zero, "a dispute goes out once more, not again and again");
        });
    }

    [Test]
    public void TheIntegrityDocumentOfASecondRoundValidatesAgainstItsSchemaTest()
    {
        var run = Disputed(2);
        var first = Answers(run.Disputes, 'a', 2).ToList();
        var before = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []), (run.Disputes, first));
        var again = before.EnumerateDisputeAgainTasks().ToList();
        var ledger = CorpusTestRun.Ledger(run.Tasks, run.First, ([], []), (run.Disputes, first), again: (again, Answers(again, 'a', 3)));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(ledger));
        using var integritySchema = CorpusSchemaCheck.LoadSchema("corpus-integrity.schema.json");
        using var reportSchema = CorpusSchemaCheck.LoadSchema("corpus-report.schema.json");

        Assert.Multiple(() =>
        {
            Assert.That(CorpusSchemaCheck.Validate(integritySchema.RootElement, integrity.RootElement), Is.Empty);
            Assert.That(CorpusSchemaCheck.Validate(reportSchema.RootElement, report.RootElement), Is.Empty);
            Assert.That(integrity.RootElement.GetProperty("schema").GetString(), Is.EqualTo("corpus-integrity/8"));
            Assert.That(report.RootElement.GetProperty("schema").GetString(), Is.EqualTo("corpus-report/6"));
            Assert.That(report.RootElement.GetProperty("rounds").GetProperty("dispute_again").GetProperty("packages").GetInt32(), Is.EqualTo(2));
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

    /// <summary>The same completed result for every task, as many copies of each as given.</summary>
    private static IEnumerable<object?> Answers(IEnumerable<CorpusTaskData> tasks, char content, int copies)
    {
        return tasks.SelectMany(task => Enumerable.Range(0, copies).Select(_ => (object?)CorpusTestRun.Completed(task, content)));
    }

    #endregion
}
