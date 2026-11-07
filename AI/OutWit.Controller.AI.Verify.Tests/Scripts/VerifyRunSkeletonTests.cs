using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Scripts;

/// <summary>
/// The distributed gate: the bundled VerifyRun.wit script running through the engine
/// (Verify.Split → Grid.ForEach ⇒ Verify.ExecuteBatch → Verify.Collect + Verify.Provenance,
/// blob transport, mock in-process nodes) executes a real taskset in the sandbox and returns a
/// verdict report — the RLVR code-vs-tests pipeline end to end.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyRunSkeletonTests : VerifyScriptTestBase
{
    #region Fields

    private string m_verifyRunScript = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_verifyRunScript = LoadScript("VerifyRun.wit");
    }

    #endregion

    #region Tests

    [Test]
    public void BundledScriptCompilesTest()
    {
        var job = Engine.Compile(m_verifyRunScript);

        Assert.That(job, Is.Not.Null);
        Assert.That(job.Activities.Count, Is.GreaterThan(0));
        var variables = job.Variables.Select(variable => variable.Name).ToList();
        Assert.That(variables, Is.SupersetOf(new[] { "taskset", "opts", "batches", "results", "report", "provenance" }));
    }

    [Test]
    public async Task DistributedRunProducesVerdictReportTest()
    {
        // A taskset mixing a passing suite, a failing suite, a runtime error, and a JS task —
        // the whole thing fans out to the mock nodes and comes back as one verdict report.
        var taskset = new StringBuilder();
        taskset.AppendLine("""{"index":0,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"a=int(input());b=int(input());print(a+b)"},"suite":[{"stdin":"2\n3\n","expected_stdout":"5\n","expected_exit":0}]}""");
        taskset.AppendLine("""{"index":1,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"print('wrong')"},"suite":[{"expected_stdout":"right\n","expected_exit":0}]}""");
        taskset.AppendLine("""{"index":2,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"raise SystemExit(4)"}}""");
        taskset.AppendLine("""{"index":3,"runtime":"quickjs-0.15.1","entry":"m.js","sources":{"m.js":"console.log(6*7)"}}""");

        var tasksetBlobId = await UploadAsync(taskset.ToString(), "taskset.jsonl");

        var job = Engine.Compile(m_verifyRunScript);
        var status = await Engine.ScheduleAndWaitAsync(job, tasksetBlobId, new VerifyOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);

        var summary = report.RootElement.GetProperty("summary");
        Assert.That(summary.GetProperty("total").GetInt32(), Is.EqualTo(4));
        Assert.That(summary.GetProperty("pass").GetInt32(), Is.EqualTo(2), "task 0 (suite) and task 3 (js) pass");

        var verdictByIndex = report.RootElement.GetProperty("results").EnumerateArray()
            .ToDictionary(r => r.GetProperty("index").GetInt32(), r => r.GetProperty("verdict").GetString());
        Assert.That(verdictByIndex[0], Is.EqualTo("Pass"));
        Assert.That(verdictByIndex[1], Is.EqualTo("Fail"));
        Assert.That(verdictByIndex[2], Is.EqualTo("RuntimeError"));
        Assert.That(verdictByIndex[3], Is.EqualTo("Pass"));

        // Provenance: every task attributed to exactly one batch, every batch to a node.
        using var provenance = await ReadJsonAsync((Guid)job.Variables["provenance"].Value!);
        var covered = provenance.RootElement.GetProperty("batches").EnumerateArray()
            .SelectMany(batch => batch.GetProperty("tasks").EnumerateArray().Select(task => task.GetInt32()))
            .OrderBy(index => index)
            .ToList();
        Assert.That(covered, Is.EqualTo(new[] { 0, 1, 2, 3 }));
        Assert.That(provenance.RootElement.GetProperty("nodes").GetArrayLength(), Is.GreaterThan(0));
    }

    [Test]
    public async Task DeterministicRewardsAreReproducibleTest()
    {
        // The RLVR artifact: the same taskset yields the same verdicts on every run — what a
        // reward pipeline depends on.
        const string tasksetJson =
            """{"index":0,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"import math;print(sum(math.isqrt(i) for i in range(10000)))"},"suite":[{"expected_stdout":"661650\n","expected_exit":0}]}""";

        async Task<string> RunOnce()
        {
            var blobId = await UploadAsync(tasksetJson + "\n", "taskset.jsonl");
            var job = Engine.Compile(m_verifyRunScript);
            var status = await Engine.ScheduleAndWaitAsync(job, blobId, new VerifyOptionsData());
            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            return await ReadTextAsync((Guid)job.Variables["report"].Value!);
        }

        var first = await RunOnce();
        var second = await RunOnce();

        Assert.That(second, Is.EqualTo(first), "verdict report must be byte-identical across runs");
        Assert.That(first, Does.Contain("\"pass\":1"));
    }

    #endregion
}
