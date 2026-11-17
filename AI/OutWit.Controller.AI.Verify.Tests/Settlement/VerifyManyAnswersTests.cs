using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;

namespace OutWit.Controller.AI.Verify.Tests.Settlement;

/// <summary>
/// A node that returns two answers for one package and still answers all its other packages,
/// in each of the three fan-outs. The server sees results, not machines, and whether a node can
/// return more than one result for a package is the platform's to decide, not the controller's.
/// What the controller does alone is hold a package to the number of answers that were asked
/// for: where the second answer makes one too many, nothing is settled by counting.
/// <para>
/// The two tests named PlatformDependency pin what the controller cannot tell apart alone: a
/// second answer that fills the place of an answer nobody gave. They hold as long as the rulings
/// are what they are; closing them needs the platform to hold a node to one result for each
/// package it was given.
/// </para>
/// </summary>
[TestFixture]
public sealed class VerifyManyAnswersTests
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
    public async Task H2TwoAnswersForASampledPackageOfTheFirstFanOutSendItToADisputeTest()
    {
        // The first package went to an honest machine and to the liar. The liar answers it twice,
        // and answers its other package as it should.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var first = new[]
        {
            await m_rig.AnswerAsync(issued[0], Fail), await m_rig.AnswerAsync(issued[0], Pass, Vary(1)), await m_rig.AnswerAsync(issued[0], Pass, Vary(2)),
            await m_rig.AnswerAsync(issued[1], Fail), await m_rig.AnswerAsync(issued[1], Fail, Vary(3))
        };

        var unsettled = await m_rig.SettleAsync(issued, first);
        var disputes = await m_rig.DisputesAsync(issued, first);

        var votes = new[] { await m_rig.AnswerAsync(disputes[0], Fail), await m_rig.AnswerAsync(disputes[0], Fail, Vary(4)), await m_rig.AnswerAsync(disputes[0], Fail, Vary(5)) };
        var settled = await m_rig.SettleAsync((issued, first), ([], []), (disputes, votes));

        Assert.Multiple(() =>
        {
            Assert.That(unsettled.Verdicts()[0], Is.EqualTo("Disputed"), "three answers where two were asked for are no agreement, whatever two of them say");
            Assert.That(unsettled.Verdicts()[1], Is.EqualTo("Fail"));
            Assert.That(disputes, Has.Count.EqualTo(1), "the package goes to a dispute; the other does not");
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Fail"), "and three machines settle it");
        });
    }

    [Test]
    public async Task H2TwoAnswersForARepairPackageAreNotTakenTest()
    {
        // Nobody answered the first package; it goes out once more, to the liar, which answers it
        // twice. One answer was asked for.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var first = new[] { await m_rig.AnswerAsync(issued[1], Fail) };

        var repairs = await m_rig.RepairsAsync(issued, first);
        Assert.That(repairs, Has.Count.EqualTo(1));

        var repaired = new[] { await m_rig.AnswerAsync(repairs[0], Pass), await m_rig.AnswerAsync(repairs[0], Pass, Vary(1)) };
        var settled = await m_rig.SettleAsync((issued, first), (repairs, repaired), null);

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts()[0], Is.EqualTo("NoResult"), "two answers where one was asked for: neither is taken");
            Assert.That(settled.Lines()[0].GetProperty("reason").GetString(), Is.EqualTo(VerifyFault.NO_ANSWER));
            Assert.That(settled.Rejected(VerifyRoundIntake.SURPLUS), Is.EqualTo(2));
            Assert.That(settled.Verdicts()[1], Is.EqualTo("Fail"));
            Assert.That(settled.ReportCount("pass"), Is.Zero);
        });
    }

    [Test]
    public async Task H2TwoAnswersForADisputePackageAreNotTakenTest()
    {
        // Three machines answer the dispute of the first package, and the liar answers it twice:
        // four answers where three were asked for. It answers the second as it should.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var first = new List<VerifyResultPackageData>();
        foreach (var package in issued)
        {
            first.Add(await m_rig.AnswerAsync(package, Fail));
            first.Add(await m_rig.AnswerAsync(package, Pass));
        }

        var disputes = await m_rig.DisputesAsync(issued, first);
        Assert.That(disputes, Has.Count.EqualTo(2));

        var votes = new[]
        {
            await m_rig.AnswerAsync(disputes[0], Fail), await m_rig.AnswerAsync(disputes[0], Fail, Vary(1)),
            await m_rig.AnswerAsync(disputes[0], Pass, Vary(2)), await m_rig.AnswerAsync(disputes[0], Pass, Vary(3)),
            await m_rig.AnswerAsync(disputes[1], Fail), await m_rig.AnswerAsync(disputes[1], Fail, Vary(4)), await m_rig.AnswerAsync(disputes[1], Fail, Vary(5))
        };

        var settled = await m_rig.SettleAsync((issued, first), ([], []), (disputes, votes));

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Disputed"), "four answers where three were asked for: none is taken");
            Assert.That(settled.DisputeOutcomes()[0], Is.EqualTo(VerifyRulings.TOO_FEW_COPIES));
            Assert.That(settled.Rejected(VerifyRoundIntake.SURPLUS), Is.EqualTo(4));
            Assert.That(settled.Verdicts()[1], Is.EqualTo("Fail"), "the package it answered once is settled by its three");
            Assert.That(settled.ReportCount("pass"), Is.Zero);
        });
    }

    [Test]
    public async Task H2PlatformDependencyAnUnsampledPackageAnsweredTwiceReadsAsTwoExecutionsTest()
    {
        // A package that was not sampled went to one machine, the liar. One forged answer would be
        // the task's result: unsampled work rests on the machine that ran it, and the documents
        // say so. A second answer adds nothing to that but a wrong count: the integrity document
        // says the task was accepted on two executions that agree. The controller alone cannot
        // tell this from a sampled package (it does not know which packages the platform sampled).
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var first = new[]
        {
            await m_rig.AnswerAsync(issued[0], Pass), await m_rig.AnswerAsync(issued[0], Pass, Vary(1)),
            await m_rig.AnswerAsync(issued[1], Fail)
        };

        var settled = await m_rig.SettleAsync(issued, first);

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Pass"), "as one forged answer would be: one machine ran the task");
            Assert.That(settled.IntegrityCount("agreed"), Is.EqualTo(1), "the count that needs the platform: two answers are read as two executions");
            Assert.That(settled.IntegrityCount("single_execution"), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task H2PlatformDependencyInADisputeWithACopyMissingASecondAnswerIsCountedAsAMachineTest()
    {
        // The dispute of the first package was answered by two machines only, an honest one and
        // the liar, and the liar answers it twice: three answers, as were asked for. It answers
        // the second package as it should, so no package of the round is short of an answer.
        // To the controller this is what two machines outvoting a third looks like. Where the
        // platform holds a node to one result for each package it was given, the second answer
        // cannot exist without another package being short, and the ruling leaves it out
        // (VerifyTrustBoundaryTests.H2ASecondAnswerOfOneMachineDoesNotWinADisputeTest).
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var first = new List<VerifyResultPackageData>();
        foreach (var package in issued)
        {
            first.Add(await m_rig.AnswerAsync(package, Fail));
            first.Add(await m_rig.AnswerAsync(package, Pass));
        }

        var disputes = await m_rig.DisputesAsync(issued, first);

        var votes = new[]
        {
            await m_rig.AnswerAsync(disputes[0], Fail), await m_rig.AnswerAsync(disputes[0], Pass, Vary(1)), await m_rig.AnswerAsync(disputes[0], Pass, Vary(2)),
            await m_rig.AnswerAsync(disputes[1], Fail), await m_rig.AnswerAsync(disputes[1], Fail, Vary(3)), await m_rig.AnswerAsync(disputes[1], Fail, Vary(4))
        };

        var settled = await m_rig.SettleAsync((issued, first), ([], []), (disputes, votes));

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Pass"), "the gap this test pins: the controller alone counts the two answers as two machines");
            Assert.That(settled.DisputeOutcomes()[0], Is.EqualTo(VerifyRulings.RESOLVED));
            Assert.That(settled.Verdicts()[1], Is.EqualTo("Fail"));
        });
    }

    #endregion

    #region Tools

    private static VerifyResultData Pass(VerifyTaskData task)
    {
        return Result(task, VerifyVerdict.Pass, "");
    }

    private static VerifyResultData Fail(VerifyTaskData task)
    {
        return Result(task, VerifyVerdict.Fail, $"honest {task.TaskIndex}\n");
    }

    private static VerifyResultData Result(VerifyTaskData task, VerifyVerdict verdict, string stdout)
    {
        return new VerifyResultData { TaskIndex = task.TaskIndex, Verdict = verdict, ExitCode = verdict == VerifyVerdict.Pass ? 0 : 1, Stdout = stdout, FuelConsumed = 100, WallMs = 3 };
    }

    /// <summary>Changes a cost of the answer, so that two packages with the same results are different blobs.</summary>
    private static Action<List<VerifyResultBatchData>> Vary(int by)
    {
        return batches => batches[0].Diagnostics!.WallMs += by;
    }

    #endregion
}
