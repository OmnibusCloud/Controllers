using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;
using OutWit.Controller.AI.Verify.Tests.Settlement;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The integrity document of a settled run: executions paired by task index, agreement on the
/// fields that are compared, a task executed once making no pair and counted as resting on one
/// execution, the counts of what was refused and repaired, and no machine named.
/// </summary>
[TestFixture]
public sealed class VerifyIntegrityWriterTests
{
    #region Fields

    private VerifySettleRig m_rig = null!;

    #endregion

    #region Setup

    [SetUp]
    public void SetUp()
    {
        m_rig = new VerifySettleRig();
    }

    [TearDown]
    public void TearDown()
    {
        m_rig.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public async Task AgreeingPairsMatchTest()
    {
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0, 1));
        var returned = new[] { await m_rig.AnswerAsync(issued[0], Answer), await m_rig.AnswerAsync(issued[0], Answer) };

        var run = await m_rig.SettleAsync(issued, returned);
        using var document = JsonDocument.Parse(run.Integrity);
        var summary = document.RootElement.GetProperty("summary");
        var pairs = document.RootElement.GetProperty("pairs").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("schema").GetString(), Is.EqualTo(OutWit.Controller.AI.Verify.Tasksets.VerifyIntegrityWriter.SCHEMA));
            Assert.That(summary.GetProperty("tasks").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.Zero);
            Assert.That(summary.GetProperty("agreed").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.GetProperty("single_execution").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("rejected_results").GetInt32(), Is.Zero);
            Assert.That(pairs.All(pair => pair.GetProperty("match").GetBoolean()), Is.True);
            Assert.That(document.RootElement.GetProperty("disputes").GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public async Task DisagreementNamesTheFieldsAndIsADisputeTest()
    {
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(5));
        var returned = new[]
        {
            await m_rig.AnswerAsync(issued[0], task => new VerifyResultData { TaskIndex = task.TaskIndex, Verdict = VerifyVerdict.Fail, ExitCode = 1, Stdout = "5\n" }),
            await m_rig.AnswerAsync(issued[0], task => new VerifyResultData { TaskIndex = task.TaskIndex, Verdict = VerifyVerdict.Pass, Stdout = "4\n" })
        };

        var run = await m_rig.SettleAsync(issued, returned);
        using var document = JsonDocument.Parse(run.Integrity);
        var summary = document.RootElement.GetProperty("summary");
        var pair = document.RootElement.GetProperty("pairs")[0];
        var dispute = document.RootElement.GetProperty("disputes")[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("mismatch_rate").GetDouble(), Is.EqualTo(1.0));
            Assert.That(summary.GetProperty("disputes").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("disputes_resolved").GetInt32(), Is.Zero);
            Assert.That(summary.GetProperty("disputed").GetInt32(), Is.EqualTo(1));
            Assert.That(pair.GetProperty("match").GetBoolean(), Is.False);
            Assert.That(pair.GetProperty("differs").EnumerateArray().Select(field => field.GetString()), Is.EqualTo(new[] { "verdict", "exit", "stdout" }));
            Assert.That(dispute.GetProperty("index").GetInt32(), Is.EqualTo(5));
            Assert.That(dispute.GetProperty("first_copies").GetInt32(), Is.EqualTo(2));
            Assert.That(dispute.GetProperty("outcome").GetString(), Is.EqualTo(VerifyRulings.TOO_FEW_COPIES), "no dispute round was run");
        });
    }

    [Test]
    public async Task TheDocumentNamesNoMachineTest()
    {
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0));
        var nodeId = Guid.NewGuid();
        var named = await m_rig.AnswerAsync(issued[0], Answer, batches =>
        {
            batches[0].Diagnostics!.NodeId = nodeId;
            batches[0].Diagnostics!.NodeName = "contributors-laptop";
        });

        var run = await m_rig.SettleAsync(issued, [named, await m_rig.AnswerAsync(issued[0], Answer)]);

        Assert.Multiple(() =>
        {
            Assert.That(run.Integrity, Does.Not.Contain("contributors-laptop"));
            Assert.That(run.Integrity, Does.Not.Contain(nodeId.ToString()));
            Assert.That(run.Integrity, Does.Not.Contain("node"));
            Assert.That(run.Integrity, Does.Not.Contain(issued[0].Ticket), "a ticket is never written to a document");
        });
    }

    [Test]
    public async Task ATaskExecutedOnceMakesNoPairAndRestsOnOneExecutionTest()
    {
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0, 1));

        var run = await m_rig.SettleAsync(issued, [await m_rig.AnswerAsync(issued[0], Answer)]);
        using var document = JsonDocument.Parse(run.Integrity);

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("summary").GetProperty("sampled").GetInt32(), Is.Zero);
            Assert.That(document.RootElement.GetProperty("summary").GetProperty("single_execution").GetInt32(), Is.EqualTo(2), "unsampled work rests on one machine, and the document says so");
            Assert.That(document.RootElement.GetProperty("pairs").GetArrayLength(), Is.Zero);
        });
    }

    [Test]
    public async Task ATaskANodeFaultedIsRepairedAndCountedTest()
    {
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0, 1));
        var faulted = await m_rig.AnswerAsync(issued[0], task => task.TaskIndex == 1
            ? new VerifyResultData { TaskIndex = 1, Verdict = VerifyVerdict.NoResult, ExitCode = -1, Fault = VerifyFault.WALL_CLOCK }
            : Answer(task));

        var repairs = await m_rig.RepairsAsync(issued, [faulted]);
        Assert.That(repairs.Sum(package => package.TaskCount), Is.EqualTo(1), "only the task without a verdict goes out again");

        var repaired = await m_rig.SettleAsync((issued, [faulted]), (repairs, [await m_rig.AnswerAsync(repairs[0], Answer)]), null);
        var stillFaulted = await m_rig.SettleAsync((issued, [faulted]), (repairs, [await m_rig.AnswerAsync(repairs[0], task =>
            new VerifyResultData { TaskIndex = task.TaskIndex, Verdict = VerifyVerdict.NoResult, ExitCode = -1, Fault = VerifyFault.WALL_CLOCK })]), null);

        Assert.Multiple(() =>
        {
            Assert.That(repaired.Verdicts()[1], Is.EqualTo("Pass"));
            Assert.That(repaired.IntegrityCount("repairs"), Is.EqualTo(1));
            Assert.That(repaired.IntegrityCount("no_result"), Is.Zero);
            Assert.That(stillFaulted.Verdicts()[1], Is.EqualTo("NoResult"));
            Assert.That(stillFaulted.Lines()[1].GetProperty("reason").GetString(), Is.EqualTo(VerifyFault.WALL_CLOCK), "the clock gives a reason, never a Timeout");
            Assert.That(stillFaulted.IntegrityCount("no_result"), Is.EqualTo(1));
            Assert.That(stillFaulted.Verdicts()[0], Is.EqualTo("Pass"), "the task beside it keeps its verdict");
        });
    }

    #endregion

    #region Tools

    private static VerifyResultData Answer(VerifyTaskData task)
    {
        return new VerifyResultData { TaskIndex = task.TaskIndex, Verdict = VerifyVerdict.Pass, Stdout = $"{task.TaskIndex}\n" };
    }

    #endregion
}
