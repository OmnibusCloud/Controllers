using System.Diagnostics;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The fuel rules as a task meets them (findings N-7, N-8 and N-11 of the audit of 2026-11-18,
/// with the modules it used): nothing a task's module does runs outside its budget, no call into
/// the host is free, and a task cannot make its own failure read as the node's clock.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyFuelRulesTests : VerifySandboxTestBase
{
    #region Constants

    private const string WASI = "wasi_snapshot_preview1";

    #endregion

    #region Fields

    private VerifyResolvedRuntime m_wasi = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void SetUp()
    {
        m_wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason) ?? throw new InvalidOperationException(reason);
    }

    #endregion

    #region N-7 Tests

    [Test]
    public void N7AStartFunctionIsRefusedInATasksModuleTest()
    {
        // The audit's module: a start function that burns two hundred million units before _start
        // ran under a budget of the compile price and a thousand units, passed, and reported
        // 278,556 units of fuel. With a budget of 4e9 a run did 7.8e9 of work.
        var module = TwoBurners(40_000_000, 0);
        var price = VerifyModuleShape.Measure(module).CompileFuel;
        var limits = VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = price + 1000 }, null);

        var clock = Stopwatch.StartNew();
        var result = Sandbox.Execute(m_wasi, Wasi(module), limits);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(result.Stderr, Is.EqualTo(VerifyModuleShape.START_FUNCTION));
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "nothing of it ran");
            Assert.That(VerifyModuleShape.Scan(TwoBurners(0, 0)).Problem, Is.EqualTo(VerifyModuleShape.START_FUNCTION), "whatever the start function does");
        });
    }

    [Test]
    public void N7TheModulesTheControllerShipsHaveNoStartFunctionTest()
    {
        // The host gives the making of a module what copying its segments can cost, and nothing
        // of that is in a budget or in the fuel reported: sound for as long as no module the
        // controller runs has code of its own there. A task's module is refused for it (above);
        // the modules of the runtime set are held to it here.
        var modules = Directory.EnumerateFiles(RuntimesRoot, "*.wasm", SearchOption.AllDirectories).ToList();
        Assert.That(modules, Is.Not.Empty);

        var withStart = modules.Where(path => VerifyModuleShape.Measure(File.ReadAllBytes(path)).HasStartFunction).Select(Path.GetFileName).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(withStart, Is.Empty);
            Assert.That(VerifyModuleShape.Measure(Convert.FromBase64String(VerifySelfCheck.HELLO_WASM_BASE64)).HasStartFunction, Is.False);
            Assert.That(VerifyModuleShape.Measure(TwoBurners(1, 1)).HasStartFunction, Is.True, "the scan sees one where there is one");
        });
    }

    [Test]
    public void N7AProgramBuiltFromSourcesHasNoStartFunctionTest()
    {
        // What the linker writes starts at _start; a program with constructors runs them from there.
        var result = Run(Clang, new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.CLANG_22_1_0,
            EntryPoint = "main.cpp",
            Sources =
            [
                new VerifySourceFileData
                {
                    Name = "main.cpp",
                    Content = "#include <cstdio>\nstruct Early { Early() { std::puts(\"constructed\"); } };\nstatic Early early;\nint main() { std::puts(\"main\"); return 0; }\n"
                }
            ]
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.Stdout, Is.EqualTo("constructed\nmain\n"));
        });
    }

    #endregion

    #region N-8 Tests

    [Test]
    public void N8AWaitWithoutEndIsOutOfFuelNotTheNodesClockTest()
    {
        // The audit's loop: poll_oneoff with 4,096 subscriptions, without end. It held a core for
        // the whole backstop (50 s at the default budget) on a few thousand units and ended as
        // NoResult, the node's fault, to be run once more on another node.
        var module = Loop("poll_oneoff", "(param i32 i32 i32 i32) (result i32)", "(i32.const 0) (i32.const 200000) (i32.const 4096) (i32.const 400000)", 8, -1);
        var limits = VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = 1_000_000_000 }, null);

        var result = Sandbox.Execute(m_wasi, Wasi(module), limits);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout), "out of fuel, the same on every node");
            Assert.That(result.Fault, Is.Empty);
            Assert.That(result.FuelConsumed, Is.EqualTo(limits.FuelBudget));
        });
    }

    [Test]
    public void N8TheArgumentsOfATaskCostTheirBytesEveryTimeTheProgramCopiesThemTest()
    {
        // The audit's loop: args_get of one large argument 2,000 times for 22,001 units.
        const int CALLS = 500;
        var big = new string('a', 100_000);
        var module = Loop("args_get", "(param i32 i32) (result i32)", "(i32.const 0) (i32.const 65536)", 8, CALLS);
        var task = Wasi(module);
        task.Args = [big];

        var result = Sandbox.Execute(m_wasi, task, VerifySandboxDefaults.Resolve(null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.FuelConsumed, Is.GreaterThan(CALLS * (long)big.Length), "a unit a byte, every time");
        });
    }

    [Test]
    public void N8AHelloPaysForItsCallsIntoTheHostTest()
    {
        // One write and nothing else: the price of the module, the price of one call, the six
        // bytes it wrote, and a handful of instructions.
        var module = Convert.FromBase64String(VerifySelfCheck.HELLO_WASM_BASE64);
        var price = VerifyModuleShape.Scan(module).CompileFuel;

        var result = Sandbox.Execute(m_wasi, Wasi(module), VerifySandboxDefaults.Resolve(null, null));

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
            Assert.That(result.FuelConsumed - price, Is.InRange(OutWit.Controller.AI.Wasm.WasmHostMeter.CALL_FUEL + 6, OutWit.Controller.AI.Wasm.WasmHostMeter.CALL_FUEL + 6 + 20));
        });
    }

    #endregion

    #region N-11 Tests

    [Test]
    public void N11AModuleThatWritesTheWordsOfTheClockGetsItsOwnVerdictTest()
    {
        // The audit's two modules: an import nobody defines, its module named with the words of
        // the deadline on a line of their own, and a call the engine refuses, made from a function
        // named so in the name section. Both ended as NoResult wall_clock: no verdict, the node's
        // fault, one more execution elsewhere.
        var newline = (char)10;
        var words = OutWit.Controller.AI.Wasm.WasmHostTraps.INTERRUPT_CAUSE;
        var import = HostileModules.Bytes($$"""
            (module
              (import "x\0a{{words}}\0ay" "f" (func $f))
              (memory (export "memory") 1)
              (func (export "_start") (call $f)))
            """);
        var refused = HostileModules.Bytes($$"""
            (module
              (import "{{WASI}}" "fd_read" (func (param i32 i32 i32 i32) (result i32)))
              (memory (export "memory") 1)
              (func (export "_start") (drop (call 0 (i32.const 0) (i32.const -8) (i32.const 1) (i32.const 8)))))
            """);
        var limits = VerifySandboxDefaults.Resolve(null, null);

        var unresolved = Sandbox.Execute(m_wasi, Wasi(import), limits);
        var plain = Sandbox.Execute(m_wasi, Wasi(refused), limits);
        var amid = Sandbox.Execute(m_wasi, Wasi(WithFunctionName(refused, 1, "x" + newline + words + newline + "y")), limits);
        var last = Sandbox.Execute(m_wasi, Wasi(WithFunctionName(refused, 1, "x" + newline + words)), limits);

        TestContext.Out.WriteLine($"import: {unresolved.Verdict}; the refused call: {plain.Verdict}; named amid: {amid.Verdict} '{amid.Fault}'; named last: {last.Verdict} '{last.Fault}'");
        Assert.Multiple(() =>
        {
            Assert.That(unresolved.Verdict, Is.EqualTo(VerifyVerdict.CompileError));
            Assert.That(unresolved.Fault, Is.Empty);
            Assert.That(amid.Verdict, Is.EqualTo(plain.Verdict), "a name changes nothing");
            Assert.That(amid.Fault, Is.Empty);
            Assert.That(last.Verdict, Is.EqualTo(plain.Verdict), "wherever in the name the words stand");
            Assert.That(last.Fault, Is.Empty);
            Assert.That(plain.Verdict, Is.Not.EqualTo(VerifyVerdict.NoResult));
        });
    }

    #endregion

    #region Tools

    private static VerifyTaskData Wasi(byte[] module)
    {
        return new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            EntryPoint = "main.wasm",
            Sources = [new VerifySourceFileData { Name = "main.wasm", Content = Convert.ToBase64String(module) }]
        };
    }

    /// <summary>Calls one function of WASI a number of times (without end for a negative number) and drops what it answers.</summary>
    private static byte[] Loop(string function, string signature, string arguments, int pages, int calls)
    {
        return HostileModules.Bytes($$"""
            (module
              (import "{{WASI}}" "{{function}}" (func $f {{signature}}))
              (memory (export "memory") {{pages}})
              (func (export "_start") (local $i i32)
                (loop $n
                  (drop (call $f {{arguments}}))
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br_if $n (i32.lt_u (local.get $i) (i32.const {{calls}}))))))
            """);
    }

    /// <summary>A start function and a _start that each turn a loop a number of times.</summary>
    private static byte[] TwoBurners(long startTurns, long mainTurns)
    {
        return HostileModules.Bytes($$"""
            (module
              (memory (export "memory") 1)
              (func $burn (param $n i64) (local $i i64)
                (loop $l
                  (local.set $i (i64.add (local.get $i) (i64.const 1)))
                  (br_if $l (i64.lt_u (local.get $i) (local.get $n)))))
              (func $init (call $burn (i64.const {{startTurns}})))
              (start $init)
              (func (export "_start") (call $burn (i64.const {{mainTurns}}))))
            """);
    }

    /// <summary>Appends a name section that names one function.</summary>
    private static byte[] WithFunctionName(byte[] module, int index, string name)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var map = new List<byte> { 1, (byte)index, (byte)nameBytes.Length };
        map.AddRange(nameBytes);

        var content = new List<byte> { 4 };
        content.AddRange("name"u8.ToArray());
        content.Add(1);
        content.Add((byte)map.Count);
        content.AddRange(map);

        var result = new List<byte>(module) { 0, (byte)content.Count };
        result.AddRange(content);
        return result.ToArray();
    }

    #endregion
}
