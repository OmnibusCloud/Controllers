using System.Buffers.Binary;
using MemoryPack;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Settlement;
using OutWit.Controller.AI.Verify.Tests.Mock;

namespace OutWit.Controller.AI.Verify.Tests.Settlement;

/// <summary>
/// What one node can do to a run with the bytes and the numbers of a result it is entitled to
/// send (the delta audit of 2026-11-18: findings N-1, N-3, N-10 and N-12). Each test gives the
/// server such a package beside an honest one and settles the run as the bundled scripts do:
/// the three activities that read results (repairs, disputes, the settlement) all complete, the
/// package is refused under a fixed reason, its tasks are simply without an answer from it, and
/// the honest package stands. A package that cannot be read, or that says what no run of its
/// task could give, fails that package and never the run.
/// </summary>
[TestFixture]
public sealed class VerifyResultIntakeTests
{
    #region Constants

    private const long MEBIBYTE = 1024 * 1024;

    // The offset of the count of case records in the frame of a result whose texts are empty:
    // the member count, three numbers, and two texts each followed by its flag.
    private const int CASE_COUNT_OFFSET = 1 + 3 * sizeof(int) + 2 * (sizeof(int) + 1);

    // The offset of the header of stdout in the frame of a result.
    private const int STDOUT_OFFSET = 1 + 3 * sizeof(int);

    #endregion

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

    #region Unreadable Tests

