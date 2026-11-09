using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
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
        Assert.That(variables, Is.SupersetOf(new[] { "taskset", "opts", "batches", "rate", "checked", "results", "report", "provenance", "integrity" }));
    }

    [Test]
    public async Task SamplingReExecutesAndComparesTest()
    {
        // Every batch executed twice (rate 1): each pair agrees.
        var taskset = new StringBuilder();
        taskset.AppendLine("""{"index":0,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"print(2+2)"},"suite":[{"expected_stdout":"4\n"}]}""");
        taskset.AppendLine("""{"index":1,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"print('wrong')"},"suite":[{"expected_stdout":"right\n"}]}""");
        taskset.AppendLine("""{"index":2,"runtime":"quickjs-0.15.1","entry":"m.js","sources":{"m.js":"console.log(6*7)"}}""");
        taskset.AppendLine("""{"index":3,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"raise SystemExit(4)"}}""");

        var tasksetBlobId = await UploadAsync(taskset.ToString(), "taskset.jsonl");

        var job = Engine.Compile(m_verifyRunScript);
        var status = await Engine.ScheduleAndWaitAsync(job, tasksetBlobId, new VerifyOptionsData { SamplingRate = 1.0, BatchSize = 2 });

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        using var integrity = await ReadJsonAsync((Guid)job.Variables["integrity"].Value!);
        var summary = integrity.RootElement.GetProperty("summary");
        var pairs = integrity.RootElement.GetProperty("pairs").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("sampled").GetInt32(), Is.EqualTo(4));
            Assert.That(summary.GetProperty("compared").GetInt32(), Is.EqualTo(4));
            Assert.That(summary.GetProperty("mismatches").GetInt32(), Is.Zero);
            Assert.That(pairs.Select(p => p.GetProperty("index").GetInt32()), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(pairs.All(p => p.GetProperty("match").GetBoolean()), Is.True);
            Assert.That(pairs[1].GetProperty("replica").GetProperty("verdict").GetString(), Is.EqualTo("Fail"));
        });

        // Eight executions of four tasks: the report still has one record for each.
        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        Assert.That(report.RootElement.GetProperty("summary").GetProperty("total").GetInt32(), Is.EqualTo(4));
        Assert.That(report.RootElement.GetProperty("results").GetArrayLength(), Is.EqualTo(4));
        Assert.That(((System.Collections.IEnumerable)job.Variables["results"].Value!).OfType<VerifyResultBatchData>().Sum(batch => batch.Results.Count), Is.EqualTo(8));

        // Rate 0 (the default) leaves the integrity document empty and the run untouched.
        var plainJob = Engine.Compile(m_verifyRunScript);
        var plainStatus = await Engine.ScheduleAndWaitAsync(plainJob, tasksetBlobId, new VerifyOptionsData());
        Assert.That(plainStatus.Result, Is.EqualTo(WitProcessingResult.Completed));
        using var empty = await ReadJsonAsync((Guid)plainJob.Variables["integrity"].Value!);
        Assert.That(empty.RootElement.GetProperty("summary").GetProperty("sampled").GetInt32(), Is.Zero);
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

        // Provenance: every task attributed to exactly one batch, and no machine named.
        using var provenance = await ReadJsonAsync((Guid)job.Variables["provenance"].Value!);
        var covered = provenance.RootElement.GetProperty("batches").EnumerateArray()
            .SelectMany(batch => batch.GetProperty("tasks").EnumerateArray().Select(task => task.GetInt32()))
            .OrderBy(index => index)
            .ToList();
        Assert.That(covered, Is.EqualTo(new[] { 0, 1, 2, 3 }));
        Assert.That(provenance.RootElement.TryGetProperty("nodes", out _), Is.False);
    }

    [Test]
    public async Task RunSpansTheRawWasiAndSympyRuntimesTest()
    {
        // One taskset over three runtimes: a hand-assembled WASI module, a sympy solve, and
        // plain Python — three batches, one report, a provenance that names each runtime.
        var taskset = new StringBuilder();
        taskset.AppendLine("""{"index":0,"runtime":"wasi","entry":"hello.wasm","sources":{"hello.wasm":""" + "\"" + VerifySelfCheck.HELLO_WASM_BASE64 + "\"" + """},"suite":[{"expected_stdout":"hello\n"}]}""");
        taskset.AppendLine("""{"index":1,"runtime":"python-3.14.6-sympy-1.14.0","entry":"m.py","sources":{"m.py":"import sympy\nx=sympy.symbols('x')\nprint(sympy.solve(x**2-4, x))"},"suite":[{"expected_stdout":"[-2, 2]\n"}]}""");
        taskset.AppendLine("""{"index":2,"runtime":"python-3.14.6","entry":"m.py","sources":{"m.py":"import sympy"}}""");

        var tasksetBlobId = await UploadAsync(taskset.ToString(), "taskset.jsonl");

        var job = Engine.Compile(m_verifyRunScript);
        var status = await Engine.ScheduleAndWaitAsync(job, tasksetBlobId, new VerifyOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));

        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        var verdictByIndex = report.RootElement.GetProperty("results").EnumerateArray()
            .ToDictionary(r => r.GetProperty("index").GetInt32(), r => r.GetProperty("verdict").GetString());

        using var provenance = await ReadJsonAsync((Guid)job.Variables["provenance"].Value!);
        var runtimes = provenance.RootElement.GetProperty("batches").EnumerateArray()
            .Select(batch => batch.GetProperty("runtime").GetString())
            .OrderBy(runtime => runtime, StringComparer.Ordinal)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(verdictByIndex[0], Is.EqualTo("Pass"), "the raw module");
            Assert.That(verdictByIndex[1], Is.EqualTo("Pass"), "sympy on its runtime");
            Assert.That(verdictByIndex[2], Is.EqualTo("RuntimeError"), "sympy is not on the plain runtime");
            Assert.That(runtimes, Is.EqualTo(new[] { "python-3.14.6", "python-3.14.6-sympy-1.14.0", "wasi" }));
            Assert.That(report.RootElement.GetProperty("summary").GetProperty("pass").GetInt32(), Is.EqualTo(2));
        });
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
