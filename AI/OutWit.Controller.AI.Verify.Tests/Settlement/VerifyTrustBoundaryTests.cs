using System.IO.Compression;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;

namespace OutWit.Controller.AI.Verify.Tests.Settlement;

/// <summary>
/// What a lying node cannot do to a run. Each test is one scenario of the audit of 2026-11-17
/// (finding H-2, with H-1's decode half and L-8): a node's answer is made as a liar would make
/// it, the run is settled as the bundled scripts settle it, and the documents a lab reads are
/// looked at. On the code the audit read, the lowest verdict won and nothing bound a result to
/// the work it answered.
/// </summary>
[TestFixture]
public sealed class VerifyTrustBoundaryTests
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

    #region H-2 Tests

    [Test]
    public async Task H2ALiarThatSaysPassDoesNotWinTheReportTest()
    {
        // Scenario 1: a sampled task. The honest node says Fail, the liar says Pass with nothing printed.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0, 1));
        var honest = await m_rig.AnswerAsync(issued[0], Fail);
        var liar = await m_rig.AnswerAsync(issued[0], Pass);

        // Without a third opinion the task is disputed: never the liar's Pass.
        var unsettled = await m_rig.SettleAsync(issued, [honest, liar]);
        Assert.Multiple(() =>
        {
            Assert.That(unsettled.Verdicts().Values, Is.All.EqualTo("Disputed"));
            Assert.That(unsettled.ReportCount("pass"), Is.Zero);
            Assert.That(unsettled.ReportCount("disputes"), Is.EqualTo(2));
            Assert.That(unsettled.ReportCount("disputes_resolved"), Is.Zero);
            Assert.That(unsettled.IntegrityCount("mismatches"), Is.EqualTo(2), "the report and the integrity document say the same thing");
        });

        // With three more executions, two honest and the liar again, the honest result stands.
        var disputes = await m_rig.DisputesAsync(issued, [honest, liar]);
        Assert.That(disputes.Sum(package => package.TaskCount), Is.EqualTo(2));
        var votes = new[] { await m_rig.AnswerAsync(disputes[0], Fail), await m_rig.AnswerAsync(disputes[0], Fail), await m_rig.AnswerAsync(disputes[0], Pass) };

        var settled = await m_rig.SettleAsync((issued, [honest, liar]), ([], []), (disputes, votes));
        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts().Values, Is.All.EqualTo("Fail"));
            Assert.That(settled.ReportCount("disputes"), Is.EqualTo(2));
            Assert.That(settled.ReportCount("disputes_resolved"), Is.EqualTo(2));
            Assert.That(settled.Lines()[0].GetProperty("stdout").GetString(), Is.EqualTo("honest 0\n"), "the outputs are the honest ones");
        });
    }

    [Test]
    public async Task H2ADisputeWithoutAMajorityStaysDisputedTest()
    {
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0));
        var first = new[] { await m_rig.AnswerAsync(issued[0], Fail), await m_rig.AnswerAsync(issued[0], Pass) };
        var disputes = await m_rig.DisputesAsync(issued, first);

        // Three copies, three different answers: none was returned by two machines.
        var votes = new[]
        {
            await m_rig.AnswerAsync(disputes[0], Fail),
            await m_rig.AnswerAsync(disputes[0], Pass),
            await m_rig.AnswerAsync(disputes[0], task => Result(task, VerifyVerdict.RuntimeError, "crash"))
        };
        var tie = await m_rig.SettleAsync((issued, first), ([], []), (disputes, votes));

        // Two copies only (too few machines), and they disagree: a tie.
        var two = await m_rig.SettleAsync((issued, first), ([], []), (disputes, votes.Take(2)));

        // One copy only: no result is returned by two.
        var one = await m_rig.SettleAsync((issued, first), ([], []), (disputes, votes.Take(1)));

        Assert.Multiple(() =>
        {
            Assert.That(tie.Verdicts()[0], Is.EqualTo("Disputed"));
            Assert.That(two.Verdicts()[0], Is.EqualTo("Disputed"));
            Assert.That(one.Verdicts()[0], Is.EqualTo("Disputed"));
            Assert.That(tie.ReportCount("pass"), Is.Zero, "a disputed task is out of the pass counts");
            Assert.That(tie.Lines()[0].GetProperty("stdout").GetString(), Is.Empty, "and out of the products");
        });
    }

    [Test]
    public async Task H2ANodeCannotAnswerForTasksItWasNeverGivenTest()
    {
        // Scenario 2: a node given one package returns Pass for every index of the job.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0, 1), VerifySettleRig.Batch(2, 3));
        var honest = new[] { await m_rig.AnswerAsync(issued[0], Fail), await m_rig.AnswerAsync(issued[1], Fail) };

        var liar = await m_rig.AnswerAsync(issued[0], Pass, batches =>
        {
            foreach (var foreign in new[] { 2, 3 })
                batches[0].Results.Add(new VerifyResultData { TaskIndex = foreign, Verdict = VerifyVerdict.Pass });
        });

        var settled = await m_rig.SettleAsync(issued, [.. honest, liar]);

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts().Values, Is.All.EqualTo("Fail"), "one node does not rewrite the report");
            Assert.That(settled.ReportCount("rejected_results"), Is.EqualTo(1));
            Assert.That(settled.Rejected(VerifyRoundIntake.CONTENT), Is.EqualTo(1));
            Assert.That(settled.ReportCount("disputes"), Is.Zero, "a refused package is no execution to disagree with");
        });
    }

    [Test]
    public async Task H2AResultUnderATicketOfAnotherPackageIsRefusedTest()
    {
        // The liar holds package 0 and answers for the tasks of package 1 under the ticket it knows.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0, 1), VerifySettleRig.Batch(2, 3));
        var honest = new[] { await m_rig.AnswerAsync(issued[0], Fail), await m_rig.AnswerAsync(issued[1], Fail) };

        var forged = await m_rig.AnswerAsync(issued[1], Pass, batches =>
        {
            foreach (var batch in batches)
                batch.Ticket = issued[0].Ticket;
        });
        forged.Ticket = issued[0].Ticket;

        // And a result under a ticket the server never gave out.
        var invented = await m_rig.AnswerAsync(issued[1], Pass);
        invented.Ticket = VerifyTickets.New();

        var settled = await m_rig.SettleAsync(issued, [.. honest, forged, invented]);

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts().Values, Is.All.EqualTo("Fail"));
            Assert.That(settled.Rejected(VerifyRoundIntake.CONTENT), Is.EqualTo(1), "the tasks under the ticket are not the package's");
            Assert.That(settled.Rejected(VerifyRoundIntake.UNKNOWN_TICKET), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task H2ADroppedTaskIsReportedNotLostTest()
    {
        // Scenario 3: a node returns fewer results than tasks. The tasks vanished and the total shrank.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0, 1, 2));
        var short_ = await m_rig.AnswerAsync(issued[0], Pass, batches => batches[0].Results.RemoveAt(1));

        var unanswered = await m_rig.SettleAsync(issued, [short_]);
        Assert.Multiple(() =>
        {
            Assert.That(unanswered.ReportCount("total"), Is.EqualTo(3), "the total does not shrink");
            Assert.That(unanswered.Verdicts().Values, Is.All.EqualTo("NoResult"));
            Assert.That(unanswered.Lines()[1].GetProperty("reason").GetString(), Is.EqualTo(VerifyFault.NO_ANSWER));
            Assert.That(unanswered.Rejected(VerifyRoundIntake.CONTENT), Is.EqualTo(1));
            Assert.That(unanswered.IntegrityCount("no_result"), Is.EqualTo(3));
        });

        // Sent out once more to a node that does the work, every task has its verdict.
        var repairs = await m_rig.RepairsAsync(issued, [short_]);
        Assert.That(repairs.Sum(package => package.TaskCount), Is.EqualTo(3));

        var repaired = await m_rig.SettleAsync((issued, [short_]), (repairs, [await m_rig.AnswerAsync(repairs[0], Fail)]), null);
        Assert.Multiple(() =>
        {
            Assert.That(repaired.Verdicts().Values, Is.All.EqualTo("Fail"));
            Assert.That(repaired.IntegrityCount("repairs"), Is.EqualTo(3));
            Assert.That(repaired.IntegrityCount("no_result"), Is.Zero);
        });
    }

    [Test]
    public async Task H2ACandidateAddedForAnIndexIsRefusedTest()
    {
        // Scenario 4 (generate and check): a node adds a second, empty candidate for an index. The
        // check ran on the honest candidate while the corpus took the smaller one.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0, 1));
        var padded = await m_rig.AnswerAsync(issued[0], Generated, batches =>
            batches[0].Results.Add(new VerifyResultData { TaskIndex = 0, Verdict = VerifyVerdict.Pass, Stdout = "" }));
        var honest = await m_rig.AnswerAsync(issued[0], Generated);

        var settled = await m_rig.SettleAsync(issued, [padded, honest]);

        Assert.Multiple(() =>
        {
            Assert.That(settled.Rejected(VerifyRoundIntake.CONTENT), Is.EqualTo(1));
            Assert.That(settled.Lines()[0].GetProperty("stdout").GetString(), Is.EqualTo("honest 0\n"));
            Assert.That(OutWit.Controller.AI.Verify.Tasksets.VerifyCheckTaskBuilder.ReadCandidates(settled.LedgerPath).Select(candidate => candidate.Stdout), Is.EqualTo(new[] { "honest 0\n", "honest 1\n" }),
                "what is checked is what the corpus holds: one settled candidate a generator");
        });
    }

    [Test]
    public async Task H2AMalformedPackageFailsThatPackageNotTheRunTest()
    {
        // Scenario 5, and the decode half of H-1: a blob that is no package, a package that ends
        // mid-frame, and a few kilobytes that unpack to a gigabyte.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1), VerifySettleRig.Batch(2), VerifySettleRig.Batch(3));
        var honest = await m_rig.AnswerAsync(issued[0], Fail);

        var garbage = await m_rig.StoreRawAsync("not a package at all"u8.ToArray(), issued[1].Ticket);
        var whole = await File.ReadAllBytesAsync(await m_rig.Blobs.GetLocalPathAsync(honest.ResultsBlobId));
        var cut = await m_rig.StoreRawAsync(whole[..(VerifyPackageCodec.HEADER_LENGTH + 3)], issued[2].Ticket);
        var bomb = await m_rig.StoreRawAsync(Bomb(1L << 30), issued[3].Ticket);

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var settled = await m_rig.SettleAsync(issued, [honest, garbage, cut, bomb]);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Fail"), "the honest package stands");
            Assert.That(new[] { 1, 2, 3 }.Select(index => settled.Verdicts()[index]), Is.All.EqualTo("NoResult"));
            Assert.That(settled.ReportCount("rejected_results"), Is.EqualTo(3));
            Assert.That(settled.Rejected(VerifyRoundIntake.UNREADABLE), Is.GreaterThanOrEqualTo(2), "a package cut short may also read as one without its tasks");
            Assert.That(allocated, Is.LessThan(256L << 20), "a gigabyte that is claimed is not a gigabyte that is read");
        });
    }

    [Test]
    public async Task H2AResultOfAnotherVersionEngineOrRuntimeIsRefusedTest()
    {
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1), VerifySettleRig.Batch(2), VerifySettleRig.Batch(3), VerifySettleRig.Batch(4));
        var returned = new[]
        {
            await m_rig.AnswerAsync(issued[0], Pass, batches => batches[0].Diagnostics!.ControllerVersion = "0.12.0"),
            await m_rig.AnswerAsync(issued[1], Pass, batches => batches[0].Diagnostics!.WasmtimeVersion = "48.0.2.0 (native unpinned 0123456789ab)"),
            await m_rig.AnswerAsync(issued[2], Pass, batches => batches[0].Diagnostics!.RuntimeSha256 = new string('0', 64)),
            await m_rig.AnswerAsync(issued[3], Pass, batches => batches[0].Diagnostics!.RuntimeTarget = @"C:\Users\someone\node"),
            await m_rig.AnswerAsync(issued[4], Pass)
        };

        var settled = await m_rig.SettleAsync(issued, returned);

        Assert.Multiple(() =>
        {
            Assert.That(settled.Rejected(VerifyRoundIntake.VERSION), Is.EqualTo(4), settled.Report);
            Assert.That(settled.Verdicts()[4], Is.EqualTo("Pass"));
            Assert.That(settled.Provenance, Does.Not.Contain("someone"), "a node's own words do not reach the provenance");
        });
    }

    [Test]
    public async Task H2APackageWhoseBytesAreNotTheOnesNamedIsRefusedTest()
    {
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0));
        var wrongHash = await m_rig.AnswerAsync(issued[0], Pass);
        wrongHash.Sha256 = new string('a', 64);

        var settled = await m_rig.SettleAsync(issued, [wrongHash]);

        Assert.Multiple(() =>
        {
            Assert.That(settled.Rejected(VerifyRoundIntake.UNREADABLE), Is.EqualTo(1));
            Assert.That(settled.Verdicts()[0], Is.EqualTo("NoResult"));
        });
    }

    [Test]
    public async Task H2APackageReturnedTwiceCountsOnceTest()
    {
        // A node that holds two packages answers one of them twice: the same blob under one ticket.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1));
        var forged = await m_rig.AnswerAsync(issued[0], Pass);
        var honest = await m_rig.AnswerAsync(issued[0], Fail);

        var settled = await m_rig.SettleAsync(issued, [forged, forged.Clone(), honest]);

        Assert.Multiple(() =>
        {
            Assert.That(settled.Rejected(VerifyRoundIntake.DUPLICATE), Is.EqualTo(1));
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Disputed"), "one machine's answer given twice is not two machines: the honest copy still disputes it");
            Assert.That(settled.Verdicts()[1], Is.EqualTo("NoResult"), "and the package whose answer was spent on it is reported as unanswered");
        });
    }

    [Test]
    public async Task H2MoreAnswersThanCopiesAreNeverAgreementTest()
    {
        // The same forged result stored twice (different blobs) and the honest one: three answers
        // where two copies were asked for. Two of them agree, and it settles nothing.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0));
        var returned = new[] { await m_rig.AnswerAsync(issued[0], Pass, Vary(1)), await m_rig.AnswerAsync(issued[0], Pass, Vary(2)), await m_rig.AnswerAsync(issued[0], Fail) };

        var first = await m_rig.SettleAsync(issued, returned);
        Assert.That(first.Verdicts()[0], Is.EqualTo("Disputed"));

        // In the dispute round the liar answers twice again: four answers where three were asked for.
        var disputes = await m_rig.DisputesAsync(issued, returned);
        var votes = new[]
        {
            await m_rig.AnswerAsync(disputes[0], Fail), await m_rig.AnswerAsync(disputes[0], Fail, Vary(3)),
            await m_rig.AnswerAsync(disputes[0], Pass, Vary(4)), await m_rig.AnswerAsync(disputes[0], Pass, Vary(5))
        };

        var settled = await m_rig.SettleAsync((issued, returned), ([], []), (disputes, votes));

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Disputed"));
            Assert.That(settled.Rejected(VerifyRoundIntake.SURPLUS), Is.EqualTo(4), "which of them is the answer cannot be told, so none is");
        });
    }

    [Test]
    public async Task H2ASecondAnswerOfOneMachineDoesNotWinADisputeTest()
    {
        // Three machines and one liar. One honest copy of the first package does not come back,
        // and the liar spends its answer to the second package on the first: two forged answers
        // against one honest one, in a package that has its three answers like any other.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1), VerifySettleRig.Batch(2));
        var first = new List<VerifyResultPackageData>();
        foreach (var package in issued)
        {
            first.Add(await m_rig.AnswerAsync(package, Fail));
            first.Add(await m_rig.AnswerAsync(package, Pass));
        }

        var disputes = await m_rig.DisputesAsync(issued, first);
        Assert.That(disputes, Has.Count.EqualTo(3));

        var votes = new[]
        {
            await m_rig.AnswerAsync(disputes[0], Fail), await m_rig.AnswerAsync(disputes[0], Pass, Vary(1)), await m_rig.AnswerAsync(disputes[0], Pass, Vary(2)),
            await m_rig.AnswerAsync(disputes[1], Fail), await m_rig.AnswerAsync(disputes[1], Fail, Vary(3)),
            await m_rig.AnswerAsync(disputes[2], Fail), await m_rig.AnswerAsync(disputes[2], Fail, Vary(4)), await m_rig.AnswerAsync(disputes[2], Fail, Vary(5))
        };

        var settled = await m_rig.SettleAsync((issued, first), ([], []), (disputes, votes));
        var outcomes = settled.DisputeOutcomes();

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Disputed"), "two answers for Pass are not two machines: the round is one answer short elsewhere");
            Assert.That(outcomes[0], Is.EqualTo(VerifyRulings.SURPLUS_COPIES));
            Assert.That(settled.Verdicts()[1], Is.EqualTo("Fail"), "the package that is short itself cannot have been given an answer to spare");
            Assert.That(settled.Verdicts()[2], Is.EqualTo("Fail"), "three that agree are two machines even with one left out");
            Assert.That(outcomes[1], Is.EqualTo(VerifyRulings.RESOLVED));
            Assert.That(outcomes[2], Is.EqualTo(VerifyRulings.RESOLVED));
        });
    }

    [Test]
    public async Task H2WithAnswersMissingAcrossARoundNothingIsSettledByTwoTest()
    {
        // Two machines, three disputed packages, three copies asked: no package can have its
        // three. The liar spends its answer to the second package on the first.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0), VerifySettleRig.Batch(1), VerifySettleRig.Batch(2));
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
            await m_rig.AnswerAsync(disputes[1], Fail),
            await m_rig.AnswerAsync(disputes[2], Fail), await m_rig.AnswerAsync(disputes[2], Fail, Vary(3))
        };

        var settled = await m_rig.SettleAsync((issued, first), ([], []), (disputes, votes));

        Assert.Multiple(() =>
        {
            Assert.That(settled.Verdicts()[0], Is.EqualTo("Disputed"), "two forged answers against one honest one do not win");
            Assert.That(settled.Verdicts()[1], Is.EqualTo("Disputed"), "one answer is not two machines");
            Assert.That(settled.Verdicts()[2], Is.EqualTo("Disputed"), "two that agree may be one machine twice while the round is short of answers elsewhere");
            Assert.That(settled.ReportCount("pass"), Is.Zero);
        });
    }

    [Test]
    public async Task H2OneDisputedPackageIsSettledByTheTwoCopiesThatCameBackTest()
    {
        // A round of one package has no other package to take an answer from.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0));
        var first = new[] { await m_rig.AnswerAsync(issued[0], Fail), await m_rig.AnswerAsync(issued[0], Pass) };
        var disputes = await m_rig.DisputesAsync(issued, first);

        var votes = new[] { await m_rig.AnswerAsync(disputes[0], Fail), await m_rig.AnswerAsync(disputes[0], Fail, Vary(1)) };
        var settled = await m_rig.SettleAsync((issued, first), ([], []), (disputes, votes));

        Assert.That(settled.Verdicts()[0], Is.EqualTo("Fail"));
    }

    #endregion

    #region L-8 Tests

    [Test]
    public async Task L8ExecutionsThatDifferInAnEarlierCaseAreADisputeWhateverTheOrderTest()
    {
        // Two executions equal in verdict, exit code and the task's own streams, different in what
        // the first case printed: the pick took whichever arrived first.
        var issued = await m_rig.PublishAsync(VerifySettleRig.Batch(0));
        var one = await m_rig.AnswerAsync(issued[0], task => WithCases(task, "alpha"));
        var other = await m_rig.AnswerAsync(issued[0], task => WithCases(task, "beta"));

        var forward = await m_rig.SettleAsync(issued, [one, other]);
        var backward = await m_rig.SettleAsync(issued, [other, one]);

        Assert.Multiple(() =>
        {
            Assert.That(forward.Verdicts()[0], Is.EqualTo("Disputed"));
            Assert.That(backward.Report, Is.EqualTo(forward.Report), "the report does not depend on the order the executions came back in");
            Assert.That(backward.Outputs, Is.EqualTo(forward.Outputs));
            Assert.That(backward.Integrity, Is.EqualTo(forward.Integrity));
            Assert.That(forward.Integrity, Does.Contain("\"differs\":[\"cases\"]"));
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

    private static VerifyResultData Generated(VerifyTaskData task)
    {
        return Result(task, VerifyVerdict.Pass, $"honest {task.TaskIndex}\n");
    }

    private static VerifyResultData Result(VerifyTaskData task, VerifyVerdict verdict, string stdout)
    {
        return new VerifyResultData { TaskIndex = task.TaskIndex, Verdict = verdict, ExitCode = verdict == VerifyVerdict.Pass ? 0 : 1, Stdout = stdout, FuelConsumed = 100, WallMs = 3 };
    }

    private static VerifyResultData WithCases(VerifyTaskData task, string firstCase)
    {
        return new VerifyResultData
        {
            TaskIndex = task.TaskIndex,
            Verdict = VerifyVerdict.Pass,
            Stdout = "last\n",
            CaseResults =
            [
                new VerifyCaseResultData { CaseIndex = 0, Passed = true, Verdict = VerifyVerdict.Pass, ActualStdout = firstCase },
                new VerifyCaseResultData { CaseIndex = 1, Passed = true, Verdict = VerifyVerdict.Pass, ActualStdout = "last\n" }
            ]
        };
    }

    /// <summary>Changes a cost of the answer, so that two packages with the same results are different blobs.</summary>
    private static Action<List<VerifyResultBatchData>> Vary(int by)
    {
        return batches => batches[0].Diagnostics!.WallMs += by;
    }

    /// <summary>A result package whose stream unpacks to a number of zero bytes: kilobytes that claim a frame of that size.</summary>
    private static byte[] Bomb(long unpacked)
    {
        using var stream = new MemoryStream();
        VerifyPackageCodec.WriteHeader(stream, VerifyPackageCodec.KIND_RESULTS, VerifyPackageCodec.RESULTS_VERSION);
        using (var brotli = new BrotliStream(stream, CompressionLevel.Optimal, leaveOpen: true))
        {
            var zeros = new byte[1 << 20];
            for (long written = 0; written < unpacked; written += zeros.Length)
                brotli.Write(zeros);
        }

        return stream.ToArray();
    }

    #endregion
}
