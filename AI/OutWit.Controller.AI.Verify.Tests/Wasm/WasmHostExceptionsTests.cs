using OutWit.Controller.AI.Verify.Tests.Sandbox;
using OutWit.Controller.AI.Wasm;
using Wasmtime;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// A host whose engine takes WebAssembly exceptions, beside the host every controller has had: a
/// module that throws and catches runs on the one and is refused by the other when it is
/// compiled; fuel is charged in a handler, and what a callee used before it threw stays charged
/// after the catch; a module without exceptions is the same run on both, in both trap modes; the
/// key of compiled code tells the two engines apart and is the text it always was for the old
/// one; and everything else the old engine refuses, the new one refuses in the same words.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class WasmHostExceptionsTests
{
    #region Constants

    private const long FUEL = 2_000_000_000;
    private const long SMALL_FUEL = 1_000_000;
    private const long MEMORY_BYTES = 64L << 20;
    private const int WALL_MS = 60_000;

    private const string NATIVES = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    #endregion

    #region Fields

    private WasmHost m_default = null!;
    private WasmHost m_defaultExplicit = null!;
    private WasmHost m_exceptions = null!;
    private WasmHost m_exceptionsExplicit = null!;
    private string m_root = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_default = new WasmHost();
        m_defaultExplicit = new WasmHost(parallelCompilation: true, explicitTraps: true);
        m_exceptions = new WasmHost(parallelCompilation: true, explicitTraps: false, exceptions: true);
        m_exceptionsExplicit = new WasmHost(parallelCompilation: true, explicitTraps: true, exceptions: true);
        m_root = Path.Combine(Path.GetTempPath(), $"witai_wasmthrow_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_default?.Dispose();
        m_defaultExplicit?.Dispose();
        m_exceptions?.Dispose();
        m_exceptionsExplicit?.Dispose();
        if (m_root != null && Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Engine Tests

    [Test]
    public void OnlyAHostMadeSoHasExceptionsOnTest()
    {
        using var parallel = new WasmHost(parallelCompilation: true);
        using var single = new WasmHost(parallelCompilation: false);

        Assert.Multiple(() =>
        {
            Assert.That(m_default.Exceptions, Is.False);
            Assert.That(parallel.Exceptions, Is.False);
            Assert.That(single.Exceptions, Is.False);
            Assert.That(m_defaultExplicit.Exceptions, Is.False);
            Assert.That(m_exceptions.Exceptions, Is.True);
            Assert.That(m_exceptionsExplicit.Exceptions, Is.True);
            Assert.That(m_exceptionsExplicit.ExplicitTraps, Is.True);
            Assert.That(m_exceptions.ExplicitTraps, Is.EqualTo(m_default.ExplicitTraps), "the trap mode is the process's, as without exceptions");
        });
    }

    [Test]
    public void AModuleThatThrowsAndCatchesRunsWithExceptionsOnTest()
    {
        var plain = Run(m_exceptions, WasmHostExceptionModules.ThrowAndCatch());
        var checks = Run(m_exceptionsExplicit, WasmHostExceptionModules.ThrowAndCatch());

        Assert.Multiple(() =>
        {
            Assert.That(plain.Verdict, Is.EqualTo(WasmHostVerdict.Exited), plain.Stderr);
            Assert.That(plain.ExitCode, Is.EqualTo(WasmHostExceptionModules.CAUGHT_EXIT), "the thrown value, caught, and what the guest adds after the catch");
            Assert.That(plain.Trap, Is.Null);
            Assert.That(plain.Stderr, Is.Empty);
            AssertSameRun(checks, plain, "explicit checks");
        });
    }

    [Test]
    public void TheDefaultHostRefusesAModuleWithExceptionsWhenItIsCompiledTest()
    {
        var bytes = WasmHostExceptionModules.ThrowAndCatch();

        Assert.Multiple(() =>
        {
            foreach (var host in new[] { m_default, m_defaultExplicit })
            {
                var exception = Assert.Throws<WasmtimeException>(() => Wasmtime.Module.FromBytes(host.Engine, "main.wasm", bytes));
                Assert.That(exception!.Message, Does.Contain("exceptions proposal not enabled"));
            }
        });
    }

    [Test]
    public void WhatTheDefaultHostRefusesTheHostWithExceptionsRefusesInTheSameWordsTest()
    {
        // The engines differ in the exceptions proposal and in nothing else: the GC proposal, the
        // typed function references, a second memory and 64-bit addresses stay off in both.
        var refused = new (string What, byte[] Bytes, string Words)[]
        {
            ("a struct type", WasmHostExceptionModules.GcStruct(), "without the gc feature"),
            ("a typed function reference", WasmHostExceptionModules.TypedReference(), "function references support is not enabled"),
            ("two memories", WasiModules.TwoMemories(), ""),
            ("a memory of 64-bit addresses", WasiModules.Memory64(), ""),
            ("a table of 64-bit indices", WasiModules.Table64(), "")
        };

        Assert.Multiple(() =>
        {
            foreach (var (what, bytes, words) in refused)
            {
                var byDefault = Refusal(m_default, bytes);
                var withExceptions = Refusal(m_exceptions, bytes);

                Assert.That(byDefault, Is.Not.Null.And.Contain(words), $"{what}: refused by the default engine");
                Assert.That(withExceptions, Is.EqualTo(byDefault), $"{what}: refused the same way with exceptions on");
            }
        });
    }

    #endregion

    #region Fuel Tests

    [TestCase(false)]
    [TestCase(true)]
    public void FuelIsChargedInsideAnExceptionHandlerTest(bool thrownByCallee)
    {
        // A handler that would turn a hundred million times, under fuel for a hundred thousand turns.
        var stopped = Run(m_exceptions, WasmHostExceptionModules.BurnInHandler(thrownByCallee, 100_000_000), SMALL_FUEL);
        var finished = Run(m_exceptions, WasmHostExceptionModules.BurnInHandler(thrownByCallee, 1000), SMALL_FUEL);

        Assert.Multiple(() =>
        {
            Assert.That(stopped.Verdict, Is.EqualTo(WasmHostVerdict.OutOfFuel));
            Assert.That(stopped.Trap, Is.EqualTo(TrapCode.OutOfFuel));
            Assert.That(stopped.FuelConsumed, Is.EqualTo(SMALL_FUEL), "the run ends at its budget");
            Assert.That(finished.Verdict, Is.EqualTo(WasmHostVerdict.Exited), finished.Stderr);
            Assert.That(finished.ExitCode, Is.EqualTo(WasmHostExceptionModules.FINISHED_EXIT), "the loop is in the handler: the program ends through it");
            Assert.That(finished.FuelConsumed, Is.GreaterThanOrEqualTo(8 * 1000), "every turn of the handler's loop is counted");
        });
    }

    [Test]
    public void TheFuelACalleeUsedBeforeItThrewStaysChargedAfterTheCatchTest()
    {
        // The defect of the engine's fuel at a catch (GHSA-m63x-6p34-q65x, wasmtime 47.0.0 to
        // 48.0.2): what a callee had used was given back when its exception was caught, so a
        // program that did its work in callees that throw ran on almost no fuel. Measured with
        // the 48.0.2 library of the binding's package, on these two loops without their exit:
        // ten thousand rounds (30 million units of work) ran to their end on 100,002 units, and
        // a hundred rounds were counted as 1,002 units, where the pinned library counts 801,202.
        // A native library with the defect fails both halves of this test. (A loop in the handler
        // itself, the test above, was charged by that library as well.)
        const int TURNS = 1000;
        const long FUEL_OF_A_HUNDRED_ROUNDS = 801_203;

        var stopped = Run(m_exceptions, WasmHostExceptionModules.BurnThenThrow(TURNS, 10_000), SMALL_FUEL);
        var finished = Run(m_exceptions, WasmHostExceptionModules.BurnThenThrow(TURNS, 100), SMALL_FUEL);

        TestContext.Out.WriteLine($"a hundred rounds of {TURNS} turns and a throw: {finished.FuelConsumed} units of fuel");
        Assert.Multiple(() =>
        {
            Assert.That(stopped.Verdict, Is.EqualTo(WasmHostVerdict.OutOfFuel), "ten thousand rounds are thirty times the budget");
            Assert.That(stopped.FuelConsumed, Is.EqualTo(SMALL_FUEL));
            Assert.That(finished.Verdict, Is.EqualTo(WasmHostVerdict.Exited), finished.Stderr);
            Assert.That(finished.ExitCode, Is.EqualTo(WasmHostExceptionModules.FINISHED_EXIT));
            Assert.That(finished.FuelConsumed, Is.EqualTo(FUEL_OF_A_HUNDRED_ROUNDS), "eight instructions a turn, a hundred rounds of a thousand turns, and the rounds' own");
        });
    }

    #endregion

    #region Same Run Tests

    [TestCase("hello")]
    [TestCase("exit")]
    [TestCase("echo")]
    [TestCase("out of fuel")]
    [TestCase("out of bounds")]
    [TestCase("memory refused")]
    public void AModuleWithoutExceptionsIsTheSameRunWithExceptionsOnTest(string program)
    {
        var plain = RunProgram(m_default, program);

        TestContext.Out.WriteLine($"{program}: {plain.Verdict}, exit {plain.ExitCode}, fuel {plain.FuelConsumed}");
        AssertSameRun(RunProgram(m_exceptions, program), plain, "exceptions on");
    }

    [TestCase("hello")]
    [TestCase("exit")]
    [TestCase("echo")]
    [TestCase("out of fuel")]
    [TestCase("out of bounds")]
    [TestCase("memory refused")]
    public void AModuleWithoutExceptionsIsTheSameRunInBothTrapModesWithExceptionsOnTest(string program)
    {
        if (m_default.ExplicitTraps)
            Assert.Ignore("this test process enforces shadow stacks, so its traps are explicit checks already: the two trap modes were NOT compared here");

        var plain = RunProgram(m_default, program);

        Assert.Multiple(() =>
        {
            AssertSameRun(RunProgram(m_defaultExplicit, program), plain, "explicit checks, exceptions off");
            AssertSameRun(RunProgram(m_exceptionsExplicit, program), plain, "explicit checks, exceptions on");
        });
    }

    #endregion

    #region Key Tests

    [Test]
    public void TheKeysOfCompiledCodeArePinnedTest()
    {
        Assert.Multiple(() =>
        {
            // The two keys every cache of compiled code has had: the same texts as before the third argument.
            Assert.That(WasmHost.CompiledCodeKeyOf(NATIVES, explicitTraps: true),
                Is.EqualTo("wasmtime-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef-explicit-traps"));
            Assert.That(WasmHost.CompiledCodeKeyOf(NATIVES, explicitTraps: false),
                Is.EqualTo("wasmtime-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef-signal-traps"));
            Assert.That(WasmHost.CompiledCodeKeyOf(NATIVES, explicitTraps: true, exceptions: false), Is.EqualTo(WasmHost.CompiledCodeKeyOf(NATIVES, explicitTraps: true)));
            Assert.That(WasmHost.CompiledCodeKeyOf(NATIVES, explicitTraps: false, exceptions: false), Is.EqualTo(WasmHost.CompiledCodeKeyOf(NATIVES, explicitTraps: false)));

            // The two new ones.
            Assert.That(WasmHost.CompiledCodeKeyOf(NATIVES, explicitTraps: true, exceptions: true),
                Is.EqualTo("wasmtime-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef-explicit-traps-exceptions"));
            Assert.That(WasmHost.CompiledCodeKeyOf(NATIVES, explicitTraps: false, exceptions: true),
                Is.EqualTo("wasmtime-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef-signal-traps-exceptions"));
        });
    }

    [Test]
    public void AHostsKeyFollowsItsEngineTest()
    {
        var natives = WasmHostNatives.PINNED[WasmHostNativeLibrary.PortableRid];

        Assert.Multiple(() =>
        {
            Assert.That(m_default.CompiledCodeKey, Is.EqualTo(WasmHost.CompiledCodeKeyOf(natives, m_default.ExplicitTraps)), "the key a host without exceptions always had");
            Assert.That(m_defaultExplicit.CompiledCodeKey, Is.EqualTo(WasmHost.CompiledCodeKeyOf(natives, explicitTraps: true)));
            Assert.That(m_exceptions.CompiledCodeKey, Is.EqualTo(WasmHost.CompiledCodeKeyOf(natives, m_exceptions.ExplicitTraps, exceptions: true)));
            Assert.That(m_exceptionsExplicit.CompiledCodeKey, Is.EqualTo(WasmHost.CompiledCodeKeyOf(natives, explicitTraps: true, exceptions: true)));
            Assert.That(m_exceptions.CompiledCodeKey, Is.EqualTo(m_default.CompiledCodeKey + "-exceptions"));
        });
    }

    #endregion

    #region Tools

    private WasmHostRunOutcome RunProgram(WasmHost host, string program)
    {
        return program switch
        {
            "hello" => Run(host, WasiModules.Hello("hello\n")),
            "exit" => Run(host, WasiModules.Exit(7)),
            "echo" => Run(host, WasiModules.Echo(), stdin: "abc\n"),
            "out of fuel" => Run(host, WasiModules.Spin(), SMALL_FUEL),
            "out of bounds" => Run(host, WasiModules.OutOfBounds()),
            "memory refused" => Run(host, WasiModules.GrowUntilFull(), memoryBytes: 64 * 65536),
            _ => throw new ArgumentOutOfRangeException(nameof(program), program, null)
        };
    }

    private WasmHostRunOutcome Run(WasmHost host, byte[] bytes, long fuel = FUEL, long memoryBytes = MEMORY_BYTES, string? stdin = null)
    {
        using var module = Wasmtime.Module.FromBytes(host.Engine, "main.wasm", bytes);
        return host.Run(module, new WasmHostRunRequest
        {
            Argv = ["main.wasm"],
            Stdin = stdin,
            CaptureDirectory = Path.Combine(m_root, "capture"),
            Fuel = fuel,
            FuelAfterInstantiation = true,
            MemoryBytes = memoryBytes,
            WallMs = WALL_MS
        });
    }

    /// <summary>The words an engine refuses a module with when it compiles it; null when it takes the module.</summary>
    private static string? Refusal(WasmHost host, byte[] bytes)
    {
        try
        {
            using var module = Wasmtime.Module.FromBytes(host.Engine, "main.wasm", bytes);
            return null;
        }
        catch (WasmtimeException exception)
        {
            return exception.Message;
        }
    }

    private static void AssertSameRun(WasmHostRunOutcome run, WasmHostRunOutcome other, string what)
    {
        Assert.That(run.Verdict, Is.EqualTo(other.Verdict), $"{what}: the verdict");
        Assert.That(run.ExitCode, Is.EqualTo(other.ExitCode), $"{what}: the exit code");
        Assert.That(run.Trap, Is.EqualTo(other.Trap), $"{what}: the trap");
        Assert.That(run.Fault, Is.EqualTo(other.Fault), $"{what}: the fault");
        Assert.That(run.Stdout, Is.EqualTo(other.Stdout), $"{what}: stdout");
        Assert.That(run.Stderr, Is.EqualTo(other.Stderr), $"{what}: stderr");
        Assert.That(run.FuelConsumed, Is.EqualTo(other.FuelConsumed), $"{what}: the fuel");
        Assert.That(run.PeakMemoryBytes, Is.EqualTo(other.PeakMemoryBytes), $"{what}: the peak memory");
    }

    #endregion
}
