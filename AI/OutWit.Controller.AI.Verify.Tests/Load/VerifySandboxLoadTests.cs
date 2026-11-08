using System.Diagnostics;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tests.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Load;

/// <summary>
/// Load on one node's sandbox (Category=Load; not part of the CI sweep — run with
/// <c>dotnet test --filter Category=Load</c>): throughput of a thousand tasks at the machine's
/// degree, memory that stays bounded over thousands of runs, a task-module cache that stays
/// bounded over hundreds of distinct modules, and nothing left on disk. The numbers go to the
/// test output; the assertions are the floors a release must not sink under.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Load")]
public sealed class VerifySandboxLoadTests : VerifySandboxTestBase
{
    #region Constants

    private const int THROUGHPUT_TASKS = 1000;
    private const int SOAK_TASKS = 3000;
    private const int SOAK_BATCH = 250;
    private const int DISTINCT_MODULES = 300;
    private const double MIN_TASKS_PER_SECOND = 5;
    private const long MAX_MANAGED_GROWTH_BYTES = 64L * 1024 * 1024;
    private const long MAX_WORKING_SET_GROWTH_BYTES = 768L * 1024 * 1024;

    #endregion

    #region Tests

    [Test]
    public void AThousandHelloTasksAtTheMachineDegreeTest()
    {
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.PYTHON_3_14_6,
            Tasks = Enumerable.Range(0, THROUGHPUT_TASKS).Select(i => PythonTask($"print({i} * 2)", i,
                suite: new VerifySuiteData { Cases = [new VerifySuiteCaseData { ExpectedStdout = $"{i * 2}\n" }] })).ToList()
        };

        var stopwatch = Stopwatch.StartNew();
        var result = VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch);
        stopwatch.Stop();

        var rate = THROUGHPUT_TASKS / stopwatch.Elapsed.TotalSeconds;
        TestContext.Out.WriteLine($"{THROUGHPUT_TASKS} python suite tasks at degree {result.Diagnostics!.Degree}: {stopwatch.Elapsed.TotalSeconds:F1} s, {rate:F1} tasks/s");

        Assert.Multiple(() =>
        {
            Assert.That(result.Results.All(r => r.Verdict == VerifyVerdict.Pass), Is.True);
            Assert.That(rate, Is.GreaterThan(MIN_TASKS_PER_SECOND));
            Assert.That(Directory.GetFiles(Sandbox.IoDirectory), Is.Empty);
        });
    }

    [Test]
    public void MemoryStaysBoundedOverThousandsOfRunsTest()
    {
        // Warm up: the module compiles and the first stores allocate their arenas.
        RunBatch(0, SOAK_BATCH);
        var managedBefore = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetBefore = Process.GetCurrentProcess().WorkingSet64;

        for (var offset = SOAK_BATCH; offset < SOAK_TASKS; offset += SOAK_BATCH)
            RunBatch(offset, SOAK_BATCH);

        var managedAfter = GC.GetTotalMemory(forceFullCollection: true);
        var workingSetAfter = Process.GetCurrentProcess().Refresh_WorkingSet();
        TestContext.Out.WriteLine($"managed {managedBefore / 1024 / 1024} → {managedAfter / 1024 / 1024} MB; working set {workingSetBefore / 1024 / 1024} → {workingSetAfter / 1024 / 1024} MB over {SOAK_TASKS} tasks");

        Assert.Multiple(() =>
        {
            Assert.That(managedAfter - managedBefore, Is.LessThan(MAX_MANAGED_GROWTH_BYTES));
            Assert.That(workingSetAfter - workingSetBefore, Is.LessThan(MAX_WORKING_SET_GROWTH_BYTES));
            Assert.That(Directory.GetFiles(Sandbox.IoDirectory), Is.Empty);
        });
    }

    [Test]
    public void HundredsOfDistinctModulesKeepTheCacheBoundedTest()
    {
        var wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out _)!;
        var batch = new VerifyTaskBatchData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            Tasks = Enumerable.Range(0, DISTINCT_MODULES).Select(i => new VerifyTaskData
            {
                TaskIndex = i,
                RuntimeId = VerifyRuntimeCatalog.WASI,
                Sources = [new VerifySourceFileData { Name = "m.wasm", Content = Convert.ToBase64String(WasiModules.Hello($"module {i}\n")) }],
                EntryPoint = "m.wasm",
                Suite = new VerifySuiteData { Cases = [new VerifySuiteCaseData { ExpectedStdout = $"module {i}\n" }] }
            }).ToList()
        };

        var stopwatch = Stopwatch.StartNew();
        var result = VerifyBatchExecutor.Execute(Sandbox, null, batch);
        stopwatch.Stop();
        TestContext.Out.WriteLine($"{DISTINCT_MODULES} distinct wasi modules compiled and run in {stopwatch.Elapsed.TotalSeconds:F1} s; cache holds {Sandbox.TaskModuleCacheCount}");

        Assert.Multiple(() =>
        {
            Assert.That(result.Results.All(r => r.Verdict == VerifyVerdict.Pass), Is.True, string.Join("; ", result.Results.Where(r => r.Verdict != VerifyVerdict.Pass).Select(r => $"{r.TaskIndex}: {r.Verdict} {r.Stderr}")));
            Assert.That(Sandbox.TaskModuleCacheCount, Is.LessThanOrEqualTo(VerifyWasmSandbox.TaskModuleCacheCapacity));
            Assert.That(wasi.Descriptor.TaskSupplied, Is.True);
        });
    }

    #endregion

    #region Helpers

    private void RunBatch(int offset, int count)
    {
        var tasks = new List<VerifyTaskData>(count);
        for (var i = 0; i < count; i++)
        {
            var index = offset + i;
            tasks.Add(i % 2 == 0
                ? PythonTask($"print({index})", index, suite: new VerifySuiteData { Cases = [new VerifySuiteCaseData { ExpectedStdout = $"{index}\n" }] })
                : JsTask($"console.log({index})", index));
        }

        foreach (var group in tasks.GroupBy(t => t.RuntimeId))
        {
            var batch = new VerifyTaskBatchData { RuntimeId = group.Key, Tasks = group.ToList() };
            var result = VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch);
            Assert.That(result.Results.All(r => r.Verdict == VerifyVerdict.Pass), Is.True);
        }
    }

    #endregion
}

internal static class ProcessExtensions
{
    public static long Refresh_WorkingSet(this Process process)
    {
        process.Refresh();
        return process.WorkingSet64;
    }
}
