using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;

namespace OutWit.Controller.AI.Verify.Tests.Settlement;

/// <summary>
/// A fan-out that came back short keeps its checking, and the documents say what happened (the
/// delta audit of 2026-11-18, finding N-4). One node could empty the results of a first fan-out
/// with one answer of no value; every task then went out once more in one copy, the node took
/// its share of that round and lied there, and its answers stood, with nothing in the report to
/// show for it. Here the settlement is given what the server sees in such a run: a first fan-out
/// with no results or with fewer than were asked for, and a repair round that ran under the
/// sampling of the first.
/// </summary>
[TestFixture]
public sealed class VerifyShortFanOutTests
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

    #region Repair Tests

    [Test]
    public async Task N4ALiarInTheRepairOfAFanOutThatCameBackEmptyDoesNotWinTest()
    {
        // Three packages, every one of them asked for twice, and nothing came back. All three go
        // out once more, twice each again: the liar holds a copy of the first and of the third.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1), VerifySettleRig.Batch(2));
        VerifyResultPackageData[] first = [];

        var repairs = await m_rig.RepairsAsync(issued, first);
        Assert.That(repairs.Select(package => package.TaskCount), Is.EqualTo(new[] { 1, 1, 1 }), "every task goes out once more, a package a task, in the order they were given out");

        var repaired = new List<VerifyResultPackageData>();
        for (var index = 0; index < repairs.Count; index++)
        {
            repaired.Add(await m_rig.AnswerAsync(repairs[index], Fail));
            repaired.Add(await m_rig.AnswerAsync(repairs[index], index == 1 ? Fail : Pass, Vary(1)));
        }

        // Before any third opinion: the liar's word is never the task's.
        var disputes = await m_rig.DisputesAsync(issued, first, repairs, repaired);
        var unsettled = await m_rig.SettleAsync((issued, first), (repairs, repaired), (disputes, []), samplingRate: 1.0);

        // Three more executions of each disputed task: two honest, and the liar again.
        var votes = new List<VerifyResultPackageData>();
        foreach (var package in disputes)
        {
            votes.Add(await m_rig.AnswerAsync(package, Fail));
            votes.Add(await m_rig.AnswerAsync(package, Fail, Vary(2)));
            votes.Add(await m_rig.AnswerAsync(package, Pass, Vary(3)));
        }

        var settled = await m_rig.SettleAsync((issued, first), (repairs, repaired), (disputes, votes), samplingRate: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(disputes.Sum(package => package.TaskCount), Is.EqualTo(2), "the two tasks whose repair copies disagree");

            Assert.That(unsettled.Verdicts()[1], Is.EqualTo("Fail"), "two copies that agree are accepted");
            Assert.That(new[] { unsettled.Verdicts()[0], unsettled.Verdicts()[2] }, Is.All.EqualTo("Disputed"), "two copies that disagree are never chosen between");
            Assert.That(unsettled.ReportCount("pass"), Is.Zero);

            Assert.That(settled.Verdicts().Values, Is.All.EqualTo("Fail"), "the honest result stands for every task");
            Assert.That(settled.ReportCount("disputes"), Is.EqualTo(2));
            Assert.That(settled.ReportCount("disputes_resolved"), Is.EqualTo(2));
            Assert.That(settled.IntegrityCount("repairs"), Is.EqualTo(3));
            Assert.That(settled.IntegrityCount("sampled"), Is.EqualTo(3), "the pairs of the repair round");
            Assert.That(settled.IntegrityCount("mismatches"), Is.EqualTo(2));
            Assert.That(settled.IntegrityCount("agreed"), Is.EqualTo(3));
            Assert.That(settled.IntegrityCount("single_execution"), Is.Zero, "no task rests on one machine");
            Assert.That(settled.PairRounds().Values, Is.All.EqualTo(VerifyRound.REPAIR));
            Assert.That(settled.Lines()[0].GetProperty("stdout").GetString(), Is.EqualTo("honest 0\n"), "the outputs are the honest ones");

            // And the documents say that the first fan-out's checking did not happen.
            Assert.That(settled.ReportWord("sampling"), Is.EqualTo(VerifySampling.SHORT));
            Assert.That(settled.ReportRate(), Is.EqualTo(1.0));
            Assert.That(settled.ReportCount("unanswered_packages"), Is.EqualTo(3));
            Assert.That(settled.FanOut(VerifyRound.MAIN), Is.EqualTo((3, 2, 3, 0, 3)), "three packages, each asked for twice: none came back with two answers, none with any");
            Assert.That(settled.FanOut(VerifyRound.REPAIR), Is.EqualTo((repairs.Count, 2, repairs.Count, repairs.Count, 0)));
            Assert.That(settled.FanOut(VerifyRound.DISPUTE), Is.EqualTo((disputes.Count, 3, disputes.Count, disputes.Count, 0)));
        });
    }

    [Test]
    public async Task N4MoreAnswersThanWereAskedForInASampledRepairAreADisputeTest()
    {
        // A repair package of a sampled round answered three times: which two are the machines
        // cannot be told, so its task goes to a dispute, as in the first fan-out.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0));
        VerifyResultPackageData[] first = [];
        var repairs = await m_rig.RepairsAsync(issued, first);

        var repaired = new[] { await m_rig.AnswerAsync(repairs[0], Pass), await m_rig.AnswerAsync(repairs[0], Pass, Vary(1)), await m_rig.AnswerAsync(repairs[0], Pass, Vary(2)) };
        var disputes = await m_rig.DisputesAsync(issued, first, repairs, repaired);
        var settled = await m_rig.SettleAsync((issued, first), (repairs, repaired), (disputes, []), samplingRate: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(disputes.Sum(package => package.TaskCount), Is.EqualTo(1));
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Disputed"), "three answers that agree are no agreement of two machines");
            Assert.That(settled.DisputeOutcomes()[0], Is.EqualTo(VerifyRulings.TOO_FEW_COPIES));
        });
    }

    [Test]
    public async Task N4AScriptThatDoesNotStateItsSamplingIsSettledAsBeforeTest()
    {
        // The scripts of an earlier package hand the settlement six arguments and run the repair
        // round once a package: one answer is taken, two are not, and the documents say that
        // they were not told what was asked for.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var first = new[] { await m_rig.AnswerAsync(issued[1], Fail) };
        var repairs = await m_rig.RepairsAsync(issued, first);

        var once = await m_rig.SettleAsync((issued, first), (repairs, [await m_rig.AnswerAsync(repairs[0], Fail)]), (Array.Empty<VerifyTaskPackageData>(), []));
        var twice = await m_rig.SettleAsync((issued, first), (repairs, [await m_rig.AnswerAsync(repairs[0], Pass), await m_rig.AnswerAsync(repairs[0], Pass, Vary(1))]),
            (Array.Empty<VerifyTaskPackageData>(), []));
        var unsampled = await m_rig.SettleAsync((issued, first), (repairs, [await m_rig.AnswerAsync(repairs[0], Pass), await m_rig.AnswerAsync(repairs[0], Pass, Vary(2))]),
            (Array.Empty<VerifyTaskPackageData>(), []), samplingRate: 0);

        Assert.Multiple(() =>
        {
            Assert.That(once.Verdicts()[0], Is.EqualTo("Fail"));
            Assert.That(once.IntegrityCount("single_execution"), Is.EqualTo(2));
            Assert.That(once.ReportWord("sampling"), Is.EqualTo(VerifySampling.NOT_STATED));
            Assert.That(once.ReportRate(), Is.Null);
            Assert.That(once.ReportCount("unanswered_packages"), Is.EqualTo(1), "the package of the first fan-out nothing answered");
            Assert.That(once.FanOut(VerifyRound.MAIN), Is.EqualTo((2, 2, -1, 0, 1)), "how many packages were asked for twice is not known");
            Assert.That(once.FanOut(VerifyRound.REPAIR), Is.EqualTo((1, 1, 1, 1, 0)), "a repair package is asked for once");

            Assert.That(twice.Verdicts()[0], Is.EqualTo("NoResult"), "two answers where one was asked for: neither is taken");
            Assert.That(twice.Rejected(VerifyRoundIntake.SURPLUS), Is.EqualTo(2));

            Assert.That(unsampled.Verdicts()[0], Is.EqualTo("NoResult"), "the same for a script that states a rate of 0: nothing was asked for twice");
            Assert.That(unsampled.ReportWord("sampling"), Is.EqualTo(VerifySampling.NONE));
            Assert.That(unsampled.ReportRate(), Is.Zero);
        });
    }

    #endregion

    #region Sampling Tests

    [Test]
    public async Task N4TheDocumentsSayWhetherTheSamplingHappenedTest()
    {
        // Four packages at a rate of one half: two are asked for twice.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1), VerifySettleRig.Batch(2), VerifySettleRig.Batch(3));
        var once = new List<VerifyResultPackageData>();
        foreach (var package in issued)
            once.Add(await m_rig.AnswerAsync(package, Fail));

        var second = new[] { await m_rig.AnswerAsync(issued[0], Fail, Vary(1)), await m_rig.AnswerAsync(issued[2], Fail, Vary(2)) };

        var asAsked = await m_rig.SettleAsync((issued, [.. once, .. second]), ([], []), (Array.Empty<VerifyTaskPackageData>(), []), samplingRate: 0.5);
        var oneShort = await m_rig.SettleAsync((issued, [.. once, second[0]]), ([], []), (Array.Empty<VerifyTaskPackageData>(), []), samplingRate: 0.5);
        var noneTwice = await m_rig.SettleAsync((issued, once), ([], []), (Array.Empty<VerifyTaskPackageData>(), []), samplingRate: 0.5);
        var notAsked = await m_rig.SettleAsync((issued, once), ([], []), (Array.Empty<VerifyTaskPackageData>(), []), samplingRate: 0);
        var outOfRange = await m_rig.SettleAsync((issued, once), ([], []), (Array.Empty<VerifyTaskPackageData>(), []), samplingRate: double.NaN);

        Assert.Multiple(() =>
        {
            Assert.That(asAsked.ReportWord("sampling"), Is.EqualTo(VerifySampling.AS_ASKED));
            Assert.That(asAsked.ReportRate(), Is.EqualTo(0.5));
            Assert.That(asAsked.ReportCount("unanswered_packages"), Is.Zero);
            Assert.That(asAsked.FanOut(VerifyRound.MAIN), Is.EqualTo((4, 2, 2, 2, 0)));
            Assert.That(asAsked.Report, Does.Contain("\"sampling_rate\":0.5,\"sampling\":\"as_asked\",\"unanswered_packages\":0"), "the three fields as the report writes them");
            Assert.That(asAsked.Integrity, Does.Contain("\"sampling_rate\":0.5,\"sampling\":\"as_asked\",\"unanswered_packages\":0"), "and the integrity document, the same");

            Assert.That(oneShort.ReportWord("sampling"), Is.EqualTo(VerifySampling.SHORT), "one of the two came back with one answer");
            Assert.That(oneShort.IntegrityWord("sampling"), Is.EqualTo(VerifySampling.SHORT));
            Assert.That(oneShort.FanOut(VerifyRound.MAIN), Is.EqualTo((4, 2, 2, 1, 0)));
            Assert.That(oneShort.Verdicts().Values, Is.All.EqualTo("Fail"), "the verdicts are what they were: a short fan-out is said, not punished");

            Assert.That(noneTwice.ReportWord("sampling"), Is.EqualTo(VerifySampling.SHORT), "a run whose checking did not happen says so");
            Assert.That(noneTwice.IntegrityCount("sampled"), Is.Zero);
            Assert.That(noneTwice.FanOut(VerifyRound.MAIN), Is.EqualTo((4, 2, 2, 0, 0)));

            Assert.That(notAsked.ReportWord("sampling"), Is.EqualTo(VerifySampling.NONE));
            Assert.That(notAsked.FanOut(VerifyRound.MAIN), Is.EqualTo((4, 2, 0, 0, 0)));
            Assert.That(outOfRange.ReportWord("sampling"), Is.EqualTo(VerifySampling.NONE), "a rate that is no share is a rate of 0");
            Assert.That(outOfRange.ReportRate(), Is.Zero);
        });
    }

    [Test]
    public async Task N4ASampledPackageWithARefusedCopyAndADisputeRoundThatCameBackShortAreCountedTest()
    {
        // Two packages asked for twice. The second copy of one is no package at all: its honest
        // copy stands on one machine, and the fan-out is one package short of its sampling. The
        // two copies of the other disagree, and of the three copies of its dispute one came back.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var first = new[]
        {
            await m_rig.AnswerAsync(issued[0], Fail), await m_rig.StoreRawAsync("no package"u8.ToArray(), issued[0].Ticket),
            await m_rig.AnswerAsync(issued[1], Fail), await m_rig.AnswerAsync(issued[1], Pass)
        };

        var disputes = await m_rig.DisputesAsync(issued, first, [], []);
        var settled = await m_rig.SettleAsync((issued, first), ([], []), (disputes, [await m_rig.AnswerAsync(disputes[0], Fail)]), samplingRate: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Fail"));
            Assert.That(settled.Verdicts()[1], Is.EqualTo("Disputed"));
            Assert.That(settled.ReportWord("sampling"), Is.EqualTo(VerifySampling.SHORT));
            Assert.That(settled.FanOut(VerifyRound.MAIN), Is.EqualTo((2, 2, 2, 1, 0)));
            Assert.That(settled.FanOut(VerifyRound.DISPUTE), Is.EqualTo((1, 3, 1, 0, 0)), "one package in dispute, asked for three times, not once complete");
            Assert.That(settled.ReportCount("unanswered_packages"), Is.Zero, "every package has an answer; it is the second ones that are missing");
            Assert.That(settled.IntegrityCount("single_execution"), Is.EqualTo(1));
            Assert.That(settled.ReportCount("rejected_results"), Is.EqualTo(1));
        });
    }

    [Test]
    public void N4TheRateAsksForTheShareRoundedUpAndOnePackageAtLeastTest()
    {
        // The rule the platform selects by (Grid.Replicas): the settlement counts by the same.
        Assert.Multiple(() =>
        {
            Assert.That(VerifySampling.Selected(6, 1.0), Is.EqualTo(6));
            Assert.That(VerifySampling.Selected(6, 0.5), Is.EqualTo(3));
            Assert.That(VerifySampling.Selected(6, 0.34), Is.EqualTo(3), "2.04 packages are three");
            Assert.That(VerifySampling.Selected(6, 0.05), Is.EqualTo(1), "one at least for any rate above 0");
            Assert.That(VerifySampling.Selected(10, 0.3), Is.EqualTo(3), "ten times 0.3 is a hair over three, and is three");
            Assert.That(VerifySampling.Selected(1562, 0.05), Is.EqualTo(79));
            Assert.That(VerifySampling.Selected(6, 7.0), Is.EqualTo(6));
            Assert.That(VerifySampling.Selected(6, 0), Is.Zero);
            Assert.That(VerifySampling.Selected(6, -1), Is.Zero);
            Assert.That(VerifySampling.Selected(6, double.NaN), Is.Zero);
            Assert.That(VerifySampling.Selected(0, 1.0), Is.Zero);

            Assert.That(VerifySampling.Clamp(double.NaN), Is.Zero);
            Assert.That(VerifySampling.Clamp(-0.5), Is.Zero);
            Assert.That(VerifySampling.Clamp(0.25), Is.EqualTo(0.25));
            Assert.That(VerifySampling.Clamp(double.PositiveInfinity), Is.EqualTo(1.0));

            Assert.That(VerifySampling.STATES, Is.EqualTo(new[] { "not_stated", "none", "as_asked", "short" }), "the words of the documents");
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
