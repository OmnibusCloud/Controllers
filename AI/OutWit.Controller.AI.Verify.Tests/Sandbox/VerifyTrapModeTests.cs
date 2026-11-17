using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The two ways the engine takes a guest's trap, held to each other (audit finding V:M-7: the
/// claim "the verdicts, the outputs and the fuel are the same either way" had no test). A host
/// under hardware stack protection runs with explicit checks, any other with hardware traps; a
/// task must not be able to tell, and neither must two copies of a task that ran on one of
/// each. The same tasks run here in two sandboxes of one process, one in each mode, and every
/// field a result has but the wall time is compared.
/// <para>
/// A process that itself enforces shadow stacks has no hardware traps to compare with: there
/// the fixture says that the comparison did not run.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyTrapModeTests : VerifySandboxTestBase
{
    #region Constants

    private const string UNREACHABLE = """(module (memory (export "memory") 1) (func (export "_start") unreachable))""";

    private const string OUT_OF_BOUNDS = """(module (memory (export "memory") 1) (func (export "_start") (drop (i32.load (i32.const 2147483632)))))""";

    private const string DIVISION_BY_ZERO = """(module (memory (export "memory") 1) (func (export "_start") (local $zero i32) (drop (i32.div_s (i32.const 1) (local.get $zero)))))""";

    private const string INTEGER_OVERFLOW = """(module (memory (export "memory") 1) (func (export "_start") (local $one i32) (local.set $one (i32.const -1)) (drop (i32.div_s (i32.const -2147483648) (local.get $one)))))""";

    private const string BAD_CONVERSION = """(module (memory (export "memory") 1) (func (export "_start") (drop (i32.trunc_f64_s (f64.const 1e300)))))""";

    private const string NULL_CALL = """(module (type $t (func)) (table 2 funcref) (memory (export "memory") 1) (func (export "_start") (call_indirect (type $t) (i32.const 1))))""";

    private const string STACK_OVERFLOW = """(module (memory (export "memory") 1) (func $again (export "_start") (call $again)))""";

    private const string OUT_OF_FUEL = """(module (memory (export "memory") 1) (func (export "_start") (local $i i64) (loop $n (local.set $i (i64.add (local.get $i) (i64.const 1))) (br $n))))""";

    private const long SMALL_FUEL = 200_000_000;

    #endregion

    #region Fields

    private VerifyWasmSandbox m_explicit = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        if (Sandbox.ExplicitTraps)
            Assert.Ignore("this test process enforces shadow stacks, so its traps are explicit checks already: the two trap modes were NOT compared here");

        m_explicit = new VerifyWasmSandbox(null, explicitTraps: true);
        Assert.That(m_explicit.ExplicitTraps, Is.True);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_explicit?.Dispose();
    }

    #endregion

    #region Tests

    [TestCase("unreachable", UNREACHABLE, VerifyVerdict.RuntimeError)]
    [TestCase("out of bounds", OUT_OF_BOUNDS, VerifyVerdict.RuntimeError)]
    [TestCase("division by zero", DIVISION_BY_ZERO, VerifyVerdict.RuntimeError)]
    [TestCase("integer overflow", INTEGER_OVERFLOW, VerifyVerdict.RuntimeError)]
    [TestCase("bad conversion", BAD_CONVERSION, VerifyVerdict.RuntimeError)]
    [TestCase("null call", NULL_CALL, VerifyVerdict.RuntimeError)]
    [TestCase("stack overflow", STACK_OVERFLOW, VerifyVerdict.RuntimeError)]
    [TestCase("out of fuel", OUT_OF_FUEL, VerifyVerdict.Timeout)]
    public void M7ATrapIsTheSameResultInBothTrapModesTest(string what, string module, VerifyVerdict verdict)
    {
        var runtime = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason);
        Assert.That(runtime, Is.Not.Null, reason);

        var task = new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            EntryPoint = "m.wasm",
            Sources = [new VerifySourceFileData { Name = "m.wasm", Content = Convert.ToBase64String(HostileModules.Bytes(module)) }]
        };
        var limits = VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = SMALL_FUEL }, null);

        var hardware = Sandbox.Execute(runtime!, task, limits);
        var checks = m_explicit.Execute(runtime!, task, limits);

        TestContext.Out.WriteLine($"{what}: {hardware.Verdict}, fuel {hardware.FuelConsumed} (hardware traps) and {checks.FuelConsumed} (explicit checks); {hardware.Stderr.Split('\n')[0]}");

        Assert.Multiple(() =>
        {
            Assert.That(hardware.Verdict, Is.EqualTo(verdict), hardware.Stderr);
            AssertSameResult(checks, hardware);
        });
    }

    [Test]
    public void M7AProgramOfEveryInterpreterIsTheSameResultInBothTrapModesTest()
    {
        // A program that computes, prints, and one that ends by an error of its language, on the
        // interpreters: millions of instructions with every kind of check in them.
        var tasks = new (VerifyResolvedRuntime Runtime, VerifyTaskData Task)[]
        {
            (Python, PythonTask("import sys\nprint(sum(i * i for i in range(20000)) // 7, 2 ** 200, 1.0 / 3)\nprint([1, 2, 3][7] if len(sys.argv) > 5 else 'in range')")),
            (Python, PythonTask("print('before')\nprint(1 // 0)")),
            (QuickJs, JsTask("let total = 0; for (let i = 0; i < 20000; i++) { total += i % 7; } console.log(total, Math.sqrt(2), [3, 1, 2].sort().join(''))")),
            (QuickJs, JsTask("function deep(n) { return deep(n + 1) + 1; } try { deep(0); } catch (error) { console.log(error instanceof RangeError); }")),
            (Lua, LuaTask("local t = {} for i = 1, 20000 do t[i] = i * i % 13 end print(#t, t[777], 10 // 3, 2 ^ 0.5)")),
            (Lua, LuaTask("print(pcall(function() error('boom') end)) local x = nil print(x.field)")),
            (Ruby, RubyTask("puts((1..2000).map { |i| i * i % 11 }.sum, 10.fdiv(3), 'abc'.reverse)"))
        };

        Assert.Multiple(() =>
        {
            foreach (var (runtime, task) in tasks)
            {
                var hardware = Run(runtime, task);
                var checks = m_explicit.Execute(runtime, task, VerifySandboxDefaults.Resolve(task.Limits, null));

                TestContext.Out.WriteLine($"{runtime.Descriptor.Id}: {hardware.Verdict}, fuel {hardware.FuelConsumed} in both modes: {hardware.FuelConsumed == checks.FuelConsumed}");
                AssertSameResult(checks, hardware);
            }
        });
    }

    #endregion

    #region Tools

    private static void AssertSameResult(VerifyResultData checks, VerifyResultData hardware)
    {
        Assert.That(checks.Verdict, Is.EqualTo(hardware.Verdict), "the verdict");
        Assert.That(checks.ExitCode, Is.EqualTo(hardware.ExitCode), "the exit code");
        Assert.That(checks.Stdout, Is.EqualTo(hardware.Stdout), "stdout");
        Assert.That(checks.Stderr, Is.EqualTo(hardware.Stderr), "stderr");
        Assert.That(checks.FuelConsumed, Is.EqualTo(hardware.FuelConsumed), "the fuel");
        Assert.That(checks.PeakMemoryBytes, Is.EqualTo(hardware.PeakMemoryBytes), "the peak memory");
        Assert.That(checks.Fault ?? "", Is.EqualTo(hardware.Fault ?? ""), "neither is a fault of the node");
    }

    #endregion
}
