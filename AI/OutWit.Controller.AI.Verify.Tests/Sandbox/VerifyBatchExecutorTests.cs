using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// Batch-level behavior: a chunk of mixed poison and benign tasks completes with one
/// verdict per task (a Timeout must not poison the chunk), and concurrent execution over
/// the shared compiled module is order-independent and reproducible.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyBatchExecutorTests : VerifySandboxTestBase
{
    [Test]
    public void BatchCarriesDiagnosticsAndReportsProgressTest()
    {
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = OutWit.Controller.AI.Verify.Runtimes.VerifyRuntimeCatalog.QUICKJS_0_15_1,
            Tasks = [JsTask("console.log(1)", 0), JsTask("console.log(2)", 1), JsTask("console.log(3)", 2)]
        };
        var reports = new List<(int Done, int Total)>();

        var result = VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch, (done, total) =>
        {
            lock (reports)
                reports.Add((done, total));
        });

        var diagnostics = result.Diagnostics;
        Assert.That(diagnostics, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(diagnostics!.NodeId, Is.EqualTo(Guid.Empty), "a batch does not say which machine ran it");
            Assert.That(diagnostics.NodeName, Is.Empty, "a batch does not say which machine ran it");
            Assert.That(diagnostics.NativeLibraryPath, Is.Empty, "a path on the node can name its account");
            Assert.That(diagnostics.RuntimeId, Is.EqualTo(batch.RuntimeId));
            Assert.That(diagnostics.RuntimeSha256, Is.Not.Empty);
            Assert.That(diagnostics.Degree, Is.GreaterThanOrEqualTo(1));
            Assert.That(diagnostics.TaskCount, Is.EqualTo(3));
            Assert.That(diagnostics.WallMs, Is.GreaterThanOrEqualTo(0));
            Assert.That(diagnostics.WasmtimeVersion, Does.StartWith("48."));
            Assert.That(diagnostics.ControllerVersion, Is.Not.Empty);
            Assert.That(reports.Select(r => r.Done).OrderBy(d => d), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(reports.Select(r => r.Total), Is.All.EqualTo(3));
        });
    }

    [Test]
    public void MixedPoisonBatchCompletesWithPerTaskVerdictsTest()
    {
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = OutWit.Controller.AI.Verify.Runtimes.VerifyRuntimeCatalog.PYTHON_3_14_6,
            DefaultLimits = new VerifyLimitsData { FuelBudget = 5_000_000_000, WallTimeMs = 30_000 },
            Tasks =
            [
                PythonTask("print('one')", 0),
                PythonTask("while True: pass", 1),          // fuel timeout
                PythonTask("raise SystemExit(3)", 2),       // runtime error, exit 3
                PythonTask("print('four')", 3)
            ]
        };

        var result = VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch);

        Assert.That(result.Results, Has.Count.EqualTo(4));
        var byIndex = result.Results.ToDictionary(r => r.TaskIndex);
        Assert.That(byIndex[0].Verdict, Is.EqualTo(VerifyVerdict.Pass));
        Assert.That(byIndex[0].Stdout.Trim(), Is.EqualTo("one"));
        Assert.That(byIndex[1].Verdict, Is.EqualTo(VerifyVerdict.Timeout));
        Assert.That(byIndex[2].Verdict, Is.EqualTo(VerifyVerdict.RuntimeError));
        Assert.That(byIndex[2].ExitCode, Is.EqualTo(3));
        Assert.That(byIndex[3].Verdict, Is.EqualTo(VerifyVerdict.Pass));
        Assert.That(byIndex[3].Stdout.Trim(), Is.EqualTo("four"));
    }

    [Test]
    public void ResultsAreKeyedByTaskIndexNotCompletionOrderTest()
    {
        // Fast tasks finish before slow ones under parallelism; TaskIndex must still re-key correctly.
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = OutWit.Controller.AI.Verify.Runtimes.VerifyRuntimeCatalog.PYTHON_3_14_6,
            DefaultLimits = new VerifyLimitsData { FuelBudget = 20_000_000_000, WallTimeMs = 30_000 },
            Tasks =
            [
                PythonTask("print(sum(range(2_000_000)))", 0),  // slower
                PythonTask("print('fast')", 1)                   // faster
            ]
        };

        var result = VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch);

        Assert.That(result.Results.Single(r => r.TaskIndex == 1).Stdout.Trim(), Is.EqualTo("fast"));
        Assert.That(result.Results.Single(r => r.TaskIndex == 0).Stdout.Trim(), Is.EqualTo("1999999000000"));
    }

    [Test]
    public void ParallelBatchIsDeterministicTest()
    {
        VerifyTaskData Work(int i) => PythonTask(
            "import math; print(sum(math.sqrt(k) for k in range(50000)))", i,
            limits: new VerifyLimitsData { FuelBudget = 20_000_000_000, WallTimeMs = 30_000 });

        var batch = new VerifyTaskBatchData
        {
            RuntimeId = OutWit.Controller.AI.Verify.Runtimes.VerifyRuntimeCatalog.PYTHON_3_14_6,
            Tasks = Enumerable.Range(0, 16).Select(Work).ToList()
        };

        var result = VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch);

        Assert.That(result.Results.All(r => r.Verdict == VerifyVerdict.Pass), Is.True);
        var distinctOutputs = result.Results.Select(r => r.Stdout).Distinct().Count();
        Assert.That(distinctOutputs, Is.EqualTo(1), "all identical tasks must produce identical output");
    }
}
