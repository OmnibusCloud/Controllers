using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Reports;
using OutWit.Controller.AI.Corpus.Tests.Mock;
using OutWit.Controller.AI.Corpus.Trust;

namespace OutWit.Controller.AI.Corpus.Tests.Trust;

/// <summary>
/// Audit finding C-1, at the server's ledger: a node that lies about its version, its task index
/// or its content hash neither switches the comparison off nor answers for a shard it was not
/// given; copies that disagree are a dispute settled by the most machines; a shard with no
/// accepted result is in the report, with the reason.
/// </summary>
[TestFixture]
public sealed class CorpusLedgerC1Tests
{
    #region Version

    [Test]
    public void C1AForgedVersionDoesNotSwitchTheComparisonOffTest()
    {
        // The audit's first scenario: a false product for a sampled shard under another version.
        var tasks = CorpusTestRun.Tasks(1);
        var honest = CorpusTestRun.Completed(tasks[0], 'a');
        var liar = CorpusTestRun.Completed(tasks[0], '0', documentsOut: 1);
        liar.ControllerVersion = "0.3.1";

        var ledger = CorpusTestRun.Ledger(tasks, [honest, liar]);
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var summary = integrity.RootElement.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Single().Accepted, Is.SameAs(honest), "the result of another version is dropped, the honest one stands");
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_VERSION] = 1 }));
            Assert.That(summary.GetProperty("version_skew").GetInt32(), Is.Zero, "no pair is set aside for its version any more");
            Assert.That(summary.GetProperty("rejected_results").GetInt32(), Is.EqualTo(1));
            Assert.That(integrity.RootElement.GetProperty("rejected").GetProperty("version").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("incomplete").GetInt32(), Is.EqualTo(1), "the pair is counted, with a side that is no result");
        });
    }

    [Test]
    public void C1AShardWhoseOnlyResultClaimsAnotherVersionIsRepairedTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        var liar = CorpusTestRun.Completed(tasks[1], '0');
        liar.ControllerVersion = "9.9.9";

        var first = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), liar]);
        var repairs = first.RepairTasks();

        Assert.Multiple(() =>
        {
            Assert.That(repairs.Select(task => task.TaskIndex), Is.EqualTo(new[] { 1 }));
            Assert.That(repairs[0].Ticket, Is.Not.EqualTo(tasks[1].Ticket), "a new round is a new ticket");
            Assert.That(CorpusTickets.IsWellFormed(repairs[0].Ticket), Is.True);
        });

        var repaired = CorpusTestRun.Completed(repairs[0], 'c');
        var ledger = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), liar], (repairs, [repaired]));

        Assert.That(ledger.Shards.Last().Accepted, Is.SameAs(repaired));
    }

    #endregion

    #region Content hash

    [TestCase('0')]
    [TestCase('f')]
    public void C1TheContentHashNeverChoosesTheExecutionTest(char forged)
    {
        // The audit's second scenario: of two completed executions the lower hash stood for the
        // shard. Now they disagree, and nothing a node says settles it.
        var tasks = CorpusTestRun.Tasks(1);
        var honest = CorpusTestRun.Completed(tasks[0], 'a');
        var liar = CorpusTestRun.Completed(tasks[0], forged);

        foreach (var order in new[] { new[] { honest, liar }, new[] { liar, honest } })
        {
            var ledger = CorpusTestRun.Ledger(tasks, order);
            var shard = ledger.Shards.Single();

            Assert.Multiple(() =>
            {
                Assert.That(shard.Accepted, Is.Null);
                Assert.That(shard.NeedsDispute, Is.True);
                Assert.That(shard.Outcome, Is.EqualTo(CorpusShardOutcome.Disputed), "without a dispute round the shard has no product");
                Assert.That(ledger.DisputeTasks().Select(task => task.TaskIndex), Is.EqualTo(new[] { 0 }));
            });
        }
    }

    [Test]
    public void C1AnEmptyContentHashIsNotAResultTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var honest = CorpusTestRun.Completed(tasks[0], 'a');
        var liar = CorpusTestRun.Completed(tasks[0], 'a');
        liar.ContentSha256 = "";

        var ledger = CorpusTestRun.Ledger(tasks, [liar, honest]);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Single().Accepted, Is.SameAs(honest));
            Assert.That(ledger.Rejected.Keys, Is.EqualTo(new[] { CorpusLedger.REJECTED_UNREADABLE }));
        });
    }

    #endregion

    #region Task index

    [Test]
    public void C1AResultUnderAForeignTaskIndexDoesNotReplaceTheHonestOneTest()
    {
        // The audit's third scenario: five shards, the node that holds shard 4 returns its result
        // as shard 2's, with a URL of its own and an empty hash.
        var tasks = CorpusTestRun.Tasks(5);
        var honest = tasks.Take(4).Select(task => CorpusTestRun.Completed(task, (char)('a' + task.TaskIndex))).ToList();
        var forged = CorpusTestRun.Completed(tasks[4], '0', documentsOut: 123456);
        forged.TaskIndex = 2;
        forged.SourceUrl = "https://attacker.example/anything";

        var ledger = CorpusTestRun.Ledger(tasks, honest.Append(forged));
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(ledger));
        var shards = report.RootElement.GetProperty("shards").EnumerateArray().ToList();
        var text = Encoding.UTF8.GetString(CorpusReportWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("tasks").GetInt32(), Is.EqualTo(5), "every shard of the task list is in the report");
            Assert.That(shards[2].GetProperty("url").GetString(), Is.EqualTo(tasks[2].Source!.Url), "the URL is the task's");
            Assert.That(shards[2].GetProperty("content_sha256").GetString(), Is.EqualTo(CorpusTestRun.Hash('c')));
            Assert.That(shards[2].GetProperty("stats").GetProperty("documents_out").GetInt32(), Is.EqualTo(5));
            Assert.That(shards[4].GetProperty("verdict").GetString(), Is.EqualTo(CorpusReportWriter.VERDICT_NO_RESULT), "the liar's own shard has no result, and the report says so");
            Assert.That(shards[4].GetProperty("reason").GetString(), Is.EqualTo(CorpusLedger.NO_RESULT_NO_ANSWER));
            Assert.That(text, Does.Not.Contain("attacker.example"));
            Assert.That(ledger.Rejected, Is.EqualTo(new Dictionary<string, int> { [CorpusLedger.REJECTED_CONTENT] = 1 }));
            Assert.That(ledger.RepairTasks().Select(task => task.TaskIndex), Is.EqualTo(new[] { 4 }), "and it is sent out once more");
        });
    }

    [Test]
    public void C1AResultUnderATicketNobodyGaveOutIsDroppedTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        var stranger = CorpusTestRun.Completed(tasks[1], '0');
        stranger.Ticket = CorpusTickets.New();
        var guessed = CorpusTestRun.Completed(tasks[1], '0');
        guessed.Ticket = "";

        var ledger = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), stranger, guessed, "not a result", null]);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Rejected[CorpusLedger.REJECTED_UNKNOWN_TICKET], Is.EqualTo(2));
            Assert.That(ledger.Rejected[CorpusLedger.REJECTED_UNREADABLE], Is.EqualTo(2));
            Assert.That(ledger.RejectedResults, Is.EqualTo(4));
            Assert.That(ledger.Shards.Last().Reason, Is.EqualTo(CorpusLedger.NO_RESULT_NO_ANSWER));
        });
    }

    [Test]
    public void C1ATicketOfAnotherRoundAnswersNothingTest()
    {
        // A node that held the shard in the first round cannot answer for it in the dispute round.
        var tasks = CorpusTestRun.Tasks(1);
        var honest = CorpusTestRun.Completed(tasks[0], 'a');
        var liar = CorpusTestRun.Completed(tasks[0], '0');
        var disputes = CorpusTestRun.Ledger(tasks, [honest, liar]).DisputeTasks();

        var replayed = CorpusTestRun.Completed(tasks[0], '0');
        var again = CorpusTestRun.Completed(tasks[0], '0');
        var ledger = CorpusTestRun.Ledger(tasks, [honest, liar], ([], []),
            (disputes, [replayed, again, CorpusTestRun.Completed(disputes[0], 'a'), CorpusTestRun.Completed(disputes[0], 'a')]));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Rejected[CorpusLedger.REJECTED_UNKNOWN_TICKET], Is.EqualTo(2), "the first round's ticket is nobody's in the dispute round");
            Assert.That(ledger.Shards.Single().Accepted!.ContentSha256, Is.EqualTo(CorpusTestRun.Hash('a')));
        });
    }

    #endregion

    #region Disputes

    [Test]
    public void C1AgreeingCopiesAreAcceptedAndCountedAsTwoTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        var ledger = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Completed(tasks[1])]);

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Select(shard => shard.Copies), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(ledger.SingleExecutions, Is.EqualTo(1), "the unsampled shard is one machine's word, and the ledger says so");
            Assert.That(ledger.Disputes, Is.Zero);
            Assert.That(ledger.RepairTasks(), Is.Empty);
            Assert.That(ledger.DisputeTasks(), Is.Empty);
        });
    }

    [Test]
    public void C1TheMostMachinesSettleADisputeTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var first = new object?[] { CorpusTestRun.Completed(tasks[0], 'a'), CorpusTestRun.Completed(tasks[0], '0') };
        var disputes = CorpusTestRun.Ledger(tasks, first).DisputeTasks();

        var ledger = CorpusTestRun.Ledger(tasks, first, ([], []),
            (disputes, [CorpusTestRun.Completed(disputes[0], '0'), CorpusTestRun.Completed(disputes[0], 'a'), CorpusTestRun.Completed(disputes[0], 'a')]));
        var shard = ledger.Shards.Single();

        Assert.Multiple(() =>
        {
            Assert.That(shard.Outcome, Is.EqualTo(CorpusShardOutcome.Accepted));
            Assert.That(shard.Accepted!.ContentSha256, Is.EqualTo(CorpusTestRun.Hash('a')));
            Assert.That(shard.Copies, Is.EqualTo(2));
            Assert.That(ledger.Disputes, Is.EqualTo(1));
            Assert.That(ledger.DisputesResolved, Is.EqualTo(1));
        });
    }

    [TestCase("a0", CorpusLedger.DISPUTE_NO_MAJORITY)]
    [TestCase("a0b", CorpusLedger.DISPUTE_NO_MAJORITY)]
    [TestCase("a", CorpusLedger.DISPUTE_TOO_FEW_COPIES)]
    [TestCase("", CorpusLedger.DISPUTE_TOO_FEW_COPIES)]
    public void C1WithoutAMajorityOfTwoTheShardIsDisputedTest(string answers, string outcome)
    {
        // A tie, three different answers, one answer, none: no result returned by the most
        // machines, two at least.
        var tasks = CorpusTestRun.Tasks(1);
        var first = new object?[] { CorpusTestRun.Completed(tasks[0], 'a'), CorpusTestRun.Completed(tasks[0], '0') };
        var disputes = CorpusTestRun.Ledger(tasks, first).DisputeTasks();

        var ledger = CorpusTestRun.Ledger(tasks, first, ([], []), (disputes, answers.Select(content => (object?)CorpusTestRun.Completed(disputes[0], content))));
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(ledger));
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));
        var shard = report.RootElement.GetProperty("shards")[0];

        Assert.Multiple(() =>
        {
            Assert.That(integrity.RootElement.GetProperty("disputes")[0].GetProperty("outcome").GetString(), Is.EqualTo(outcome));
            Assert.That(integrity.RootElement.GetProperty("disputes")[0].GetProperty("copies").GetInt32(), Is.EqualTo(answers.Length));
            Assert.That(shard.GetProperty("verdict").GetString(), Is.EqualTo(CorpusReportWriter.VERDICT_DISPUTED));
            Assert.That(shard.GetProperty("text_blob").GetString(), Is.Empty, "a disputed shard has no product");
            Assert.That(shard.GetProperty("stats").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("documents_out").GetInt64(), Is.Zero, "and is out of the counts");
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("disputes").GetInt32(), Is.EqualTo(1));
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("disputes_resolved").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public void C1ANodeFaultInTheDisputeRoundIsNoVoteTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var first = new object?[] { CorpusTestRun.Completed(tasks[0], 'a'), CorpusTestRun.Completed(tasks[0], '0') };
        var disputes = CorpusTestRun.Ledger(tasks, first).DisputeTasks();

        var ledger = CorpusTestRun.Ledger(tasks, first, ([], []),
            (disputes, [CorpusTestRun.Fault(disputes[0]), CorpusTestRun.Completed(disputes[0], 'a'), CorpusTestRun.Completed(disputes[0], 'a')]));

        Assert.That(ledger.Shards.Single().Accepted!.ContentSha256, Is.EqualTo(CorpusTestRun.Hash('a')));
    }

    [Test]
    public void C1MoreAnswersThanTheFirstRoundSentOutAreADisputeTest()
    {
        // Three agreeing answers where at most two were sent: one machine answered twice.
        var tasks = CorpusTestRun.Tasks(1);
        var ledger = CorpusTestRun.Ledger(tasks, Enumerable.Repeat('0', 3).Select(content => (object?)CorpusTestRun.Completed(tasks[0], content)));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Single().NeedsDispute, Is.True);
            Assert.That(ledger.Shards.Single().Accepted, Is.Null);
        });
    }

    [Test]
    public void C1ADifferentVerdictIsADisagreementTest()
    {
        // One copy completed, the other says the origin refused it: nothing a node says decides.
        var tasks = CorpusTestRun.Tasks(1);
        var ledger = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Completed(tasks[0]), CorpusTestRun.Verdict(tasks[0], CorpusShardVerdict.FetchFailed, "http_404")]);

        Assert.That(ledger.Shards.Single().NeedsDispute, Is.True);
    }

    #endregion

    #region Repairs

    [Test]
    public void C1AFaultedShardIsSentOutOnceMoreAndThenHasNoResultTest()
    {
        var tasks = CorpusTestRun.Tasks(3);
        var first = new object?[]
        {
            CorpusTestRun.Completed(tasks[0]),
            CorpusTestRun.Fault(tasks[1], CorpusResultCodes.FAULT_DISK),
            CorpusTestRun.Fault(tasks[2], CorpusResultCodes.FAULT_PDF_BACKSTOP)
        };
        var repairs = CorpusTestRun.Ledger(tasks, first).RepairTasks();

        Assert.That(repairs.Select(task => task.TaskIndex), Is.EqualTo(new[] { 1, 2 }));

        var ledger = CorpusTestRun.Ledger(tasks, first, (repairs, [CorpusTestRun.Completed(repairs[0], 'c'), CorpusTestRun.Fault(repairs[1], CorpusResultCodes.FAULT_NETWORK)]));
        using var report = JsonDocument.Parse(CorpusReportWriter.Write(ledger));
        var shards = report.RootElement.GetProperty("shards").EnumerateArray().ToList();
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(shards[1].GetProperty("verdict").GetString(), Is.EqualTo("Completed"));
            Assert.That(shards[1].GetProperty("faults").EnumerateArray().Select(fault => fault.GetString()), Is.EqualTo(new[] { "disk" }));
            Assert.That(shards[2].GetProperty("verdict").GetString(), Is.EqualTo(CorpusReportWriter.VERDICT_NO_RESULT));
            Assert.That(shards[2].GetProperty("reason").GetString(), Is.EqualTo(CorpusLedger.NO_RESULT_NODE_FAULT));
            Assert.That(shards[2].GetProperty("faults").EnumerateArray().Select(fault => fault.GetString()), Is.EqualTo(new[] { "network", "pdf_backstop" }));
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("verdicts").GetProperty("NoResult").GetInt32(), Is.EqualTo(1));
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("repairs").GetInt32(), Is.EqualTo(2));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("node_faults").GetInt32(), Is.EqualTo(3));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("repaired").GetInt32(), Is.EqualTo(1));
            Assert.That(integrity.RootElement.GetProperty("no_result")[0].GetProperty("index").GetInt32(), Is.EqualTo(2));
            Assert.That(ledger.RepairTasks(), Is.Empty, "a shard is repaired once");
        });
    }

    [Test]
    public void C1AFaultIsNeverAVerdictInTheReportTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var ledger = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Fault(tasks[0])], ([], []));
        var report = Encoding.UTF8.GetString(CorpusReportWriter.Write(ledger));

        Assert.That(report, Does.Not.Contain("NodeFault"));
    }

    [Test]
    public void C1ACopyThatFaultedLeavesTheOtherStandingTest()
    {
        var tasks = CorpusTestRun.Tasks(1);
        var honest = CorpusTestRun.Completed(tasks[0]);
        var ledger = CorpusTestRun.Ledger(tasks, [CorpusTestRun.Fault(tasks[0]), honest]);
        using var integrity = JsonDocument.Parse(CorpusIntegrityWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            Assert.That(ledger.Shards.Single().Accepted, Is.SameAs(honest));
            Assert.That(ledger.Shards.Single().Copies, Is.EqualTo(1));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("incomplete").GetInt32(), Is.EqualTo(1));
            Assert.That(integrity.RootElement.GetProperty("summary").GetProperty("compared").GetInt32(), Is.Zero);
            Assert.That(integrity.RootElement.GetProperty("pairs")[0].GetProperty("primary").GetProperty("kind").GetString(), Is.EqualTo("fault"));
        });
    }

    #endregion

    #region Documents

    [Test]
    public void C1NoTicketIsWrittenToADocumentTest()
    {
        var tasks = CorpusTestRun.Tasks(3);
        var first = new object?[] { CorpusTestRun.Completed(tasks[0], 'a'), CorpusTestRun.Completed(tasks[0], '0'), CorpusTestRun.Fault(tasks[1]), CorpusTestRun.Completed(tasks[2]) };
        var before = CorpusTestRun.Ledger(tasks, first);
        var repairs = before.RepairTasks();
        var disputes = before.DisputeTasks();
        var ledger = CorpusTestRun.Ledger(tasks, first, (repairs, [CorpusTestRun.Completed(repairs[0])]), (disputes, [CorpusTestRun.Completed(disputes[0], 'a'), CorpusTestRun.Completed(disputes[0], 'a')]));

        var documents = Encoding.UTF8.GetString(CorpusReportWriter.Write(ledger)) + Encoding.UTF8.GetString(CorpusIntegrityWriter.Write(ledger));

        Assert.Multiple(() =>
        {
            foreach (var task in tasks.Concat(repairs).Concat(disputes))
                Assert.That(documents, Does.Not.Contain(task.Ticket));
            // The one place the word stands is the name of a rejection reason, written with its count.
            Assert.That(documents.Replace($"\"{CorpusLedger.REJECTED_UNKNOWN_TICKET}\"", ""), Does.Not.Contain("ticket"));
        });
    }

    [Test]
    public void C1TheSameRunGivesTheSameDocumentsTest()
    {
        var tasks = CorpusTestRun.Tasks(2);
        var first = new object?[] { CorpusTestRun.Completed(tasks[1], 'b'), CorpusTestRun.Completed(tasks[0], 'a'), CorpusTestRun.Completed(tasks[0], 'a') };

        Assert.Multiple(() =>
        {
            Assert.That(CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks, first)), Is.EqualTo(CorpusReportWriter.Write(CorpusTestRun.Ledger(tasks, first))));
            Assert.That(CorpusIntegrityWriter.Write(CorpusTestRun.Ledger(tasks, first)), Is.EqualTo(CorpusIntegrityWriter.Write(CorpusTestRun.Ledger(tasks, first))));
        });
    }

    #endregion
}
