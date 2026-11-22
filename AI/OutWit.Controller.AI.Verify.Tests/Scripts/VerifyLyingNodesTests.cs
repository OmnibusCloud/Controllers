using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OutWit.Controller.AI.Verify.Delivery;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Settlement;
using OutWit.Controller.AI.Verify.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Scripts;

/// <summary>
/// The bundled script against a node that lies, end to end through the engine: three in-process
/// nodes really run the taskset, and the first of them returns something else in place of every
/// result package it made. Each test is one way of lying. In every one the job completes, no
/// task takes the liar's word, and the documents say what happened.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyLyingNodesTests : VerifyScriptTestBase
{
    #region Constants

    private const int TASKS = 12;

    #endregion

    #region Fields

    private string m_script = null!;
    private VerifyLyingNodesManager m_nodes = null!;
    private Guid m_taskset;

    #endregion

    #region Setup

    protected override IWitNodesManager CreateNodesManager(IWitEngineNode node)
    {
        return m_nodes = new VerifyLyingNodesManager(node);
    }

    [OneTimeSetUp]
    public async Task Setup()
    {
        m_script = LoadScript("VerifyRun.wit");

        // Even tasks print their index and pass their suite; odd tasks fail theirs.
        var taskset = new StringBuilder();
        for (var index = 0; index < TASKS; index++)
        {
            var expected = index % 2 == 0 ? index : -1;
            taskset.AppendLine($$"""{"index":{{index}},"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"print({{index}})"},"suite":[{"expected_stdout":"{{expected}}\n"}]}""");
        }

        m_taskset = await UploadAsync(taskset.ToString(), "taskset.jsonl");
    }

    [SetUp]
    public void Reset()
    {
        m_nodes.Lie = null;
        m_nodes.Lies = 0;
        m_nodes.Second = null;
        m_nodes.SecondAnswers = 0;
        m_nodes.NoValues = 0;
        m_nodes.NoValuesSent = 0;
    }

    #endregion

    #region Tests

    [Test]
    public async Task AnHonestRunHasNothingToReportTest()
    {
        var run = await RunAsync(sampling: 1.0);

        Assert.Multiple(() =>
        {
            AssertHonestVerdicts(run);
            Assert.That(run.Summary("rejected_results"), Is.Zero);
            Assert.That(run.Summary("disputes"), Is.Zero);
            Assert.That(run.Integrity("sampled"), Is.EqualTo(TASKS));
            Assert.That(run.Integrity("mismatches"), Is.Zero);
            Assert.That(run.Integrity("repairs"), Is.Zero);
            Assert.That(run.Word("sampling"), Is.EqualTo(VerifySampling.AS_ASKED));
            Assert.That(run.Summary("unanswered_packages"), Is.Zero);
        });
    }

    [TestCase(1.0)]
    [TestCase(0.5)]
    [TestCase(0.34)]
    [TestCase(0.05)]
    public async Task AnHonestRunGetsTheSamplingItAskedForTest(double rate)
    {
        // The settlement counts how many packages a rate asks for by the rule the platform
        // selects by. Held to the platform here: an honest fan-out comes back with exactly that
        // many packages answered twice, at every rate, and the documents say "as asked".
        var run = await RunAsync(rate);
        var (packages, copies, selected, complete, unanswered) = run.FanOut(VerifyRound.MAIN);

        Assert.Multiple(() =>
        {
            AssertHonestVerdicts(run);
            Assert.That(packages, Is.EqualTo(TASKS / 2), "two tasks a package");
            Assert.That(copies, Is.EqualTo(2));
            Assert.That(selected, Is.EqualTo(VerifySampling.Selected(packages, rate)));
            Assert.That(complete, Is.EqualTo(selected), "the platform ran as many packages twice as the settlement expects of the rate");
            Assert.That(unanswered, Is.Zero);
            Assert.That(run.Word("sampling"), Is.EqualTo(VerifySampling.AS_ASKED));
            Assert.That(run.Rate(), Is.EqualTo(rate));
            Assert.That(run.Integrity("sampled"), Is.EqualTo(2 * selected), "two tasks of every package that ran twice");
            Assert.That(run.Summary("rejected_results") + run.Summary("disputes") + run.Summary("unanswered_packages"), Is.Zero);
        });
    }

    [Test]
    public async Task ANodeThatAnswersWithNoValueAndLiesInTheRepairGainsNothingTest()
    {
        // The delta audit's finding N-4. The liar answers the first package it holds with a
        // variable that has no value, and from then on says Pass for everything. Under Grid
        // 1.3.0 the one answer of no value empties the results of the whole first fan-out:
        // every task goes out once more, and the liar takes its share of that round. That round
        // used to run once a package, so the liar's answers in it stood (two false passes of
        // twelve in the audit's run) and no document said anything. It now runs under the
        // sampling of the first, so the liar's copies meet honest ones and are outvoted. A Grid
        // that leaves the answer out instead hands over a fan-out one answer short, and the
        // honest copy of that package stands. Either way no task takes the liar's word, and the
        // documents say that the sampling that was asked for did not all happen.
        m_nodes.NoValues = 1;
        m_nodes.Lie = honest => ForgeAsync(honest, batches =>
        {
            foreach (var result in batches.SelectMany(batch => batch.Results))
            {
                result.Verdict = VerifyVerdict.Pass;
                result.ExitCode = 0;
                result.Stdout = "";
                foreach (var item in result.CaseResults)
                {
                    item.Passed = true;
                    item.Verdict = VerifyVerdict.Pass;
                    item.ActualStdout = "";
                }
            }
        });

        var run = await RunAsync(sampling: 1.0);
        var (packages, _, selected, complete, _) = run.FanOut(VerifyRound.MAIN);

        Assert.Multiple(() =>
        {
            Assert.That(m_nodes.NoValuesSent, Is.EqualTo(1));
            AssertHonestVerdicts(run);
            Assert.That(run.Summary("pass"), Is.EqualTo(TASKS / 2), "no false pass");
            Assert.That(run.Outputs[0].GetProperty("cases")[0].GetProperty("stdout").GetString(), Is.EqualTo("0" + (char)10), "the outputs are the honest ones");
            Assert.That(run.Summary("disputes_resolved"), Is.EqualTo(run.Summary("disputes")), "two honest machines of three settle every dispute");

            Assert.That(selected, Is.EqualTo(packages), "every package was asked for twice");
            Assert.That(complete, Is.LessThan(selected), "and not every one came back with two answers");
            Assert.That(run.Word("sampling"), Is.EqualTo(VerifySampling.SHORT), "the report says so in a word");
            Assert.That(run.IntegrityWord("sampling"), Is.EqualTo(VerifySampling.SHORT), "and so does the integrity document");
            Assert.That(run.Integrity("single_execution") + run.Integrity("agreed"), Is.EqualTo(TASKS), "every task has a result");
        });
    }

    [Test]
    public async Task ANodeThatSaysPassIsOutvotedTest()
    {
        // Wrong verdict: every result of the liar says Pass with nothing printed.
        m_nodes.Lie = honest => ForgeAsync(honest, batches =>
        {
            foreach (var result in batches.SelectMany(batch => batch.Results))
            {
                result.Verdict = VerifyVerdict.Pass;
                result.ExitCode = 0;
                result.Stdout = "";
                foreach (var item in result.CaseResults)
                {
                    item.Passed = true;
                    item.Verdict = VerifyVerdict.Pass;
                    item.ActualStdout = "";
                }
            }
        });

        var run = await RunAsync(sampling: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(m_nodes.Lies, Is.GreaterThan(0));
            AssertHonestVerdicts(run);
            Assert.That(run.Summary("disputes"), Is.GreaterThan(0), "the liar's copies disagree with the honest ones");
            Assert.That(run.Summary("disputes_resolved"), Is.EqualTo(run.Summary("disputes")), "two honest machines of three settle every one");
            Assert.That(run.Summary("pass"), Is.EqualTo(TASKS / 2));
            Assert.That(run.Outputs[0].GetProperty("cases")[0].GetProperty("stdout").GetString(), Is.EqualTo("0\n"), "the outputs are the honest ones");
        });
    }

    [Test]
    public async Task APackageUnderAWrongHashIsRefusedTest()
    {
        m_nodes.Lie = honest =>
        {
            var forged = honest.Clone();
            forged.Sha256 = new string('0', 64);
            return Task.FromResult(forged);
        };

        await AssertRefusedAsync(VerifyRoundIntake.UNREADABLE);
    }

    [Test]
    public async Task AResultForATaskOfAnotherPackageIsRefusedTest()
    {
        // Foreign index: the liar adds a Pass for a task it was never given.
        m_nodes.Lie = honest => ForgeAsync(honest, batches =>
        {
            var held = batches.SelectMany(batch => batch.Results).Select(result => result.TaskIndex).ToHashSet();
            var foreign = Enumerable.Range(0, TASKS).First(index => !held.Contains(index));
            batches[0].Results.Add(new VerifyResultData { TaskIndex = foreign, Verdict = VerifyVerdict.Pass });
        });

        await AssertRefusedAsync(VerifyRoundIntake.CONTENT);
    }

    [Test]
    public async Task AResultThatClaimsAnotherVersionIsRefusedTest()
    {
        m_nodes.Lie = honest => ForgeAsync(honest, batches =>
        {
            foreach (var batch in batches)
                batch.Diagnostics!.ControllerVersion = "0.12.0";
        });

        await AssertRefusedAsync(VerifyRoundIntake.VERSION);
    }

    [Test]
    public async Task AMalformedPackageFailsThatPackageNotTheRunTest()
    {
        m_nodes.Lie = async honest =>
        {
            var bytes = Encoding.UTF8.GetBytes("this is no result package");
            var forged = honest.Clone();
            forged.ResultsBlobId = await BlobService.UploadBytesAsync(bytes, VerifyPackageCodec.RESULTS_FILE_NAME);
            forged.Sha256 = VerifyPackageCodec.Hash(bytes);
            forged.SizeBytes = bytes.Length;
            return forged;
        };

        await AssertRefusedAsync(VerifyRoundIntake.UNREADABLE);
    }

    [Test]
    public async Task APackageReturnedForTwoItemsCountsOnceTest()
    {
        // Duplicated package: the liar answers every package it holds with the first result it made.
        VerifyResultPackageData? first = null;
        m_nodes.Lie = honest => Task.FromResult((first ??= honest).Clone());

        var run = await RunAsync(sampling: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(m_nodes.Lies, Is.GreaterThan(1));
            AssertHonestVerdicts(run);
            Assert.That(run.Rejected(VerifyRoundIntake.DUPLICATE) + run.Rejected(VerifyRoundIntake.UNKNOWN_TICKET), Is.GreaterThan(0),
                "the same blob again in one fan-out is a duplicate; carried into a later one it is under a ticket that fan-out never gave");
            Assert.That(run.Summary("rejected_results"), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task ASecondAnswerForOnePackageNeverReachesTheServerTest()
    {
        // The liar answers every package it holds, and the first package of each batch twice: a
        // second result package of its own making, sent under that package's return variable.
        // Grid keeps one value for each return variable a node's tasks asked for (Grid 1.3.0), so
        // the server receives what an honest run returns. Without that rule the sampled package
        // would have three answers where two were asked for, and its tasks would go to a dispute.
        m_nodes.Second = honest => ForgeAsync(honest, _ => { });

        var run = await RunAsync(sampling: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(m_nodes.SecondAnswers, Is.GreaterThan(0));
            AssertHonestVerdicts(run);
            Assert.That(run.Summary("rejected_results"), Is.Zero, "no answer too many came to be refused");
            Assert.That(run.Summary("disputes"), Is.Zero, "no package had more answers than were asked for");
            Assert.That(run.Integrity("sampled"), Is.EqualTo(TASKS));
            Assert.That(run.Integrity("mismatches"), Is.Zero);
            Assert.That(run.Integrity("repairs"), Is.Zero);
        });
    }

    [Test]
    public async Task ASecondAnswerThatLiesIsLeftOutAndTheFirstStandsTest()
    {
        // The same, with a second answer that says Pass for everything. It is the value that
        // comes second under its return variable, and it is the one left out.
        m_nodes.Second = honest => ForgeAsync(honest, batches =>
        {
            foreach (var result in batches.SelectMany(batch => batch.Results))
            {
                result.Verdict = VerifyVerdict.Pass;
                result.ExitCode = 0;
                result.Stdout = "";
                foreach (var item in result.CaseResults)
                {
                    item.Passed = true;
                    item.Verdict = VerifyVerdict.Pass;
                    item.ActualStdout = "";
                }
            }
        });

        var run = await RunAsync(sampling: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(m_nodes.SecondAnswers, Is.GreaterThan(0));
            AssertHonestVerdicts(run);
            Assert.That(run.Summary("disputes"), Is.Zero);
            Assert.That(run.Summary("rejected_results"), Is.Zero);
        });
    }

    [Test]
    public async Task ADroppedTaskIsNoticedAndAnsweredByAnotherNodeTest()
    {
        m_nodes.Lie = honest => ForgeAsync(honest, batches => batches[0].Results.RemoveAt(0));

        await AssertRefusedAsync(VerifyRoundIntake.CONTENT);
    }

    [Test]
    public async Task WithoutSamplingADroppedTaskIsRepairedOrReportedNeverLostTest()
    {
        // No second copy to fall back on: what the liar held goes out once more, and what it
        // drops again is reported as a task without a result.
        m_nodes.Lie = honest => ForgeAsync(honest, batches => batches[0].Results.RemoveAt(0));

        var run = await RunAsync(sampling: 0);

        Assert.Multiple(() =>
        {
            Assert.That(m_nodes.Lies, Is.GreaterThan(0));
            Assert.That(run.Summary("total"), Is.EqualTo(TASKS), "no task is lost");
            Assert.That(run.Integrity("repairs"), Is.GreaterThan(0));
            Assert.That(run.Summary("rejected_results"), Is.GreaterThan(0));

            foreach (var (index, verdict) in run.Verdicts)
                Assert.That(verdict, Is.EqualTo(Honest(index)).Or.EqualTo("NoResult"), $"task {index}");

            foreach (var (index, line) in run.Outputs.Where(line => run.Verdicts[line.Key] == "NoResult"))
                Assert.That(line.GetProperty("reason").GetString(), Is.EqualTo(VerifyFault.NO_ANSWER), $"task {index}");

            Assert.That(run.Summary("pass"), Is.EqualTo(run.Verdicts.Count(pair => pair.Value == "Pass")));
        });
    }

    #endregion

    #region Tools

    private static string Honest(int index)
    {
        return index % 2 == 0 ? "Pass" : "Fail";
    }

    private static void AssertHonestVerdicts(Run run)
    {
        Assert.That(run.Verdicts.Keys, Is.EquivalentTo(Enumerable.Range(0, TASKS)));
        foreach (var (index, verdict) in run.Verdicts)
            Assert.That(verdict, Is.EqualTo(Honest(index)), $"task {index}");
    }

    /// <summary>With every package run twice, the liar's copy is refused for the reason and the honest copy stands.</summary>
    private async Task AssertRefusedAsync(string reason)
    {
        var run = await RunAsync(sampling: 1.0);

        Assert.Multiple(() =>
        {
            Assert.That(m_nodes.Lies, Is.GreaterThan(0));
            AssertHonestVerdicts(run);
            Assert.That(run.Rejected(reason), Is.GreaterThan(0));
            Assert.That(run.Summary("rejected_results"), Is.EqualTo(run.Integrity("rejected_results")), "the report and the integrity document count the same");
            Assert.That(run.Summary("disputes"), Is.Zero, "a refused package is no execution to disagree with");
        });
    }

    /// <summary>What the liar makes of an honest result package: its batches changed, written and stored as a node stores them.</summary>
    private async Task<VerifyResultPackageData> ForgeAsync(VerifyResultPackageData honest, Action<List<VerifyResultBatchData>> lie)
    {
        var batches = VerifyResultPackageReader.Read(await BlobService.GetLocalPathAsync(honest.ResultsBlobId), int.MaxValue, int.MaxValue).ToList();
        lie(batches);

        var path = Path.Combine(Path.GetTempPath(), $"witai_verify_lie_{Guid.NewGuid():N}.results");
        try
        {
            using (var writer = new VerifyResultPackageWriter(path))
            {
                foreach (var batch in batches)
                    writer.Write(batch);

                writer.Complete();
            }

            return await VerifyPackageTransfer.UploadResultsAsync(BlobService, path, honest.Ticket, batches.Sum(batch => batch.Results.Count), batches.Count, NullLogger.Instance, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private async Task<Run> RunAsync(double sampling)
    {
        var job = Engine.Compile(m_script);
        var status = await Engine.ScheduleAndWaitAsync(job, m_taskset, new VerifyOptionsData { SamplingRate = sampling, BatchSize = 2 });
        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);

        return new Run(
            await ReadTextAsync((Guid)job.Variables["report"].Value!),
            await ReadTextAsync((Guid)job.Variables["integrity"].Value!),
            await ReadTextAsync((Guid)job.Variables["outputs"].Value!));
    }

    #endregion

    #region Nested Types

    /// <summary>The documents of one run.</summary>
    private sealed class Run
    {
        private readonly JsonElement m_report;
        private readonly JsonElement m_integrity;

        public Run(string report, string integrity, string outputs)
        {
            m_report = JsonDocument.Parse(report).RootElement.Clone();
            m_integrity = JsonDocument.Parse(integrity).RootElement.Clone();

            Verdicts = m_report.GetProperty("results").EnumerateArray()
                .ToDictionary(result => result.GetProperty("index").GetInt32(), result => result.GetProperty("verdict").GetString()!);
            Outputs = outputs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JsonDocument.Parse(line).RootElement.Clone())
                .ToDictionary(line => line.GetProperty("index").GetInt32());
        }

        public Dictionary<int, string> Verdicts { get; }

        public Dictionary<int, JsonElement> Outputs { get; }

        public int Summary(string name) => m_report.GetProperty("summary").GetProperty(name).GetInt32();

        public string? Word(string name) => m_report.GetProperty("summary").GetProperty(name).GetString();

        public string? IntegrityWord(string name) => m_integrity.GetProperty("summary").GetProperty(name).GetString();

        public double? Rate()
        {
            var rate = m_report.GetProperty("summary").GetProperty("sampling_rate");
            return rate.ValueKind == JsonValueKind.Null ? null : rate.GetDouble();
        }

        public (int Packages, int Copies, int Selected, int Complete, int Unanswered) FanOut(string round)
        {
            var fanOut = m_integrity.GetProperty("fan_outs").EnumerateArray().Single(item => item.GetProperty("round").GetString() == round);
            var selected = fanOut.GetProperty("selected");

            return (fanOut.GetProperty("packages").GetInt32(), fanOut.GetProperty("copies").GetInt32(), selected.ValueKind == JsonValueKind.Null ? -1 : selected.GetInt32(),
                fanOut.GetProperty("complete").GetInt32(), fanOut.GetProperty("unanswered").GetInt32());
        }

        public int Integrity(string name) => m_integrity.GetProperty("summary").GetProperty(name).GetInt32();

        public int Rejected(string reason) => m_integrity.GetProperty("summary").GetProperty("rejected").GetProperty(reason).GetInt32();
    }

    #endregion
}
