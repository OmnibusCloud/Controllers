using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Wasm;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The memory the engine takes for a task's module, counted where the node decides how many
/// tasks run at once (findings N-2 and M-3 of the audit of 2026-11-18): what a compile may take
/// is estimated from the module's shape, a compile that may take much waits for its turn, the
/// degree sets one such compile and the kept modules aside, and the compiled modules the sandbox
/// keeps are bounded by their bytes, not by their number.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyCompileMemoryTests
{
    #region Constants

    private const long MEBIBYTE = 1L << 20;

    #endregion

    #region Fields

    private WasmHost m_host = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void SetUp()
    {
        m_host = new WasmHost(parallelCompilation: false);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_host?.Dispose();
    }

    #endregion

    #region Estimate Tests

    [Test]
    public void N2WhatACompileMayTakeIsReadOffTheShapeTest()
    {
        // Measured: 6 KB a function while the module is compiled, up to 4 KB a byte of the
        // function the compiler works on, 4 bytes a unit of a block that passes values.
        var hello = VerifyModuleShape.Scan(Convert.FromBase64String(VerifySelfCheck.HELLO_WASM_BASE64));
        var calls = VerifyModuleShape.Scan(ShapeModules.Calls(8, 20_000));
        var functions = VerifyModuleShape.Scan(ShapeModules.Locals(20_000, 0));
        var blocks = VerifyModuleShape.Scan(ShapeModules.Blocks(8, 2048));

        Assert.Multiple(() =>
        {
            Assert.That(hello.CompileMemoryBytes, Is.LessThan(VerifyWasmSandbox.HEAVY_COMPILE_BYTES), "a hello is compiled beside its neighbours");
            Assert.That(calls.CompileMemoryBytes, Is.GreaterThan(160 * MEBIBYTE), "40 KB of calls that pass eight values took 180 MiB");
            Assert.That(functions.CompileMemoryBytes, Is.GreaterThan(120 * MEBIBYTE), "20,000 functions took 115 MiB");
            Assert.That(blocks.CompileMemoryBytes, Is.GreaterThan(130 * MEBIBYTE), "eight results through 2,048 blocks took up to 132 MiB at sixteen");
            Assert.That(calls.CompileMemoryBytes, Is.GreaterThanOrEqualTo(VerifyWasmSandbox.HEAVY_COMPILE_BYTES), "such a compile goes through the gate");
        });
    }

    [Test]
    public void N2NoModuleWithinTheBoundsMayTakeMoreThanWhatIsSetAsideTest()
    {
        var atTheBounds = VerifyModuleShape.CompileMemoryAtTheBounds(VerifyWasmSandbox.MAX_TASK_MODULE_BYTES);
        var gate = VerifyWasmSandbox.CompileGateBytes;
        var shapes = new[]
        {
            ShapeModules.Calls(8, 60_000),
            ShapeModules.Locals(VerifyModuleShape.MAX_FUNCTIONS - 1, 0),
            ShapeModules.Blocks(VerifyModuleShape.MAX_BLOCK_VALUES, VerifyModuleShape.MAX_NESTING),
            ShapeModules.Chain(18_000),
            ShapeModules.Body(VerifyModuleShape.MAX_BODY_BYTES)
        };

        Assert.Multiple(() =>
        {
            foreach (var module in shapes)
            {
                var report = VerifyModuleShape.Scan(module);
                Assert.That(report.Problem, Is.Null);
                Assert.That(report.CompileMemoryBytes, Is.LessThanOrEqualTo(atTheBounds));
            }

            Assert.That(atTheBounds, Is.LessThan(1024 * MEBIBYTE), "under a gibibyte for a module a task brings");
            Assert.That(gate, Is.GreaterThanOrEqualTo(atTheBounds), "the gate holds a compile at the bounds, of a module a task brings and of a program a build wrote");
            Assert.That(gate, Is.EqualTo(VerifyModuleShape.CompileMemoryAtTheBounds(VerifyClangBuild.PRODUCT_LIMIT_BYTES)));
        });
    }

    [Test]
    public void N2CompilesThatTakeMuchShareTheGateAndOneThatFillsItRunsAloneTest()
    {
        var gate = new VerifyCompileGate(100);

        var first = gate.Enter(60);
        var beside = gate.Enter(30);
        var takenByTwo = gate.Taken;

        // A third that does not fit waits until one of the two leaves.
        using var entered = new ManualResetEventSlim(false);
        IDisposable? third = null;
        var waiter = Task.Run(() =>
        {
            third = gate.Enter(60);
            entered.Set();
        });
        var enteredWhileFull = entered.Wait(TimeSpan.FromMilliseconds(300));
        first.Dispose();
        var enteredOnceThereWasRoom = entered.Wait(TimeSpan.FromSeconds(30));
        waiter.Wait(TimeSpan.FromSeconds(30));
        var takenByTheOtherTwo = gate.Taken;

        // One that may take more than the gate holds takes all of it: it waits for an empty gate.
        using var alone = new ManualResetEventSlim(false);
        IDisposable? whole = null;
        var large = Task.Run(() =>
        {
            whole = gate.Enter(1_000_000);
            alone.Set();
        });
        var enteredBesideOthers = alone.Wait(TimeSpan.FromMilliseconds(300));
        beside.Dispose();
        third!.Dispose();
        var enteredWhenEmpty = alone.Wait(TimeSpan.FromSeconds(30));
        large.Wait(TimeSpan.FromSeconds(30));
        var takenByTheLargest = gate.Taken;
        whole!.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(takenByTwo, Is.EqualTo(90), "two that fit run side by side");
            Assert.That(enteredWhileFull, Is.False, "a third that does not fit waits");
            Assert.That(enteredOnceThereWasRoom, Is.True);
            Assert.That(takenByTheOtherTwo, Is.EqualTo(90));
            Assert.That(enteredBesideOthers, Is.False, "one that fills the gate does not run beside another");
            Assert.That(enteredWhenEmpty, Is.True);
            Assert.That(takenByTheLargest, Is.EqualTo(100), "it takes the gate, not more than the gate holds");
            Assert.That(gate.Taken, Is.Zero);
        });
    }

    #endregion

    #region Degree Tests

    [Test]
    public void M3ABatchWhoseModulesAreCompiledOnTheNodeSetsACompileAndTheKeptModulesAsideTest()
    {
        var wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason) ?? throw new InvalidOperationException(reason);
        var batch = new VerifyTaskBatchData { RuntimeId = VerifyRuntimeCatalog.WASI, Tasks = [new VerifyTaskData()] };

        var aside = VerifyBatchExecutor.MemoryAside(wasi);
        var perTask = VerifyBatchExecutor.MemoryPerTask(batch, wasi);

        Assert.Multiple(() =>
        {
            Assert.That(aside, Is.EqualTo(VerifyWasmSandbox.CompileGateBytes + VerifyTaskModuleCache.DEFAULT_BYTE_CAPACITY), "the gate of the larger compiles and the kept modules");
            Assert.That(perTask, Is.EqualTo(VerifySandboxDefaults.DEFAULT_MEMORY_BYTES + VerifyWasmSandbox.HEAVY_COMPILE_BYTES), "a task's cap and a compile of the lighter kind");
        });
    }

    [Test]
    public void M3TheDegreeCountsTheTasksInWhatIsLeftOnceTheBatchsOwnShareIsTakenTest()
    {
        const long GIBIBYTE = 1L << 30;
        var aside = 1200 * MEBIBYTE;
        var perTask = 320 * MEBIBYTE;

        // 16 GiB free: three quarters are 12 GiB, less 1.2 GiB, in tasks of 320 MiB: 34.
        var roomy = VerifySandboxDegree.UsableRamBytes(16 * GIBIBYTE, VerifySandboxDegree.DEFAULT_RAM_HEADROOM, aside);

        // 2 GiB free: 1.5 GiB less 1.2 GiB is one task.
        var tight = VerifySandboxDegree.UsableRamBytes(2 * GIBIBYTE, VerifySandboxDegree.DEFAULT_RAM_HEADROOM, aside);

        // 1 GiB free: nothing is left, and one task runs at a time all the same.
        var none = VerifySandboxDegree.UsableRamBytes(GIBIBYTE, VerifySandboxDegree.DEFAULT_RAM_HEADROOM, aside);

        Assert.Multiple(() =>
        {
            Assert.That(VerifySandboxDegree.Compute(64, roomy, perTask, 64), Is.EqualTo(34));
            Assert.That(VerifySandboxDegree.Compute(64, tight, perTask, 64), Is.EqualTo(1));
            Assert.That(none, Is.EqualTo(1));
            Assert.That(VerifySandboxDegree.Compute(64, none, perTask, 64), Is.EqualTo(1));
            Assert.That(VerifySandboxDegree.UsableRamBytes(0, VerifySandboxDegree.DEFAULT_RAM_HEADROOM, aside), Is.Zero, "unknown stays unknown");
            Assert.That(VerifySandboxDegree.UsableRamBytes(16 * GIBIBYTE, VerifySandboxDegree.DEFAULT_RAM_HEADROOM, 0), Is.EqualTo(12 * GIBIBYTE), "nothing aside for a runtime the controller ships");
        });
    }

    #endregion

    #region Cache Tests

    [Test]
    public void M3TheEngineSaysWhatACompiledModuleHoldsTest()
    {
        // The bound of the cache rests on this number: on the pinned binding and library it is
        // the engine's own, not the estimate.
        var bytes = ShapeModules.Calls(4, 2000);
        using var module = WasmModule.FromBytes(m_host.Engine, "m", bytes);
        using var hello = WasmModule.FromBytes(m_host.Engine, "h", Convert.FromBase64String(VerifySelfCheck.HELLO_WASM_BASE64));

        var large = WasmHostModuleImage.Measure(module);
        var small = WasmHostModuleImage.Measure(hello);

        TestContext.Out.WriteLine($"{bytes.Length} bytes of calls compile to {large} bytes; the hello to {small}");
        Assert.Multiple(() =>
        {
            Assert.That(large, Is.Not.Null, "the engine was asked and answered");
            Assert.That(small, Is.Not.Null);
            Assert.That(large, Is.GreaterThan(4L * bytes.Length), "the code of 2,000 calls is several times their bytes");
            Assert.That(large, Is.GreaterThan(small ?? long.MaxValue));
            Assert.That(WasmHostModuleImage.Bytes(module, bytes.Length), Is.EqualTo(large));
            Assert.That(WasmHostModuleImage.Estimate(1000), Is.EqualTo(1000 * WasmHostModuleImage.ESTIMATE_PER_BYTE));
        });
    }

    [Test]
    public void M3TheKeptModulesAreBoundedByTheirBytesTest()
    {
        // Every module here is taken to hold 400 KiB; the cache may hold a mebibyte of idle ones.
        using var cache = new VerifyTaskModuleCache(m_host.Engine, m_host.CompiledCodeKey, capacity: 64, byteCapacity: MEBIBYTE, sizeOf: (_, _) => 400 * 1024);

        for (var index = 0; index < 10; index++)
        {
            using var lease = cache.Lease("m", ShapeModules.Calls(1, 10 + index));
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Count, Is.EqualTo(2), "two of 400 KiB fit a mebibyte; ten were compiled");
            Assert.That(cache.Bytes, Is.EqualTo(800 * 1024));
        });
    }

    [Test]
    public void M3AModuleLargerThanTheBoundIsNotKeptOnceNobodyHoldsItTest()
    {
        using var cache = new VerifyTaskModuleCache(m_host.Engine, m_host.CompiledCodeKey, capacity: 64, byteCapacity: MEBIBYTE, sizeOf: (_, bytes) => bytes > 200 ? 8 * MEBIBYTE : 1024);

        var small = cache.Lease("s", ShapeModules.Calls(1, 4));
        small.Dispose();
        var held = cache.Lease("l", ShapeModules.Calls(1, 400));
        var whileHeld = (cache.Count, cache.Bytes);
        held.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(whileHeld.Count, Is.EqualTo(1), "the idle one went to make room, and that was not enough");
            Assert.That(whileHeld.Bytes, Is.EqualTo(8 * MEBIBYTE), "a module in flight is never evicted");
            Assert.That(cache.Count, Is.Zero, "and it goes when its lease returns");
            Assert.That(cache.Bytes, Is.Zero);
        });
    }

    [Test]
    public void M3TheSandboxKeepsNoMoreCompiledCodeThanItsBoundTest()
    {
        // The audit's residue: one module of 9 KB left 1,964 MiB in the process for as long as the
        // sandbox lived, and 64 such could stay. Modules within the bounds, each a few megabytes
        // compiled: what stays is under the bound whatever the number of them.
        var wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason) ?? throw new InvalidOperationException(reason);
        using var sandbox = new VerifyWasmSandbox();
        var limits = VerifySandboxDefaults.Resolve(null, null);

        long largest = 0;
        for (var index = 0; index < 6; index++)
        {
            var module = ShapeModules.Calls(8, 3000 + index);
            var result = sandbox.Execute(wasi, new VerifyTaskData
            {
                RuntimeId = VerifyRuntimeCatalog.WASI,
                EntryPoint = "main.wasm",
                Sources = [new VerifySourceFileData { Name = "main.wasm", Content = Convert.ToBase64String(module) }]
            }, limits);

            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            largest = Math.Max(largest, sandbox.TaskModuleCacheHeldBytes);
        }

        TestContext.Out.WriteLine($"six modules of 6 KB hold {sandbox.TaskModuleCacheHeldBytes} bytes of compiled code in {sandbox.TaskModuleCacheCount} entries");
        Assert.Multiple(() =>
        {
            Assert.That(sandbox.TaskModuleCacheHeldBytes, Is.GreaterThan(6 * 6000L), "the bytes counted are the compiled code's, not the module's");
            Assert.That(largest, Is.LessThanOrEqualTo(VerifyWasmSandbox.TaskModuleCacheBytes));
        });
    }

    #endregion
}
