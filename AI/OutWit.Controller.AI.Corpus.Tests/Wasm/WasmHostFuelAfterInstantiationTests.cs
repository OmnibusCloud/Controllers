using OutWit.Controller.AI.Wasm;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Corpus.Tests.Wasm;

/// <summary>
/// The shared host's opt-in that Corpus uses for the PDF stage, on Corpus's own copy of the host:
/// with the budget given after instantiation, the fuel a run reports and the verdict at a budget
/// are the guest's own — a module with a megabyte of data segments costs exactly what the same
/// module without them costs, under a budget smaller than its data — while without it (Verify's
/// behaviour, unchanged) instantiation's copy of the data segments is charged where the platform
/// copies them (one unit a byte on Windows, where wasmtime has no copy-on-write memory image).
/// </summary>
[TestFixture]
public sealed class WasmHostFuelAfterInstantiationTests
{
    #region Constants

    private const int DATA_BYTES = 1 << 20;
    private const int LOOP = 1000;
    private const long MEMORY_BYTES = 64L << 20;
    private const int WALL_MS = 30_000;

    #endregion

    #region Fields

    private WasmHost m_host = null!;
    private WasmModule m_withData = null!;
    private WasmModule m_withoutData = null!;
    private string m_root = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_host = new WasmHost();
        m_withData = WasmModule.FromText(m_host.Engine, "with-data", Module(DATA_BYTES));
        m_withoutData = WasmModule.FromText(m_host.Engine, "without-data", Module(0));
        m_root = Path.Combine(Path.GetTempPath(), $"witai_corpus_fuel_{Guid.NewGuid():N}");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_withData?.Dispose();
        m_withoutData?.Dispose();
        m_host?.Dispose();
        if (m_root != null && Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void TheBudgetAfterInstantiationCountsTheGuestOnlyTest()
    {
        var withData = Run(m_withData, 1_000_000_000, afterInstantiation: true);
        var withoutData = Run(m_withoutData, 1_000_000_000, afterInstantiation: true);

        Assert.Multiple(() =>
        {
            Assert.That(withData.Verdict, Is.EqualTo(WasmHostVerdict.Exited));
            Assert.That(withoutData.Verdict, Is.EqualTo(WasmHostVerdict.Exited));
            Assert.That(withData.FuelConsumed, Is.EqualTo(withoutData.FuelConsumed), "a megabyte of data segments costs the guest nothing");
            Assert.That(withData.FuelConsumed, Is.GreaterThan(LOOP).And.LessThan(100 * LOOP), "the loop is what is counted");
        });
    }

    [Test]
    public void ABudgetSmallerThanTheDataSegmentsStillRunsTheGuestTest()
    {
        // On a platform that copies the segments, the copy alone is ten times this budget.
        var outcome = Run(m_withData, DATA_BYTES / 10, afterInstantiation: true);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Fault);
            Assert.That(outcome.ExitCode, Is.Zero);
        });
    }

    [Test]
    public void TheGuestsOwnWorkStillRunsOutOfFuelTest()
    {
        var outcome = Run(m_withoutData, LOOP / 2, afterInstantiation: true);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.OutOfFuel));
            Assert.That(outcome.FuelConsumed, Is.EqualTo(LOOP / 2), "a budget stop reports its whole budget");
        });
    }

    [Test]
    public void WithoutTheOptionInstantiationIsChargedWhereThePlatformCopiesTest()
    {
        var withData = Run(m_withData, 1_000_000_000, afterInstantiation: false);
        var withoutData = Run(m_withoutData, 1_000_000_000, afterInstantiation: false);

        Assert.Multiple(() =>
        {
            Assert.That(withData.Verdict, Is.EqualTo(WasmHostVerdict.Exited));
            Assert.That(withData.FuelConsumed, Is.GreaterThanOrEqualTo(withoutData.FuelConsumed));
            if (OperatingSystem.IsWindows())
                Assert.That(withData.FuelConsumed - withoutData.FuelConsumed, Is.GreaterThanOrEqualTo(DATA_BYTES), "Windows copies the data segments at instantiation and charges a unit a byte");
        });
    }

    #endregion

    #region Tools

    private WasmHostRunOutcome Run(WasmModule module, long fuel, bool afterInstantiation)
    {
        return m_host.Run(module, new WasmHostRunRequest
        {
            Argv = ["guest"],
            CaptureDirectory = m_root,
            Fuel = fuel,
            FuelAfterInstantiation = afterInstantiation,
            MemoryBytes = MEMORY_BYTES,
            WallMs = WALL_MS
        });
    }

    /// <summary>A module with one active data segment of so many bytes and a <c>_start</c> that counts to <see cref="LOOP"/>.</summary>
    private static string Module(int dataBytes)
    {
        var data = dataBytes == 0 ? "" : $"(data (i32.const 0) \"{new string('a', dataBytes)}\")";
        return $$"""
            (module
              (memory (export "memory") 32)
              {{data}}
              (func (export "_start") (local $i i32)
                (loop $next
                  (local.set $i (i32.add (local.get $i) (i32.const 1)))
                  (br_if $next (i32.lt_u (local.get $i) (i32.const {{LOOP}}))))))
            """;
    }

    #endregion
}
