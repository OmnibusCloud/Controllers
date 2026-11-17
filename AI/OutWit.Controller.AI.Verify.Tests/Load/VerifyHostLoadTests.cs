using System.Diagnostics;
using System.Text;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;
using OutWit.Controller.AI.Verify.Tests.Settlement;

namespace OutWit.Controller.AI.Verify.Tests.Load;

/// <summary>
/// Load on the host side (Category=Load): a hundred thousand tasks through the parser and the
/// planner, a hundred thousand results through the settlement into every document. No sandbox involved; these are
/// the activities the server runs, and they must stay in seconds and in kilobytes per task.
/// </summary>
[TestFixture]
[Category("Load")]
public sealed class VerifyHostLoadTests
{
    #region Constants

    private const int TASKS = 100_000;
    private const int BATCH_SIZE = 100;
    private const int SAMPLED = 10_000;
    private static readonly TimeSpan BUDGET = TimeSpan.FromSeconds(20);

    #endregion

    #region Tests

    [Test]
    public void AHundredThousandLinesParseAndPlanTest()
    {
        var jsonl = new StringBuilder(TASKS * 90);
        for (var i = 0; i < TASKS; i++)
            jsonl.Append("{\"index\":").Append(i).Append(",\"runtime\":\"").Append(i % 3 == 0 ? "quickjs-0.15.1" : "python-3.14.6")
                .Append("\",\"sources\":{\"m\":\"print(").Append(i).Append(")\"},\"suite\":[{\"expected_stdout\":\"").Append(i).Append("\\n\"}]}\n");

        var stopwatch = Stopwatch.StartNew();
        var parsed = VerifyTasksetParser.Parse(jsonl.ToString());
        var parseTime = stopwatch.Elapsed;
        var plan = VerifyTasksetPlanner.Plan(parsed.Tasks, new VerifyOptionsData { BatchSize = BATCH_SIZE });
        stopwatch.Stop();
        TestContext.Out.WriteLine($"parse {TASKS} lines: {parseTime.TotalSeconds:F1} s; plan: {(stopwatch.Elapsed - parseTime).TotalSeconds:F1} s; {plan.Batches.Count} batches");

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Errors, Is.Empty);
            Assert.That(parsed.Tasks, Has.Count.EqualTo(TASKS));
            Assert.That(plan.Batches.Sum(b => b.Tasks.Count), Is.EqualTo(TASKS));
            Assert.That(stopwatch.Elapsed, Is.LessThan(BUDGET));
        });
    }

    [Test]
    public async Task AHundredThousandResultsAreSettledIntoEveryDocumentTest()
    {
        // The server's whole pass: every result package taken in under its ticket, a tenth of
        // them twice, every task ruled on, the four documents written.
        using var rig = new VerifySettleRig();
        var batches = Enumerable.Range(0, TASKS / BATCH_SIZE)
            .Select(batch => VerifySettleRig.Batch(Enumerable.Range(batch * BATCH_SIZE, BATCH_SIZE).ToArray()))
            .ToArray();
        var issued = await rig.PublishAsync(batches);

        var returned = new List<VerifyResultPackageData>();
        foreach (var package in issued)
            returned.Add(await rig.AnswerAsync(package, Answer));
        foreach (var package in issued.Take(issued.Count * SAMPLED / TASKS))
            returned.Add(await rig.AnswerAsync(package, Answer));

        var stopwatch = Stopwatch.StartNew();
        var run = await rig.SettleAsync(issued, returned);
        stopwatch.Stop();

        TestContext.Out.WriteLine(
            $"{issued.Count} packages, {returned.Count} result packages settled in {stopwatch.Elapsed.TotalSeconds:F1} s; report {run.Report.Length / 1024} KB; " +
            $"outputs {run.Outputs.Length / 1024} KB; provenance {run.Provenance.Length / 1024} KB; integrity {run.Integrity.Length / 1024} KB");

        Assert.Multiple(() =>
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(BUDGET));
            Assert.That(run.Report.Length, Is.LessThan(TASKS * 200), "under 200 bytes per task");
            Assert.That(run.Summary.Total, Is.EqualTo(TASKS));
            Assert.That(run.Summary.RejectedResults, Is.Zero);
            Assert.That(run.IntegrityCount("sampled"), Is.EqualTo(SAMPLED));
            Assert.That(run.IntegrityCount("mismatches"), Is.Zero);
        });
    }

    #endregion

    #region Helpers

    private static VerifyResultData Answer(VerifyTaskData task)
    {
        var index = task.TaskIndex;
        return new VerifyResultData
        {
            TaskIndex = index,
            Verdict = index % 7 == 0 ? VerifyVerdict.Fail : VerifyVerdict.Pass,
            ExitCode = 0,
            Stdout = $"{index}\n",
            FuelConsumed = 120_000_000 + index,
            PeakMemoryBytes = 40 * 1024 * 1024,
            WallMs = 40,
            CaseResults = [new VerifyCaseResultData { CaseIndex = 0, Passed = index % 7 != 0, Verdict = index % 7 == 0 ? VerifyVerdict.Fail : VerifyVerdict.Pass, ActualStdout = $"{index}\n" }]
        };
    }

    #endregion
}