    [Test]
    public async Task N1AFrameWhoseTextLeavesItFailsItsPackageNotTheRunTest()
    {
        // A result whose stdout states a length far outside the frame that holds it. The frame
        // is refused where it is walked, before anything decodes it.
        var frame = MemoryPackSerializer.Serialize(new VerifyResultData { TaskIndex = 1, Stdout = "AAAA" });
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(STDOUT_OFFSET), int.MinValue);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(STDOUT_OFFSET + sizeof(int)), 0);

        var run = await SettleBesideAnHonestPackageAsync(ticket => [VerifySettleRig.HeadFrame(ticket), (VerifyResultPackageWriter.FRAME_RESULT, frame)]);

        AssertRefusedAlone(run, VerifyRoundIntake.UNREADABLE);
    }

    [Test]
    public async Task N3ACountBelowZeroFailsItsPackageNotTheRunTest()
    {
        // A list of case records that states a count of -2: an exception of another type than
        // the reader caught, out of all three activities.
        var frame = MemoryPackSerializer.Serialize(new VerifyResultData { TaskIndex = 1 });
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(CASE_COUNT_OFFSET), -2);

        var run = await SettleBesideAnHonestPackageAsync(ticket => [VerifySettleRig.HeadFrame(ticket), (VerifyResultPackageWriter.FRAME_RESULT, frame)]);

        AssertRefusedAlone(run, VerifyRoundIntake.UNREADABLE);
    }

    [Test]
    public async Task N3ASurrogateWithoutItsPairFailsItsPackageNotTheRunTest()
    {
        // Stdout in the UTF-16 form, one character: a high surrogate alone. The decoder took it,
        // and the settlement threw when it wrote the result on.
        var honest = MemoryPackSerializer.Serialize(new VerifyResultData { TaskIndex = 1 });
        byte[] frame = [.. honest[..STDOUT_OFFSET], 1, 0, 0, 0, 0x3D, 0xD8, .. honest[(STDOUT_OFFSET + sizeof(int))..]];

        var run = await SettleBesideAnHonestPackageAsync(ticket => [VerifySettleRig.HeadFrame(ticket), (VerifyResultPackageWriter.FRAME_RESULT, frame)]);

        AssertRefusedAlone(run, VerifyRoundIntake.UNREADABLE);
    }

    [Test]
    public async Task N10ACountOfMillionsIsRefusedBeforeAnythingIsAllocatedByItTest()
    {
        // A result of 67,108,800 case records, each the one byte of no object: 64 MiB on the
        // wire, a few kilobytes as a blob. The decoder made a list of that many places (640 MiB)
        // before the result was refused.
        const int claimed = 67_108_800;
        var honest = MemoryPackSerializer.Serialize(new VerifyResultData { TaskIndex = 1 });

        var frame = new byte[honest.Length + claimed];
        honest.AsSpan(0, CASE_COUNT_OFFSET).CopyTo(frame);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(CASE_COUNT_OFFSET), claimed);
        frame.AsSpan(CASE_COUNT_OFFSET + sizeof(int), claimed).Fill(255);
        honest.AsSpan(CASE_COUNT_OFFSET + sizeof(int)).CopyTo(frame.AsSpan(CASE_COUNT_OFFSET + sizeof(int) + claimed));
        Assert.That(frame.Length, Is.LessThanOrEqualTo(VerifyResultPackageReader.MAX_FRAME_BYTES), "a frame the reader takes whole");

        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var returned = new[]
        {
            await m_rig.AnswerAsync(issued[0], Fail),
            await m_rig.StoreFramesAsync(issued[1].Ticket, VerifySettleRig.HeadFrame(issued[1].Ticket), (VerifyResultPackageWriter.FRAME_RESULT, frame))
        };
        frame = [];

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var run = await m_rig.SettleAsync(issued, returned);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.Multiple(() =>
        {
            Assert.That(run.Verdicts()[0], Is.EqualTo("Fail"));
            Assert.That(run.Verdicts()[1], Is.EqualTo("NoResult"));
            Assert.That(run.Rejected(VerifyRoundIntake.UNREADABLE), Is.EqualTo(1), "a count outside its bound is a frame that cannot be read");
            Assert.That(allocated, Is.LessThan(320 * MEBIBYTE), "the frame is read (twice its 64 MiB, as its buffer grows), and nothing is allocated by the count in it");
        });
    }

    [Test]
    public async Task N3WhateverFailsWhileAPackageIsReadFailsThatPackageTest()
    {
        // A failure of a type nobody listed: the store answers for one result blob with an
        // exception of its own kind. The reading caught four types and let the rest end the run.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var honest = await m_rig.AnswerAsync(issued[0], Fail);
        var lost = await m_rig.AnswerAsync(issued[1], Fail);

        var store = new VerifyFaultingBlobService(m_rig.Blobs, [lost.ResultsBlobId], () => new NotSupportedException("a store of another kind"));
        m_rig.Server = store;

        var run = await m_rig.SettleAsync(issued, [honest, lost]);

        Assert.Multiple(() =>
        {
            Assert.That(store.Failures, Is.GreaterThan(0));
            Assert.That(run.Verdicts()[0], Is.EqualTo("Fail"), "the package beside it stands");
            Assert.That(run.Verdicts()[1], Is.EqualTo("NoResult"));
            Assert.That(run.Rejected(VerifyRoundIntake.UNREADABLE), Is.EqualTo(1));
            Assert.That(run.ReportCount("rejected_results"), Is.EqualTo(1));
        });
    }

    #endregion

    #region Held To The Task Tests

    [Test]
    public async Task N12AResultOutsideWhatItsTaskAllowsIsRefusedTest()
    {
        // Honest verdicts with numbers and records no run of the task could give. All of these
        // were accepted and written into the documents; the first of them ended the run when
        // the provenance summed the fuel.
        var lies = new List<(int Cases, string What, Func<VerifyTaskData, VerifyResultData> Answer)>
        {
            (0, "fuel of the largest number", task => With(task, result => result.FuelConsumed = long.MaxValue)),
            (0, "fuel one unit over the budget", task => With(task, result => result.FuelConsumed = VerifySandboxDefaults.DEFAULT_FUEL_BUDGET + 1)),
            (0, "fuel below zero", task => With(task, result => result.FuelConsumed = -1)),
            (0, "memory one byte over the cap", task => With(task, result => result.PeakMemoryBytes = VerifySandboxDefaults.DEFAULT_MEMORY_BYTES + 1)),
            (0, "memory below zero", task => With(task, result => result.PeakMemoryBytes = -1)),
            (0, "a time below zero", task => With(task, result => result.WallMs = -1)),
            (0, "8 MiB of stdout under a cap of 256 KiB", task => With(task, result => result.Stdout = new string('x', VerifyLimitCeilings.MAX_STDOUT_LIMIT_BYTES))),
            (0, "one character of stdout over the cap", task => With(task, result => result.Stdout = new string('x', VerifySandboxDefaults.DEFAULT_STDOUT_LIMIT_BYTES + 1))),
            (0, "a thousand case records for a task without a suite", task => With(task, result => { result.Verdict = VerifyVerdict.Pass; result.ExitCode = 0; result.CaseResults = Cases(1000); })),
            (0, "a pass with exit code 7", task => With(task, result => { result.Verdict = VerifyVerdict.Pass; result.ExitCode = 7; })),
            (2, "one case record for a suite of two", task => With(task, result => { result.Verdict = VerifyVerdict.Pass; result.ExitCode = 0; result.CaseResults = Cases(1); })),
            (2, "three case records for a suite of two", task => With(task, result => { result.Verdict = VerifyVerdict.Pass; result.ExitCode = 0; result.CaseResults = Cases(3); })),
            (2, "no case record under a verdict of a task that ran", task => With(task, result => result.Verdict = VerifyVerdict.RuntimeError)),
            (2, "case records out of their order", task => With(task, result =>
            {
                result.Verdict = VerifyVerdict.Pass;
                result.ExitCode = 0;
                result.CaseResults = Cases(2);
                result.CaseResults.Reverse();
            })),
            (2, "a pass over a case that failed", task => With(task, result =>
            {
                result.Verdict = VerifyVerdict.Pass;
                result.ExitCode = 0;
                result.CaseResults = Cases(2);
                result.CaseResults[1].Passed = false;
                result.CaseResults[1].Verdict = VerifyVerdict.Fail;
            })),
            (2, "a case that passed with a verdict that is no pass", task => With(task, result =>
            {
                result.Verdict = VerifyVerdict.Pass;
                result.ExitCode = 0;
                result.CaseResults = Cases(2);
                result.CaseResults[0].Verdict = VerifyVerdict.Timeout;
            })),
            (2, "the cases' stdout together over the cap", task => With(task, result =>
            {
                result.Verdict = VerifyVerdict.Pass;
                result.ExitCode = 0;
                result.CaseResults = Cases(2);
                result.CaseResults[0].ActualStdout = new string('x', VerifySandboxDefaults.DEFAULT_STDOUT_LIMIT_BYTES);
                result.CaseResults[1].ActualStdout = "y";
            }))
        };

        var batches = new List<VerifyTaskBatchData> { VerifySettleRig.Batch(0) };
        for (var lie = 0; lie < lies.Count; lie++)
            batches.Add(VerifySettleRig.BatchOf(lies[lie].Cases, null, lie + 1));

        var issued = await m_rig.PublishAsync(batches.ToArray());
        Assert.That(issued, Has.Count.EqualTo(batches.Count), "a package a task");

        var returned = new List<VerifyResultPackageData> { await m_rig.AnswerAsync(issued[0], Fail) };
        for (var lie = 0; lie < lies.Count; lie++)
            returned.Add(await m_rig.AnswerAsync(issued[lie + 1], lies[lie].Answer));

        var repairs = await m_rig.RepairsAsync(issued, returned);
        var run = await m_rig.SettleAsync(issued, returned);
        var verdicts = run.Verdicts();

        Assert.Multiple(() =>
        {
            Assert.That(verdicts[0], Is.EqualTo("Fail"), "the honest package stands");

            for (var lie = 0; lie < lies.Count; lie++)
                Assert.That(verdicts[lie + 1], Is.EqualTo("NoResult"), lies[lie].What);

            Assert.That(run.Rejected(VerifyRoundIntake.CONTENT), Is.EqualTo(lies.Count));
            Assert.That(run.ReportCount("rejected_results"), Is.EqualTo(lies.Count));
            Assert.That(repairs.Sum(package => package.TaskCount), Is.EqualTo(lies.Count), "every task of a refused package goes out once more");
            Assert.That(run.Provenance, Does.Not.Contain(long.MaxValue.ToString()), "a refused number reaches no document");
        });
    }

    [Test]
    public async Task N12AResultAtTheEdgeOfWhatItsTaskAllowsIsTakenTest()
    {
        // What an honest node can report at the most, under limits of the task's own: the whole
        // budget for each of two runs, the memory cap, the stdout cap shared by the cases. And a
        // task that never ran: a module that did not compile, a compile the budget did not pay for.
        var limits = new VerifyLimitsData { FuelBudget = 1000, MemoryBytes = 65536, StdoutLimitBytes = 8, StderrLimitBytes = 4 };
        var issued = await m_rig.PublishAsync(
            VerifySettleRig.BatchOf(2, limits, 0), VerifySettleRig.BatchOf(2, limits, 1), VerifySettleRig.BatchOf(2, limits, 2), VerifySettleRig.BatchOf(0, limits, 3),
            VerifySettleRig.BatchOf(2, limits, 4), VerifySettleRig.BatchOf(2, limits, 5), VerifySettleRig.BatchOf(0, limits, 6));

        var returned = new[]
        {
            await m_rig.AnswerAsync(issued[0], task => With(task, result =>
            {
                result.Verdict = VerifyVerdict.Pass;
                result.ExitCode = 0;
                result.FuelConsumed = 2000;
                result.PeakMemoryBytes = 65536;
                result.CaseResults = Cases(2);
                result.CaseResults[0].ActualStdout = "1234";
                result.CaseResults[1].ActualStdout = "5678";
                result.Stdout = "5678";

                // The node's own words for a run are not counted against the task's stderr cap.
                result.Stderr = "longer than four";
            })),
            await m_rig.AnswerAsync(issued[1], task => With(task, result => { result.Verdict = VerifyVerdict.CompileError; result.ExitCode = -1; result.FuelConsumed = 0; })),
            await m_rig.AnswerAsync(issued[2], task => With(task, result => { result.Verdict = VerifyVerdict.Timeout; result.ExitCode = -1; result.FuelConsumed = 1000; })),
            await m_rig.AnswerAsync(issued[3], task => With(task, result => { result.Verdict = VerifyVerdict.Pass; result.ExitCode = 0; result.FuelConsumed = 1000; result.Stdout = "12345678"; })),

            // One past each edge.
            await m_rig.AnswerAsync(issued[4], task => With(task, result => { result.Verdict = VerifyVerdict.Pass; result.ExitCode = 0; result.FuelConsumed = 2001; result.CaseResults = Cases(2); })),
            await m_rig.AnswerAsync(issued[5], task => With(task, result =>
            {
                result.Verdict = VerifyVerdict.Pass;
                result.ExitCode = 0;
                result.CaseResults = Cases(2);
                result.CaseResults[0].ActualStdout = "12345";
                result.CaseResults[1].ActualStdout = "6789";
            })),
            await m_rig.AnswerAsync(issued[6], task => With(task, result => { result.Verdict = VerifyVerdict.Pass; result.ExitCode = 0; result.FuelConsumed = 1001; }))
        };

        var run = await m_rig.SettleAsync(issued, returned);
        var verdicts = run.Verdicts();

        Assert.Multiple(() =>
        {
            Assert.That(verdicts[0], Is.EqualTo("Pass"));
            Assert.That(verdicts[1], Is.EqualTo("CompileError"));
            Assert.That(verdicts[2], Is.EqualTo("Timeout"));
            Assert.That(verdicts[3], Is.EqualTo("Pass"));
            Assert.That(new[] { verdicts[4], verdicts[5], verdicts[6] }, Is.All.EqualTo("NoResult"), "one unit of fuel, one character of the cases, one unit of fuel of a single run");
            Assert.That(run.Rejected(VerifyRoundIntake.CONTENT), Is.EqualTo(3));
        });
    }

    #endregion

    #region Tools

    /// <summary>Settles a run of two packages: an honest answer to the first, and to the second a package of frames a test wrote by hand.</summary>
    private async Task<(VerifySettledRun Run, int Repairs, int Disputes)> SettleBesideAnHonestPackageAsync(Func<string, (byte Kind, byte[] Image)[]> frames)
    {
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var returned = new[] { await m_rig.AnswerAsync(issued[0], Fail), await m_rig.StoreFramesAsync(issued[1].Ticket, frames(issued[1].Ticket)) };

        // The three activities that read what the nodes returned, as the script calls them.
        var repairs = await m_rig.RepairsAsync(issued, returned);
        var disputes = await m_rig.DisputesAsync(issued, returned);
        var run = await m_rig.SettleAsync(issued, returned);

        return (run, repairs.Sum(package => package.TaskCount), disputes.Count);
    }

    private static void AssertRefusedAlone((VerifySettledRun Run, int Repairs, int Disputes) settled, string reason)
    {
        Assert.Multiple(() =>
        {
            Assert.That(settled.Run.Verdicts()[0], Is.EqualTo("Fail"), "the honest package stands");
            Assert.That(settled.Run.Verdicts()[1], Is.EqualTo("NoResult"), "the task of the refused package is without an answer");
            Assert.That(settled.Run.Lines()[1].GetProperty("reason").GetString(), Is.EqualTo(VerifyFault.NO_ANSWER));
            Assert.That(settled.Run.Rejected(reason), Is.EqualTo(1));
            Assert.That(settled.Run.ReportCount("rejected_results"), Is.EqualTo(1));
            Assert.That(settled.Repairs, Is.EqualTo(1), "and goes out once more");
            Assert.That(settled.Disputes, Is.Zero, "a refused package is no execution to disagree with");
        });
    }

    private static VerifyResultData Fail(VerifyTaskData task)
    {
        return new VerifyResultData { TaskIndex = task.TaskIndex, Verdict = VerifyVerdict.Fail, ExitCode = 1, Stdout = $"honest {task.TaskIndex}\n", FuelConsumed = 100, WallMs = 3 };
    }

    /// <summary>An answer a node could honestly give, with one thing changed.</summary>
    private static VerifyResultData With(VerifyTaskData task, Action<VerifyResultData> change)
    {
        var result = new VerifyResultData { TaskIndex = task.TaskIndex, Verdict = VerifyVerdict.RuntimeError, ExitCode = 1, FuelConsumed = 100, PeakMemoryBytes = 65536, WallMs = 3 };
        change(result);
        return result;
    }

    /// <summary>Case records of cases that all passed, in order.</summary>
    private static List<VerifyCaseResultData> Cases(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new VerifyCaseResultData { CaseIndex = index, Passed = true, Verdict = VerifyVerdict.Pass, ExitCode = 0, ActualStdout = "" })
            .ToList();
    }

    #endregion
}
