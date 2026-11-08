using System.Diagnostics;
using System.Text;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Load;

/// <summary>
/// Load on the host side (Category=Load): a hundred thousand tasks through the parser and the
/// planner, a hundred thousand results through every writer. No sandbox involved; these are
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
    public void AHundredThousandResultsThroughEveryWriterTest()
    {
        var primaries = Batches(0, TASKS, "alpha");
        var replicas = Batches(0, SAMPLED, "beta");
        var allResults = primaries.SelectMany(b => b.Results).ToList();

        var stopwatch = Stopwatch.StartNew();
        var report = VerifyReportWriter.Write(allResults);
        var reportTime = stopwatch.Elapsed;
        var corpus = VerifyCorpusWriter.Write(allResults);
        var corpusTime = stopwatch.Elapsed - reportTime;
        var provenance = VerifyProvenanceWriter.Write(primaries);
        var provenanceTime = stopwatch.Elapsed - reportTime - corpusTime;
        var integrity = VerifyIntegrityWriter.Write(primaries, replicas);
        stopwatch.Stop();

        TestContext.Out.WriteLine(
            $"report {report.Length / 1024} KB in {reportTime.TotalSeconds:F1} s; corpus {corpus.Length / 1024} KB in {corpusTime.TotalSeconds:F1} s; " +
            $"provenance {provenance.Length / 1024} KB in {provenanceTime.TotalSeconds:F1} s; integrity {integrity.Length / 1024} KB; total {stopwatch.Elapsed.TotalSeconds:F1} s");

        Assert.Multiple(() =>
        {
            Assert.That(stopwatch.Elapsed, Is.LessThan(BUDGET));
            Assert.That(report.Length, Is.LessThan(TASKS * 200), "under 200 bytes per task");
            Assert.That(VerifyReportWriter.ReadSummary(report).Total, Is.EqualTo(TASKS));
        });
    }

    #endregion

    #region Helpers

    private static List<VerifyResultBatchData> Batches(int from, int count, string nodeName)
    {
        var nodeId = Guid.NewGuid();
        var batches = new List<VerifyResultBatchData>();
        for (var offset = from; offset < from + count; offset += BATCH_SIZE)
        {
            var results = new List<VerifyResultData>();
            for (var i = offset; i < Math.Min(offset + BATCH_SIZE, from + count); i++)
            {
                results.Add(new VerifyResultData
                {
                    TaskIndex = i,
                    Verdict = i % 7 == 0 ? VerifyVerdict.Fail : VerifyVerdict.Pass,
                    ExitCode = 0,
                    Stdout = $"{i}\n",
                    FuelConsumed = 120_000_000 + i,
                    PeakMemoryBytes = 40 * 1024 * 1024,
                    WallMs = 40,
                    CaseResults = [new VerifyCaseResultData { CaseIndex = 0, Passed = i % 7 != 0, Verdict = i % 7 == 0 ? VerifyVerdict.Fail : VerifyVerdict.Pass, ActualStdout = $"{i}\n" }]
                });
            }

            batches.Add(new VerifyResultBatchData
            {
                Results = results,
                Diagnostics = new VerifyBatchDiagnosticsData
                {
                    NodeId = nodeId, NodeName = nodeName, RuntimeTarget = "linux-x64", WasmtimeVersion = "44.0.0.0",
                    NativeLibraryPath = "(load context)", RuntimeId = "python-3.14.6", RuntimeSha256 = "aaa",
                    Degree = 2, TaskCount = results.Count, WallMs = 100, ControllerVersion = "1.0.0-test"
                }
            });
        }

        return batches;
    }

    #endregion
}
