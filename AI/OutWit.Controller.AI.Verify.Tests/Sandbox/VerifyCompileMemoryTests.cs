using System.Diagnostics;
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
        // Measured: 6 KB a function while the module is compiled, up to 5 KB a byte of the
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
    public void D7AModuleThatMayTakeMoreThanTheGateIsRefusedBeforeItIsCompiledTest()
    {
        // 64 functions of 26,000 calls through a table: within every bound, 8.3 MB, and on x64
        // 1.6 GiB and five minutes to compile. Estimated over what a node sets aside for one
        // compile, it is refused without being compiled, at any budget.
        var module = ShapeModulesKept.IndirectCalls(0, 26_000, 64);
        var report = VerifyModuleShape.Scan(module);
        var unbounded = VerifyModuleShape.Measure(module);
        var wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason) ?? throw new InvalidOperationException(reason);
        var limits = VerifyLimitCeilings.Effective(new VerifyLimitsData { FuelBudget = long.MaxValue }, null);

        // Without the refusal the sandbox would compile it: the scan's word is asked first.
        Assert.That(report.Problem, Is.EqualTo(VerifyModuleShape.TOO_MUCH_MEMORY));

        using var sandbox = new VerifyWasmSandbox();
        var clock = Stopwatch.StartNew();
        var result = sandbox.Execute(wasi, ModuleTask(module), limits);

        Assert.Multiple(() =>
        {
            Assert.That(module, Has.Length.LessThanOrEqualTo(VerifyWasmSandbox.MAX_TASK_MODULE_BYTES));
            Assert.That(unbounded.Problem, Is.Null, "no other bound holds it");
            Assert.That(unbounded.CompileMemoryBytes, Is.GreaterThan(VerifyCompileMemory.MAX_BYTES));
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Is.EqualTo(VerifyModuleShape.TOO_MUCH_MEMORY));
            Assert.That(sandbox.TaskModuleCacheCount, Is.Zero, "nothing reached the engine's compiler");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void D7NoModuleThatIsCompiledMayTakeMoreThanWhatIsSetAsideTest()
    {
        // The gate is the most a compile may take: whatever is not refused fits it alone.
        var shapes = new[]
        {
            ShapeModules.Calls(8, 65_000, 16),
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
                Assert.That(report.CompileMemoryBytes, Is.LessThanOrEqualTo(VerifyWasmSandbox.CompileGateBytes));
            }

            Assert.That(VerifyWasmSandbox.CompileGateBytes, Is.EqualTo(VerifyCompileMemory.MAX_BYTES));
            Assert.That(VerifyModuleShape.TOO_MUCH_MEMORY, Is.EqualTo($"compiling the module may take more than {VerifyCompileMemory.MAX_BYTES >> 20} MiB of memory"));
        });
    }

    [Test]
    public void D7TheDenseCallsAreEstimatedOverWhatTheyTookAndRefusedTest()
    {
        // 64 functions of 65,000 calls that pass eight values (8.3 MB, every bound held): measured
        // 1,893 MiB resident on an arm64 node and 1,382 MiB committed on x64, where the estimate
        // was 540 MiB. Now it is estimated over the arm64 peak with the margin, and over what a
        // node sets aside for one compile: it is refused, and never compiled. Sixteen of those
        // functions (1,020 MiB on arm64) are within the gate and over half of it: such a compile
        // runs alone.
        var report = VerifyModuleShape.Scan(ShapeModules.Calls(8, 65_000, 64));
        var sixteen = VerifyModuleShape.Scan(ShapeModules.Calls(8, 65_000, 16));

        Assert.Multiple(() =>
        {
            Assert.That(report.CompileMemoryBytes, Is.GreaterThanOrEqualTo((long)(1.15 * 1893 * MEBIBYTE)));
            Assert.That(report.Problem, Is.EqualTo(VerifyModuleShape.TOO_MUCH_MEMORY));
            Assert.That(sixteen.Problem, Is.Null);
            Assert.That(sixteen.CompileMemoryBytes, Is.GreaterThanOrEqualTo((long)(1.15 * 1020 * MEBIBYTE)));
            Assert.That(sixteen.CompileMemoryBytes, Is.LessThanOrEqualTo(VerifyWasmSandbox.CompileGateBytes));
            Assert.That(sixteen.CompileMemoryBytes, Is.GreaterThan(VerifyWasmSandbox.CompileGateBytes / 2));
        });
    }

    [Test]
    public void D7WhatACompileKeepsOfEveryFunctionIsCountedTest()
    {
        // The compiler keeps what it made of every function until the module is done: the
        // estimate grows with every function's instructions, not only with the largest one.
        const int CALLS = 65_000;
        var one = VerifyModuleShape.Scan(ShapeModules.Calls(8, CALLS));
        var sixteen = VerifyModuleShape.Scan(ShapeModules.Calls(8, CALLS, 16));
        var bytes = ShapeModules.Calls(8, CALLS, 16).Length - ShapeModules.Calls(8, CALLS).Length;

        // Each function of the shape: eight constants and the calls, each passing eight values each way.
        var perFunction = CALLS * VerifyCompileMemory.CallUnits(false, 8, 8) + 8L * VerifyCompileMemory.UNITS_PLAIN;

        Assert.Multiple(() =>
        {
            Assert.That(sixteen.KeptUnits - one.KeptUnits, Is.EqualTo(15 * perFunction));
            Assert.That(sixteen.LargestWork, Is.EqualTo(one.LargestWork), "what the compiler takes for the function at hand does not add up");
            Assert.That(sixteen.CompileMemoryBytes - one.CompileMemoryBytes,
                Is.EqualTo(15 * (perFunction * VerifyCompileMemory.PER_KEPT_UNIT + VerifyCompileMemory.PER_FUNCTION) + bytes * VerifyCompileMemory.PER_MODULE_BYTE));
        });
    }

    [Test]
    public void D7ACallCountsTheValuesOfTheTypeItCallsTest()
    {
        // A call of a function of the module, of an import and through a table: the units of
        // its kind and a unit a value of the type called, a parameter or a result.
        const int CALLS = 1000;
        var none = VerifyModuleShape.Scan(ShapeModules.Calls(0, CALLS));
        var direct = VerifyModuleShape.Scan(ShapeModules.Calls(8, CALLS));
        var imported = VerifyModuleShape.Scan(ShapeModulesKept.ImportedCalls(8, CALLS));
        var indirect = VerifyModuleShape.Scan(ShapeModulesKept.IndirectCalls(8, CALLS));
        var constants = 8L * VerifyCompileMemory.UNITS_PLAIN;
        var callee = VerifyCompileMemory.UNITS_PLAIN; // its one instruction, unreachable

        Assert.Multiple(() =>
        {
            Assert.That(none.KeptUnits, Is.EqualTo(CALLS * (long)VerifyCompileMemory.UNITS_CALL + callee));
            Assert.That(direct.KeptUnits, Is.EqualTo(CALLS * VerifyCompileMemory.CallUnits(false, 8, 8) + constants + callee));
            Assert.That(imported.KeptUnits, Is.EqualTo(CALLS * VerifyCompileMemory.CallUnits(false, 8, 8) + constants), "an import has the type it is imported with");
            Assert.That(indirect.KeptUnits, Is.EqualTo(CALLS * (VerifyCompileMemory.CallUnits(true, 8, 8) + VerifyCompileMemory.UNITS_PLAIN) + constants + callee));
            Assert.That(VerifyCompileMemory.CallUnits(false, 8, 8), Is.EqualTo(VerifyCompileMemory.UNITS_CALL + 16 * VerifyCompileMemory.UNITS_PER_VALUE));
            Assert.That(indirect.LargestWork - direct.LargestWork,
                Is.EqualTo((indirect.LargestBody - direct.LargestBody) * VerifyCompileMemory.PER_BODY_BYTE + CALLS * VerifyCompileMemory.WORK_INDIRECT_CALL),
                "a call through a table takes the compiler more while it works on the function");
        });
    }

    [Test]
    public void D7EveryKindOfInstructionCountsWhatItKeepsAndTakesTest()
    {
        const int COUNT = 1000;
        var memory = (long)VerifyCompileMemory.UNITS_MEMORY;
        var plain = (long)VerifyCompileMemory.UNITS_PLAIN;

        var loads = VerifyModuleShape.Scan(ShapeModulesKept.SummedLoads(COUNT));
        var divisions = VerifyModuleShape.Scan(ShapeModulesKept.Divisions(COUNT));
        var truncations = VerifyModuleShape.Scan(ShapeModulesKept.Truncations(COUNT));
        var grows = VerifyModuleShape.Scan(ShapeModulesKept.Grows(COUNT));
        var branches = VerifyModuleShape.Scan(ShapeModules.Branches(0, COUNT));
        var arithmetic = VerifyModuleShape.Scan(ShapeModulesKept.Arithmetic(COUNT));

        Assert.Multiple(() =>
        {
            // A load from a base read first, a load and an addition each, the store of the sum.
            Assert.That(loads.KeptUnits, Is.EqualTo(2 * memory + 2 * plain + COUNT * (memory + plain)));
            Assert.That(loads.LargestWork, Is.EqualTo(VerifyCompileMemory.Work(loads.LargestBody, (COUNT + 2) * VerifyCompileMemory.WORK_MEMORY)));
            // Two values read first, a division each.
            Assert.That(divisions.KeptUnits, Is.EqualTo(2 * memory + 2 * plain + COUNT * (long)VerifyCompileMemory.UNITS_DIVISION));
            // A value read first; a conversion to a float, one back that may trap, a constant and an addition each; the store.
            Assert.That(truncations.KeptUnits, Is.EqualTo(2 * memory + 2 * plain + COUNT * (VerifyCompileMemory.UNITS_TRUNCATION + 3 * plain)));
            Assert.That(grows.KeptUnits, Is.EqualTo(COUNT * (VerifyCompileMemory.UNITS_ENGINE_CALL + plain)));
            Assert.That(branches.KeptUnits, Is.EqualTo(COUNT * (long)VerifyCompileMemory.UNITS_BRANCH));
            Assert.That(arithmetic.KeptUnits, Is.EqualTo(2 * memory + 2 * plain + COUNT * 4 * plain));
            Assert.That(grows.LargestWork, Is.EqualTo(VerifyCompileMemory.Work(grows.LargestBody, 0)), "a call into the engine takes the compiler no more than its bytes");
        });
    }

    [Test]
    public void D7AnInstructionIsReadWithAllItsImmediatesTest()
    {
        // A select with its type, and an import: what follows them is the next instruction, not
        // their bytes read as instructions (the type of a select is i64, which read as an
        // instruction would be a multiplication).
        byte[] select = [0x00, 0x42, 0x00, 0x42, 0x00, 0x41, 0x00, 0x1C, 0x01, 0x7E, 0x1A, 0x0B];
        var report = VerifyModuleShape.Scan(ShapeModules.Build([([], [])], [(0, [0x00, 0x0B]), (0, select)], 1));
        var imported = VerifyModuleShape.Scan(ShapeModulesKept.ImportedCalls(2, 10));

        Assert.Multiple(() =>
        {
            Assert.That(report.Problem, Is.Null);
            Assert.That(report.KeptUnits, Is.EqualTo(4L * VerifyCompileMemory.UNITS_PLAIN), "three constants and the select");
            Assert.That(imported.Problem, Is.Null, "the import's names are passed over whole");
            Assert.That(imported.KeptUnits, Is.EqualTo(10 * VerifyCompileMemory.CallUnits(false, 2, 2) + 2L * VerifyCompileMemory.UNITS_PLAIN));
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

    [Test]
    public void N2TheSandboxSendsACompileThatMayTakeMuchThroughTheGateAndNoOtherTest()
    {
        // Two modules of one function of nothing but nops: priced alike by their instructions, and
        // apart by the memory their compile may take, the larger body past the line and the smaller
        // under it. With the gate held full, the one past the line waits for its place; the other
        // is compiled and run as if there were no gate.
        var wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason) ?? throw new InvalidOperationException(reason);
        var limits = VerifySandboxDefaults.Resolve(null, null);
        var small = ShapeModules.Body(4096);
        var large = ShapeModules.Body(16_384);
        var smallShape = VerifyModuleShape.Scan(small);
        var largeShape = VerifyModuleShape.Scan(large);

        using var sandbox = new VerifyWasmSandbox();
        var gate = sandbox.CompileGate;

        Task<VerifyResultData> largeRun;
        Task<VerifyResultData> smallRun;
        bool smallDoneWhileFull;
        bool largeDoneWhileFull;
        var full = gate.Enter(gate.Capacity);
        try
        {
            largeRun = Task.Run(() => sandbox.Execute(wasi, ModuleTask(large), limits));
            smallRun = Task.Run(() => sandbox.Execute(wasi, ModuleTask(small), limits));
            smallDoneWhileFull = smallRun.Wait(TimeSpan.FromSeconds(60));

            // The larger started first: without the gate it would have been compiled and run by now.
            largeDoneWhileFull = largeRun.Wait(TimeSpan.FromSeconds(2));
        }
        finally
        {
            full.Dispose();
        }

        var largeDone = largeRun.Wait(TimeSpan.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(smallShape.CompileMemoryBytes, Is.LessThan(VerifyWasmSandbox.HEAVY_COMPILE_BYTES));
            Assert.That(largeShape.CompileMemoryBytes, Is.GreaterThanOrEqualTo(VerifyWasmSandbox.HEAVY_COMPILE_BYTES));
            Assert.That(largeShape.ShapeFuel, Is.LessThan(VerifyWasmSandbox.HEAVY_COMPILE_FUEL), "past the line by the memory alone");
            Assert.That(smallDoneWhileFull, Is.True, "a compile under the line does not wait for the gate");
            Assert.That(largeDoneWhileFull, Is.False, "a compile past the line waits while the gate has no room");
            Assert.That(largeDone, Is.True, "and goes on once it has");
            Assert.That(gate.Taken, Is.Zero);
        });

        Assert.Multiple(() =>
        {
            Assert.That(smallRun.Result.Verdict, Is.EqualTo(VerifyVerdict.Pass), smallRun.Result.Stderr);
            Assert.That(largeRun.Result.Verdict, Is.EqualTo(VerifyVerdict.Pass), largeRun.Result.Stderr);
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
            Assert.That(VerifyWasmSandbox.CompileGateBytes, Is.EqualTo(VerifyCompileMemory.MAX_BYTES), "the gate holds the largest compile there may be");
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

    [Test]
    public void D7HowManyTasksOfACompiledRuntimeANodeRunsAtOnceTest()
    {
        // A node of 16 cores at the default cap of the degree, with 5, 16 and 64 GiB free: three
        // quarters of that, less the gate and the kept modules, in tasks of their memory cap and a
        // compile of the lighter kind (wasi), or of the compiler's step and that compile (C/C++).
        const long GIBIBYTE = 1L << 30;
        var wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason) ?? throw new InvalidOperationException(reason);
        var aside = VerifyBatchExecutor.MemoryAside(wasi);
        var perWasiTask = VerifySandboxDefaults.DEFAULT_MEMORY_BYTES + VerifyWasmSandbox.HEAVY_COMPILE_BYTES;
        var perCTask = VerifyClangBuild.STEP_MEMORY_BYTES + VerifyWasmSandbox.HEAVY_COMPILE_BYTES;

        int Degree(long free, long perTask) =>
            VerifySandboxDegree.Compute(16, VerifySandboxDegree.UsableRamBytes(free, VerifySandboxDegree.DEFAULT_RAM_HEADROOM, aside), perTask);

        Assert.Multiple(() =>
        {
            Assert.That(aside, Is.EqualTo(1792 * MEBIBYTE), "1.5 GiB of the gate and 256 MiB of kept modules");
            Assert.That(Degree(5 * GIBIBYTE, perWasiTask), Is.EqualTo(6));
            Assert.That(Degree(5 * GIBIBYTE, perCTask), Is.EqualTo(1));
            Assert.That(Degree(16 * GIBIBYTE, perWasiTask), Is.EqualTo(16));
            Assert.That(Degree(16 * GIBIBYTE, perCTask), Is.EqualTo(9));
            Assert.That(Degree(64 * GIBIBYTE, perWasiTask), Is.EqualTo(16));
            Assert.That(Degree(64 * GIBIBYTE, perCTask), Is.EqualTo(16));
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

    #region Tools

    /// <summary>A <c>wasi</c> task that brings a module.</summary>
    private static VerifyTaskData ModuleTask(byte[] module)
    {
        return new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            EntryPoint = "main.wasm",
            Sources = [new VerifySourceFileData { Name = "main.wasm", Content = Convert.ToBase64String(module) }]
        };
    }

    #endregion
}
