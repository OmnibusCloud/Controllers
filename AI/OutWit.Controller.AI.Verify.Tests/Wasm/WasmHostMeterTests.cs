using OutWit.Controller.AI.Verify.Tests.Sandbox;
using OutWit.Controller.AI.Wasm;
using Wasmtime;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The fuel rules of the shared host for a run whose calls cost fuel (findings N-7, N-8 and N-11
/// of the audit of 2026-11-18, with its inputs): every call into the host costs a fixed amount,
/// whoever answers it; a wait, the arguments and the environment cost the bytes they move; a
/// start function has the copy allowance and nothing of the budget; and the deadline is known by
/// the trap's code, never by words a module can put into an error.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class WasmHostMeterTests
{
    #region Constants

    private const string WASI = "wasi_snapshot_preview1";

    private const long FUEL = 50_000_000_000;
    private const long MEMORY_BYTES = 64L << 20;
    private const int WALL_MS = 60_000;

    private const int CALLS = 200;

    /// <summary>What one turn of the loop around a call costs at the most, the call's own arguments included.</summary>
    private const int LOOP_FUEL = 40;

    #endregion

    #region Fields

    private WasmHost m_host = null!;
    private string m_root = null!;
    private string m_folder = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_host = new WasmHost();
        m_root = Path.Combine(Path.GetTempPath(), $"witai_wasmmeter_{Guid.NewGuid():N}");
        m_folder = Path.Combine(m_root, "in");
        Directory.CreateDirectory(m_folder);
        File.WriteAllText(Path.Combine(m_folder, "a.txt"), "content");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_host?.Dispose();
        if (m_root != null && Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Minimum Tests

    // Answered by the host's clock and its taken-away calls.
    [TestCase("clock_time_get", "(param i32 i64 i32) (result i32)", "(i32.const 0) (i64.const 0) (i32.const 64)")]
    [TestCase("poll_oneoff", "(param i32 i32 i32 i32) (result i32)", "(i32.const 4096) (i32.const 8192) (i32.const 0) (i32.const 64)")]
    [TestCase("fd_renumber", "(param i32 i32) (result i32)", "(i32.const 98) (i32.const 99)")]
    [TestCase("fd_readdir", "(param i32 i32 i32 i64 i32) (result i32)", "(i32.const 3) (i32.const 64) (i32.const 64) (i64.const 0) (i32.const 200)")]
    [TestCase("fd_filestat_set_times", "(param i32 i64 i64 i32) (result i32)", "(i32.const 1) (i64.const 0) (i64.const 0) (i32.const 0)")]
    [TestCase("path_filestat_set_times", "(param i32 i32 i32 i32 i64 i64 i32) (result i32)", "(i32.const 3) (i32.const 0) (i32.const 1024) (i32.const 5) (i64.const 0) (i64.const 0) (i32.const 0)")]
    // Looked into by the host before WASI answers.
    [TestCase("random_get", "(param i32 i32) (result i32)", "(i32.const 64) (i32.const 0)")]
    [TestCase("clock_res_get", "(param i32 i32) (result i32)", "(i32.const 0) (i32.const 64)")]
    [TestCase("fd_write", "(param i32 i32 i32 i32) (result i32)", "(i32.const 2) (i32.const 0) (i32.const 0) (i32.const 64)")]
    [TestCase("fd_read", "(param i32 i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 0) (i32.const 0) (i32.const 64)")]
    [TestCase("fd_close", "(param i32) (result i32)", "(i32.const 99)")]
    [TestCase("fd_filestat_get", "(param i32 i32) (result i32)", "(i32.const 99) (i32.const 64)")]
    [TestCase("path_filestat_get", "(param i32 i32 i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 0) (i32.const 1024) (i32.const 0) (i32.const 64)")]
    [TestCase("path_open", "(param i32 i32 i32 i32 i32 i64 i64 i32 i32) (result i32)", "(i32.const 99) (i32.const 0) (i32.const 1024) (i32.const 0) (i32.const 0) (i64.const 0) (i64.const 0) (i32.const 0) (i32.const 64)")]
    // Left to WASI as they come.
    [TestCase("sched_yield", "(result i32)", "")]
    [TestCase("args_sizes_get", "(param i32 i32) (result i32)", "(i32.const 64) (i32.const 72)")]
    [TestCase("environ_sizes_get", "(param i32 i32) (result i32)", "(i32.const 64) (i32.const 72)")]
    [TestCase("fd_fdstat_get", "(param i32 i32) (result i32)", "(i32.const 1) (i32.const 64)")]
    [TestCase("fd_fdstat_set_flags", "(param i32 i32) (result i32)", "(i32.const 99) (i32.const 0)")]
    [TestCase("fd_prestat_get", "(param i32 i32) (result i32)", "(i32.const 3) (i32.const 64)")]
    [TestCase("fd_seek", "(param i32 i64 i32 i32) (result i32)", "(i32.const 99) (i64.const 0) (i32.const 0) (i32.const 64)")]
    [TestCase("fd_tell", "(param i32 i32) (result i32)", "(i32.const 99) (i32.const 64)")]
    [TestCase("fd_sync", "(param i32) (result i32)", "(i32.const 99)")]
    [TestCase("fd_datasync", "(param i32) (result i32)", "(i32.const 99)")]
    [TestCase("fd_advise", "(param i32 i64 i64 i32) (result i32)", "(i32.const 99) (i64.const 0) (i64.const 0) (i32.const 0)")]
    [TestCase("fd_allocate", "(param i32 i64 i64) (result i32)", "(i32.const 99) (i64.const 0) (i64.const 0)")]
    [TestCase("fd_filestat_set_size", "(param i32 i64) (result i32)", "(i32.const 99) (i64.const 0)")]
    [TestCase("path_unlink_file", "(param i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 1024) (i32.const 0)")]
    [TestCase("proc_raise", "(param i32) (result i32)", "(i32.const 0)")]
    [TestCase("sock_shutdown", "(param i32 i32) (result i32)", "(i32.const 99) (i32.const 0)")]
    public void N8EveryCallIntoTheHostCostsTheFixedPriceTest(string function, string signature, string arguments)
    {
        var module = Loop(function, signature, arguments, 2, CALLS);

        var charged = Run(module, charge: true);
        var free = Run(module, charge: false);

        TestContext.Out.WriteLine($"{function}: {charged.FuelConsumed} units for {CALLS} calls with the price, {free.FuelConsumed} without");
        Assert.Multiple(() =>
        {
            Assert.That(charged.Verdict, Is.EqualTo(WasmHostVerdict.Exited), charged.Fault);
            Assert.That(free.Verdict, Is.EqualTo(WasmHostVerdict.Exited), free.Fault);
            Assert.That(charged.FuelConsumed, Is.InRange(CALLS * WasmHostMeter.CALL_FUEL, CALLS * (WasmHostMeter.CALL_FUEL + LOOP_FUEL)), "a call costs the fixed price, and these move no bytes");
            Assert.That(free.FuelConsumed, Is.LessThan(CALLS * LOOP_FUEL), "a run that does not ask for the price pays for its instructions only");
        });
    }

    [TestCase("sched_yield", "(result i32)", "")]
    [TestCase("args_sizes_get", "(param i32 i32) (result i32)", "(i32.const 64) (i32.const 72)")]
    [TestCase("args_get", "(param i32 i32) (result i32)", "(i32.const 64) (i32.const 2048)")]
    [TestCase("environ_sizes_get", "(param i32 i32) (result i32)", "(i32.const 64) (i32.const 72)")]
    [TestCase("environ_get", "(param i32 i32) (result i32)", "(i32.const 64) (i32.const 2048)")]
    [TestCase("fd_fdstat_get", "(param i32 i32) (result i32)", "(i32.const 1) (i32.const 64)")]
    [TestCase("fd_fdstat_get", "(param i32 i32) (result i32)", "(i32.const 99) (i32.const 64)")]
    [TestCase("fd_fdstat_set_flags", "(param i32 i32) (result i32)", "(i32.const 99) (i32.const 0)")]
    [TestCase("fd_prestat_get", "(param i32 i32) (result i32)", "(i32.const 3) (i32.const 64)")]
    [TestCase("fd_prestat_get", "(param i32 i32) (result i32)", "(i32.const 9) (i32.const 64)")]
    [TestCase("fd_prestat_dir_name", "(param i32 i32 i32) (result i32)", "(i32.const 3) (i32.const 64) (i32.const 3)")]
    [TestCase("fd_seek", "(param i32 i64 i32 i32) (result i32)", "(i32.const 99) (i64.const 0) (i32.const 0) (i32.const 64)")]
    [TestCase("fd_tell", "(param i32 i32) (result i32)", "(i32.const 99) (i32.const 64)")]
    [TestCase("fd_sync", "(param i32) (result i32)", "(i32.const 99)")]
    [TestCase("fd_datasync", "(param i32) (result i32)", "(i32.const 99)")]
    [TestCase("fd_advise", "(param i32 i64 i64 i32) (result i32)", "(i32.const 99) (i64.const 0) (i64.const 0) (i32.const 0)")]
    [TestCase("fd_allocate", "(param i32 i64 i64) (result i32)", "(i32.const 99) (i64.const 0) (i64.const 0)")]
    [TestCase("fd_filestat_set_size", "(param i32 i64) (result i32)", "(i32.const 99) (i64.const 0)")]
    [TestCase("fd_fdstat_set_rights", "(param i32 i64 i64) (result i32)", "(i32.const 99) (i64.const 0) (i64.const 0)")]
    [TestCase("path_create_directory", "(param i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 1024) (i32.const 5)")]
    [TestCase("path_remove_directory", "(param i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 1024) (i32.const 5)")]
    [TestCase("path_unlink_file", "(param i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 1024) (i32.const 5)")]
    [TestCase("path_rename", "(param i32 i32 i32 i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 1024) (i32.const 5) (i32.const 99) (i32.const 1024) (i32.const 5)")]
    [TestCase("path_symlink", "(param i32 i32 i32 i32 i32) (result i32)", "(i32.const 1024) (i32.const 5) (i32.const 99) (i32.const 1024) (i32.const 5)")]
    [TestCase("path_link", "(param i32 i32 i32 i32 i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 0) (i32.const 1024) (i32.const 5) (i32.const 99) (i32.const 1024) (i32.const 5)")]
    [TestCase("path_readlink", "(param i32 i32 i32 i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 1024) (i32.const 5) (i32.const 2048) (i32.const 64) (i32.const 64)")]
    [TestCase("proc_raise", "(param i32) (result i32)", "(i32.const 0)")]
    [TestCase("sock_accept", "(param i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 0) (i32.const 64)")]
    [TestCase("sock_recv", "(param i32 i32 i32 i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 0) (i32.const 0) (i32.const 0) (i32.const 64) (i32.const 72)")]
    [TestCase("sock_send", "(param i32 i32 i32 i32 i32) (result i32)", "(i32.const 99) (i32.const 0) (i32.const 0) (i32.const 0) (i32.const 64)")]
    [TestCase("sock_shutdown", "(param i32 i32) (result i32)", "(i32.const 99) (i32.const 0)")]
    public void N8ACallTheHostOnlyMetersAnswersWhatWasiAnswersTest(string function, string signature, string arguments)
    {
        // The exit code is what the call answered. Without the price the call is WASI's own.
        var module = Answer(function, signature, arguments);

        var charged = Run(module, charge: true);
        var plain = Run(module, charge: false);

        TestContext.Out.WriteLine($"{function} {arguments}: {charged.Verdict} {charged.ExitCode}");
        Assert.Multiple(() =>
        {
            Assert.That(plain.Verdict, Is.EqualTo(WasmHostVerdict.Exited), plain.Fault);
            Assert.That(charged.Verdict, Is.EqualTo(plain.Verdict), charged.Fault);
            Assert.That(charged.ExitCode, Is.EqualTo(plain.ExitCode), "the same answer");
            Assert.That(charged.Stdout, Is.EqualTo(plain.Stdout));
        });
    }

    #endregion

    #region Bytes Tests

    [Test]
    public void N8AWaitCostsTheBytesOfItsRecordsTest()
    {
        // The audit's call: 4,096 subscriptions read (196 KB) and as many events written (131 KB),
        // 20,000 times over for 260,001 units of fuel: 58,617 units a second.
        const int WAITS = 50;
        var module = Loop("poll_oneoff", "(param i32 i32 i32 i32) (result i32)", "(i32.const 0) (i32.const 200000) (i32.const 4096) (i32.const 400000)", 8, WAITS);
        var each = WasmHostMeter.CALL_FUEL + 4096L * (WasmHostLinker.SUBSCRIPTION_SIZE + WasmHostLinker.EVENT_SIZE);

        var outcome = Run(module, charge: true);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Fault);
            Assert.That(outcome.FuelConsumed, Is.InRange(WAITS * each, WAITS * (each + LOOP_FUEL)), "48 bytes a subscription, 32 an event, and the call");
        });
    }

    [Test]
    public void N8AWaitWithoutEndEndsByItsFuelTest()
    {
        // The audit's loop ran until the wall-clock backstop (50 s) on a few thousand units.
        var module = Loop("poll_oneoff", "(param i32 i32 i32 i32) (result i32)", "(i32.const 0) (i32.const 200000) (i32.const 4096) (i32.const 400000)", 8, -1);

        var outcome = Run(module, charge: true, fuel: 500_000_000);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.OutOfFuel));
            Assert.That(outcome.FuelConsumed, Is.EqualTo(500_000_000));
        });
    }

    [Test]
    public void N8TheArgumentsAndTheEnvironmentCostTheirBytesEveryTimeTheyAreCopiedTest()
    {
        // The audit's call: the arguments of a run copied into the guest 2,000 times for 22,001 units.
        var big = new string('a', 60_000);
        var arguments = Loop("args_get", "(param i32 i32) (result i32)", "(i32.const 64) (i32.const 2048)", 4, CALLS);
        var environment = Loop("environ_get", "(param i32 i32) (result i32)", "(i32.const 64) (i32.const 2048)", 4, CALLS);

        // Each string with its end and its pointer: "main.wasm" and the big one; NAME=value.
        var argumentBytes = (9 + 1 + 4) + (60_000 + 1 + 4);
        var environmentBytes = 4 + 1 + 60_000 + 1 + 4;

        var copied = Run(arguments, charge: true, argv: ["main.wasm", big]);
        var listed = Run(environment, charge: true, environment: [("NAME", big)]);

        Assert.Multiple(() =>
        {
            Assert.That(copied.Verdict, Is.EqualTo(WasmHostVerdict.Exited), copied.Fault);
            Assert.That(copied.FuelConsumed, Is.InRange(CALLS * (WasmHostMeter.CALL_FUEL + argumentBytes), CALLS * (WasmHostMeter.CALL_FUEL + argumentBytes + LOOP_FUEL)));
            Assert.That(listed.Verdict, Is.EqualTo(WasmHostVerdict.Exited), listed.Fault);
            Assert.That(listed.FuelConsumed, Is.InRange(CALLS * (WasmHostMeter.CALL_FUEL + environmentBytes), CALLS * (WasmHostMeter.CALL_FUEL + environmentBytes + LOOP_FUEL)));
        });
    }

    [Test]
    public void N8ACallThatIsNotPaidForEndsTheRunAsOutOfFuelTest()
    {
        // Fuel for the instructions before the call and not for the call.
        var outcome = Run(Loop("sched_yield", "(result i32)", "", 1, 1), charge: true, fuel: WasmHostMeter.CALL_FUEL / 2);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.OutOfFuel));
            Assert.That(outcome.Trap, Is.EqualTo(TrapCode.OutOfFuel));
            Assert.That(outcome.FuelConsumed, Is.EqualTo(WasmHostMeter.CALL_FUEL / 2));
        });
    }

    [Test]
    public void N8TheCallsOfARunAreCountedByNameWhenAskedTest()
    {
        var census = new WasmHostCallCensus();
        var module = Loop("poll_oneoff", "(param i32 i32 i32 i32) (result i32)", "(i32.const 0) (i32.const 200000) (i32.const 2) (i32.const 400000)", 8, 3);

        var outcome = Run(module, charge: true, census: census);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Fault);
            Assert.That(census.Lines(), Is.EqualTo(new[] { $"poll_oneoff 3 {3 * 2 * (WasmHostLinker.SUBSCRIPTION_SIZE + WasmHostLinker.EVENT_SIZE)}" }));
            Assert.That(census.Calls, Is.EqualTo(3));
        });
    }

    #endregion

    #region Start Function Tests

    [Test]
    public void N7AStartFunctionHasTheCopyAllowanceAndNothingOfTheBudgetTest()
    {
        // The audit's module: a start function that burns two hundred million units before
        // _start, under a budget of a few thousand. It passed, and none of it was reported.
        const long ONE_MEBIBYTE = 1L << 20;
        var allowance = WasmHostLimits.InstantiationFuel(ONE_MEBIBYTE);

        var greedy = Run(TwoBurners(40_000_000, 0), charge: true, fuel: FUEL, memoryBytes: ONE_MEBIBYTE);
        var modest = Run(TwoBurners(1000, 1000), charge: true, fuel: FUEL, memoryBytes: ONE_MEBIBYTE);

        Assert.Multiple(() =>
        {
            Assert.That(allowance, Is.EqualTo(ONE_MEBIBYTE + WasmHostLimits.MAX_TABLES * WasmHostLimits.MAX_TABLE_ELEMENTS), "what copying segments can cost, and nothing of the budget");
            Assert.That(greedy.Verdict, Is.EqualTo(WasmHostVerdict.OutOfFuel), "fifty thousand million units of budget buy a start function nothing");
            Assert.That(modest.Verdict, Is.EqualTo(WasmHostVerdict.Exited), modest.Fault);
            Assert.That(modest.FuelConsumed, Is.LessThan(20_000), "what _start burned");
        });
    }

    #endregion

    #region Deadline Tests

    [Test]
    public void N11WordsOfTheClockInAnErrorAreNotTheClockTest()
    {
        // The audit's two modules. An import nobody defines, its module named with the words of
        // the deadline on a line of their own; and a call the engine refuses, made from a function
        // named so in the name section. Both read as the clock: no verdict, the machine's fault.
        var words = "x\\0a" + WasmHostTraps.INTERRUPT_CAUSE + "\\0ay";
        var import = HostileModules.Bytes($$"""
            (module
              (import "{{words}}" "f" (func $f))
              (memory (export "memory") 1)
              (func (export "_start") (call $f)))
            """);

        var refused = HostileModules.Bytes($$"""
            (module
              (import "{{WASI}}" "fd_read" (func (param i32 i32 i32 i32) (result i32)))
              (memory (export "memory") 1)
              (func (export "_start") (drop (call 0 (i32.const 0) (i32.const -8) (i32.const 1) (i32.const 8)))))
            """);
        var newline = (char)10;
        var named = WithFunctionName(refused, 1, "x" + newline + WasmHostTraps.INTERRUPT_CAUSE);

        var unresolved = Run(import, charge: true);
        var plain = Run(refused, charge: true);
        var forged = Run(named, charge: true);
        var uncharged = Run(named, charge: false);

        TestContext.Out.WriteLine($"import: {unresolved.Verdict}; the refused call: {plain.Verdict} {plain.ExitCode}; named: {forged.Verdict} {forged.ExitCode}; without the price: {uncharged.Verdict} {uncharged.ExitCode}");
        Assert.Multiple(() =>
        {
            Assert.That(unresolved.Verdict, Is.EqualTo(WasmHostVerdict.NotInstantiated));
            Assert.That(forged.Verdict, Is.Not.EqualTo(WasmHostVerdict.Interrupted));
            Assert.That(forged.Verdict, Is.EqualTo(plain.Verdict), "a name changes nothing");
            Assert.That(uncharged.Verdict, Is.Not.EqualTo(WasmHostVerdict.Interrupted));
        });
    }

    [Test]
    public void N11TheClockInsideACallThroughTheHostIsKnownByItsCodeTest()
    {
        // A program that does nothing but call through the host: the deadline lands inside such a
        // call, or between two. Every run is the clock's, whichever it was.
        var module = Loop("fd_fdstat_get", "(param i32 i32) (result i32)", "(i32.const 1) (i32.const 64)", 2, -1);
        var others = new List<string>();

        for (var run = 0; run < 12; run++)
        {
            var outcome = Run(module, charge: run % 2 == 0, fuel: long.MaxValue / 4, wallMs: 30);
            if (outcome.Verdict != WasmHostVerdict.Interrupted || outcome.Trap != TrapCode.Interrupt)
                others.Add($"run {run}: {outcome.Verdict} {outcome.Trap} {outcome.Fault}");
        }

        Assert.That(others, Is.Empty);
    }

    #endregion

    #region Tools

    private WasmHostRunOutcome Run(byte[] bytes, bool charge, long fuel = FUEL, long memoryBytes = MEMORY_BYTES, int wallMs = WALL_MS,
        IReadOnlyList<string>? argv = null, IReadOnlyList<(string Name, string Value)>? environment = null, WasmHostCallCensus? census = null)
    {
        using var module = Wasmtime.Module.FromBytes(m_host.Engine, "main.wasm", bytes);
        return m_host.Run(module, new WasmHostRunRequest
        {
            Argv = argv ?? ["main.wasm"],
            Environment = environment ?? [],
            Preopens = [new WasmHostPreopen(m_folder, "/in")],
            CaptureDirectory = Path.Combine(m_root, "capture"),
            Fuel = fuel,
            // The fuel of a run is the program's own, as a controller asks for it: what making the
            // module ready costs differs between platforms.
            FuelAfterInstantiation = true,
            MemoryBytes = memoryBytes,
            WallMs = wallMs,
            ChargeHostCalls = charge,
            Census = census
        });
    }

    /// <summary>Calls one function of WASI a number of times (without end for a negative number) and drops what it answers.</summary>
    private static byte[] Loop(string function, string signature, string arguments, int pages, int calls)
    {
        return HostileModules.Bytes($$"""
            (module
              (import "{{WASI}}" "{{function}}" (func $f {{signature}}))
              (memory (export "memory") {{pages}})
              (data (i32.const 1024) "a.txt")
              (func (export "_start") (local $i i32)
                (loop $n
                  (drop (call $f {{arguments}}))
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br_if $n (i32.lt_u (local.get $i) (i32.const {{calls}}))))))
            """);
    }

    /// <summary>Calls one function of WASI once and exits with what it answered.</summary>
    private static byte[] Answer(string function, string signature, string arguments)
    {
        return HostileModules.Bytes($$"""
            (module
              (import "{{WASI}}" "{{function}}" (func $f {{signature}}))
              (import "{{WASI}}" "proc_exit" (func $exit (param i32)))
              (memory (export "memory") 2)
              (data (i32.const 1024) "a.txt")
              (func (export "_start")
                (call $exit (call $f {{arguments}}))))
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
