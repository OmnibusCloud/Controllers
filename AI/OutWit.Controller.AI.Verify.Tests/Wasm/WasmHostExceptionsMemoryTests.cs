using System.Diagnostics;
using OutWit.Controller.AI.Wasm;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The memory a guest's exceptions take outside its linear memory. An exception is an object in a
/// heap the store manages beside the linear memory. A guest throws and catches a hundred thousand
/// of them under a cap of one page, and the test writes down what the process's memory did
/// meanwhile, next to the same loop without exceptions: it holds that run to its verdict and to
/// nothing else, the numbers are an observation of one machine, not a bound. The bound is the
/// second test: the store's cap of a memory holds the heap of the exceptions as it holds the
/// linear memory, each on its own, so a guest that keeps its exceptions is refused more at the
/// cap.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class WasmHostExceptionsMemoryTests
{
    #region Constants

    private const int EXCEPTIONS = 100_000;
    private const long FUEL = 2_000_000_000;
    private const long ONE_PAGE = 65536;
    private const int WALL_MS = 120_000;
    private const int SAMPLE_MS = 1;

    /// <summary>What the process may grow by while a guest is refused at a cap of one page: far above the noise of a test process, far below what the guest asked for.</summary>
    private const long HELD_BOUND_BYTES = 4L << 20;

    #endregion

    #region Fields

    private WasmHost m_host = null!;
    private string m_root = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_host = new WasmHost(parallelCompilation: true, explicitTraps: false, exceptions: true);
        m_root = Path.Combine(Path.GetTempPath(), $"witai_wasmthrowmem_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_root);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        m_host?.Dispose();
        if (m_root != null && Directory.Exists(m_root))
            Directory.Delete(m_root, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void AHundredThousandExceptionsUnderACapOfOnePageEndWithTheirVerdictTest()
    {
        // The same loop twice: the value comes back as a result, then as an exception.
        var returned = Measure(WasmHostExceptionModules.Sum(EXCEPTIONS, throws: false));
        var thrown = Measure(WasmHostExceptionModules.Sum(EXCEPTIONS, throws: true));

        TestContext.Out.WriteLine($"{EXCEPTIONS} values returned: {Describe(returned)}");
        TestContext.Out.WriteLine($"{EXCEPTIONS} values thrown and caught: {Describe(thrown)}");
        TestContext.Out.WriteLine($"the exceptions took {Mebibytes(thrown.Highest.Private - thrown.Before.Private - (returned.Highest.Private - returned.Before.Private))} MiB of private memory more than the loop without them, at the highest point");

        Assert.Multiple(() =>
        {
            Assert.That(returned.Outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), returned.Outcome.Stderr);
            Assert.That(returned.Outcome.ExitCode, Is.EqualTo(WasmHostExceptionModules.SumExit(EXCEPTIONS)));
            Assert.That(thrown.Outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), thrown.Outcome.Stderr);
            Assert.That(thrown.Outcome.ExitCode, Is.EqualTo(WasmHostExceptionModules.SumExit(EXCEPTIONS)), "every value thrown was caught");
            Assert.That(thrown.Outcome.PeakMemoryBytes, Is.EqualTo(ONE_PAGE), "the linear memory stayed at its page: the exceptions are not in it");
        });
    }

    [Test]
    public void AGuestThatKeepsItsExceptionsIsRefusedMoreAtTheMemoryCapTest()
    {
        // An exception of four 64-bit values is an object of 64 bytes: a hundred of them fit a
        // heap of one page, a hundred thousand are a hundred times the cap. The engine refuses
        // the allocation that does not fit: an error of the guest's call, neither a trap nor an
        // exit, which the host reports as the engine's error with the engine's own words.
        var few = Measure(WasmHostExceptionModules.Keep(100));
        var many = Measure(WasmHostExceptionModules.Keep(EXCEPTIONS));

        TestContext.Out.WriteLine($"a hundred exceptions kept: {Describe(few)}");
        TestContext.Out.WriteLine($"{EXCEPTIONS} exceptions to keep: {Describe(many)}; the engine: {many.Outcome.EngineError?.Message.ReplaceLineEndings(" ")}");

        Assert.Multiple(() =>
        {
            Assert.That(few.Outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), few.Outcome.Stderr);
            Assert.That(few.Outcome.ExitCode, Is.EqualTo(WasmHostExceptionModules.FINISHED_EXIT));
            Assert.That(many.Outcome.Verdict, Is.EqualTo(WasmHostVerdict.EngineError), "the cap ended the run, not the table's own limit and not the fuel");
            Assert.That(many.Outcome.Fault, Is.EqualTo(WasmHost.ENGINE_ERROR_FAULT));
            Assert.That(many.Outcome.EngineError?.Message, Does.Contain("GC heap out of memory"));
            Assert.That(many.Outcome.PeakMemoryBytes, Is.EqualTo(ONE_PAGE));
            Assert.That(many.Highest.Private - many.Before.Private, Is.LessThan(HELD_BOUND_BYTES), "a hundred thousand kept exceptions would be 6 MiB and more");
        });
    }

    #endregion

    #region Tools

    /// <summary>
    /// Runs a module under the cap of one page and samples the process's memory while it runs.
    /// The module is compiled before the first sample: what compiling takes is not the run's.
    /// </summary>
    private (WasmHostRunOutcome Outcome, Sample Before, Sample Highest, Sample After) Measure(byte[] bytes)
    {
        using var module = Wasmtime.Module.FromBytes(m_host.Engine, "main.wasm", bytes);
        var request = new WasmHostRunRequest
        {
            Argv = ["main.wasm"],
            CaptureDirectory = Path.Combine(m_root, "capture"),
            Fuel = FUEL,
            FuelAfterInstantiation = true,
            MemoryBytes = ONE_PAGE,
            WallMs = WALL_MS
        };

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = Sample.Now();
        var highest = before;
        using var done = new ManualResetEventSlim(false);
        var sampler = new Thread(() =>
        {
            do
            {
                highest = highest.Highest(Sample.Now());
            } while (!done.Wait(SAMPLE_MS));
        })
        {
            IsBackground = true
        };
        sampler.Start();

        WasmHostRunOutcome outcome;
        try
        {
            outcome = m_host.Run(module, request);
        }
        finally
        {
            done.Set();
            sampler.Join();
        }

        return (outcome, before, highest, Sample.Now());
    }

    private static string Describe((WasmHostRunOutcome Outcome, Sample Before, Sample Highest, Sample After) run)
    {
        return $"{run.Outcome.Verdict} {run.Outcome.ExitCode} in {run.Outcome.WallMs} ms, fuel {run.Outcome.FuelConsumed}; "
               + $"private memory {Mebibytes(run.Before.Private)} MiB before, {Mebibytes(run.Highest.Private)} at the highest, {Mebibytes(run.After.Private)} after; "
               + $"working set {Mebibytes(run.Before.Working)} MiB before, {Mebibytes(run.Highest.Working)} at the highest, {Mebibytes(run.After.Working)} after";
    }

    private static string Mebibytes(long bytes)
    {
        return (bytes / 1048576.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    #endregion

    #region Nested Types

    /// <summary>The memory of this process at one moment, as the operating system counts it.</summary>
    /// <param name="Private">The bytes only this process holds.</param>
    /// <param name="Working">The bytes it has in physical memory.</param>
    private readonly record struct Sample(long Private, long Working)
    {
        /// <summary>Reads the process's memory now.</summary>
        /// <returns>The sample.</returns>
        public static Sample Now()
        {
            using var process = Process.GetCurrentProcess();
            return new Sample(process.PrivateMemorySize64, process.WorkingSet64);
        }

        /// <summary>The higher of each number of two samples.</summary>
        /// <param name="other">The other sample.</param>
        /// <returns>The sample of the higher numbers.</returns>
        public Sample Highest(Sample other)
        {
            return new Sample(Math.Max(Private, other.Private), Math.Max(Working, other.Working));
        }
    }

    #endregion
}
