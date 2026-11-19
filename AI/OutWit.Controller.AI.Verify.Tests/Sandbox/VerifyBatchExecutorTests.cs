using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
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

    #region M-3 Tests

    [Test]
    public void M3TheDegreeCountsTheMemoryThatIsFreeTest()
    {
        const long gibibyte = 1L << 30;

        // 64 GiB of which 60 are taken: three quarters of the 4 that are free hold twelve programs
        // of 256 MiB. Three quarters of the total would have said sixteen.
        var free = VerifySandboxDegree.FreeRamBytes(64 * gibibyte, 60 * gibibyte);

        Assert.Multiple(() =>
        {
            Assert.That(free, Is.EqualTo(4 * gibibyte));
            Assert.That(VerifySandboxDegree.Compute(32, (long)(free * VerifySandboxDegree.DEFAULT_RAM_HEADROOM), VerifySandboxDefaults.DEFAULT_MEMORY_BYTES), Is.EqualTo(12));
            Assert.That(VerifySandboxDegree.FreeRamBytes(64 * gibibyte, 0), Is.EqualTo(64 * gibibyte), "a load that is not known leaves the total");
            Assert.That(VerifySandboxDegree.FreeRamBytes(64 * gibibyte, 70 * gibibyte), Is.EqualTo(1), "nothing free: one program at a time");
            Assert.That(VerifySandboxDegree.Compute(32, 1, VerifySandboxDefaults.DEFAULT_MEMORY_BYTES), Is.EqualTo(1));
            Assert.That(VerifySandboxDegree.ForCurrentMachine(VerifySandboxDefaults.DEFAULT_MEMORY_BYTES), Is.InRange(1, VerifySandboxDegree.DEFAULT_MAX_DEGREE));
        });
    }

    [Test]
    public void M3ATaskOfTheCompilerRuntimeCountsForItsCompilersMemoryTest()
    {
        // C tasks with a memory cap of 16 MiB ran sixteen at once; each compile step may take 1 GiB.
        var small = new VerifyLimitsData { MemoryBytes = 16 << 20 };
        var c = new VerifyTaskBatchData { RuntimeId = VerifyRuntimeCatalog.CLANG_22_1_0, DefaultLimits = small, Tasks = [new VerifyTaskData()] };
        var python = new VerifyTaskBatchData { RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6, DefaultLimits = small, Tasks = [new VerifyTaskData()] };

        Assert.Multiple(() =>
        {
            Assert.That(VerifyBatchExecutor.MemoryPerTask(c, Clang), Is.EqualTo(VerifyClangBuild.STEP_MEMORY_BYTES + VerifyWasmSandbox.HEAVY_COMPILE_BYTES),
                "the compiler's step, and the engine compiling the program it built");
            Assert.That(VerifyBatchExecutor.MemoryPerTask(python, Python), Is.EqualTo(16 << 20));
            Assert.That(VerifyBatchExecutor.MemoryAside(Clang), Is.EqualTo(VerifyWasmSandbox.CompileGateBytes + VerifyTaskModuleCache.DEFAULT_BYTE_CAPACITY));
            Assert.That(VerifyBatchExecutor.MemoryAside(Python), Is.Zero, "the interpreter is the controller's own module, compiled once");
        });
    }

    [Test]
    public void M2ANodeRefusesATaskOutsideTheBoundsOfATasksetTest()
    {
        // A task runs once a case and every source is a file: what Split refuses a taskset for,
        // a node refuses a package for, whoever made the package. One case, source or argument
        // past the bound is enough; a task at the bounds is taken.
        const int most = OutWit.Controller.AI.Verify.Tasksets.VerifyTasksetLimits.MAX_SUITE_CASES;

        VerifyTaskBatchData Batch(Action<VerifyTaskData> change)
        {
            var task = JsTask("console.log(1)");
            change(task);
            return new VerifyTaskBatchData { RuntimeId = VerifyRuntimeCatalog.QUICKJS_0_15_1, Tasks = [JsTask("console.log(2)", 1), task] };
        }

        var cases = Batch(task => task.Suite = new VerifySuiteData { Cases = Enumerable.Range(0, most + 1).Select(_ => new VerifySuiteCaseData()).ToList() });
        var sources = Batch(task => task.Sources = Enumerable.Range(0, most + 1).Select(index => new VerifySourceFileData { Name = $"f{index}", Content = "" }).ToList());
        var arguments = Batch(task => task.Args = Enumerable.Repeat("a", most + 1).ToList());
        var caseArguments = Batch(task => task.Suite = new VerifySuiteData { Cases = [new VerifySuiteCaseData { Args = Enumerable.Repeat("a", most + 1).ToList() }] });
        var atTheBounds = Batch(task =>
        {
            task.Args = Enumerable.Repeat("a", most).ToList();
            task.Suite = new VerifySuiteData { Cases = Enumerable.Range(0, most).Select(_ => new VerifySuiteCaseData()).ToList() };
        });

        Assert.Multiple(() =>
        {
            foreach (var batch in new[] { cases, sources, arguments, caseArguments })
            {
                Assert.That(() => VerifyBatchExecutor.EnsureRunnable(RuntimesRoot, batch),
                    Throws.TypeOf<VerifyBatchRefusedException>().With.Message.EqualTo("a task of the batch has more sources, arguments or cases than a node takes"));
            }

            Assert.That(() => VerifyBatchExecutor.EnsureRunnable(RuntimesRoot, atTheBounds), Throws.Nothing);
        });
    }

    #endregion
}
