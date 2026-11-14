using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The fuel a task reports and the budget it runs under are the program's own since 0.12.0: the
/// budget is given once the module is instantiated, so the copy of its data segments (which
/// Windows pays a unit a byte for, and Linux, with a copy-on-write image, does not) is in neither.
/// A raw module with a megabyte of data costs what the same module without it costs, a budget
/// smaller than the data still runs it, and the program's own work still runs out of fuel.
/// Needs no language runtime.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyFuelAccountingTests
{
    #region Constants

    private const int DATA_BYTES = 1 << 20;
    private const int LOOP = 1000;

    #endregion

    #region Fields

    private VerifyWasmSandbox m_sandbox = null!;
    private VerifyResolvedRuntime m_wasi = null!;
    private string m_root = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_root = Path.Combine(Path.GetTempPath(), $"witai_verify_fuel_{Guid.NewGuid():N}");
        m_sandbox = new VerifyWasmSandbox(m_root);
        m_wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason)!;
        Assert.That(m_wasi, Is.Not.Null, reason);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_sandbox?.Dispose();
    }

    #endregion

    #region Tests

    [Test]
    public void TheDataSegmentsCostTheProgramNothingTest()
    {
        var withData = Run(DATA_BYTES, 1_000_000_000);
        var withoutData = Run(0, 1_000_000_000);

        Assert.Multiple(() =>
        {
            Assert.That(withData.Verdict, Is.EqualTo(VerifyVerdict.Pass), withData.Stderr);
            Assert.That(withoutData.Verdict, Is.EqualTo(VerifyVerdict.Pass), withoutData.Stderr);
            Assert.That(withData.FuelConsumed, Is.EqualTo(withoutData.FuelConsumed), "a megabyte of data segments costs the program nothing, on every platform");
            Assert.That(withData.FuelConsumed, Is.GreaterThan(LOOP).And.LessThan(100 * LOOP), "the loop is what is counted");
        });
    }

    [Test]
    public void ABudgetSmallerThanTheDataStillRunsTheProgramTest()
    {
        // Where the segments are copied the copy alone is ten times this budget.
        var result = Run(DATA_BYTES, DATA_BYTES / 10);

        Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Pass), result.Stderr);
    }

    [Test]
    public void TheProgramsOwnWorkStillRunsOutOfFuelTest()
    {
        var result = Run(DATA_BYTES, LOOP / 2);

        Assert.Multiple(() =>
        {
            Assert.That(result.Verdict, Is.EqualTo(VerifyVerdict.Timeout));
            Assert.That(result.FuelConsumed, Is.EqualTo(LOOP / 2), "a budget stop reports its whole budget");
        });
    }

    #endregion

    #region Tools

    private VerifyResultData Run(int dataBytes, long fuel)
    {
        var task = new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            EntryPoint = "data.wasm",
            Sources = [new VerifySourceFileData { Name = "data.wasm", Content = Convert.ToBase64String(Module(dataBytes)) }]
        };

        return m_sandbox.Execute(m_wasi, task, VerifySandboxDefaults.Resolve(new VerifyLimitsData { FuelBudget = fuel }, null));
    }

    /// <summary>A module with one active data segment of so many bytes and a <c>_start</c> that counts to <see cref="LOOP"/>.</summary>
    private static byte[] Module(int dataBytes)
    {
        var data = dataBytes == 0 ? "" : $"(data (i32.const 0) \"{new string('a', dataBytes)}\")";
        return Wasmtime.Module.ConvertText($$"""
            (module
              (memory (export "memory") 32)
              {{data}}
              (func (export "_start") (local $i i32)
                (loop $next
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br_if $next (i32.lt_u (local.get $i) (i32.const {{LOOP}}))))))
            """);
    }

    #endregion
}
