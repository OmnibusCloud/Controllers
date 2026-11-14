using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tests.Sandbox;
using OutWit.Controller.AI.Wasm;
using Wasmtime;

namespace OutWit.Controller.AI.Verify.Tests.Wasm;

/// <summary>
/// The shared wasm host on its own, as a controller other than Verify calls it: one WASI
/// command module run in a fresh store, every way it can end a verdict (never an exception), the
/// fixed words of a crash, the memory test asked only of a failed end, and folders preopened
/// read-only or writable — the in/out shape a module that reads a file and writes its product
/// runs in. Needs only the native library of the test bin (no language runtime).
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class WasmHostTests
{
    #region Constants

    private const long FUEL = 50_000_000_000;
    private const long MEMORY_BYTES = 64L << 20;
    private const int WALL_MS = 30_000;

    #endregion

    #region Fields

    private WasmHost m_host = null!;
    private string m_root = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_host = new WasmHost();
        m_root = Path.Combine(Path.GetTempPath(), $"witai_wasmhost_{Guid.NewGuid():N}");
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

    #region Verdict Tests

    [Test]
    public void AProgramThatEndsByItselfIsExitedWithItsOutputTest()
    {
        var outcome = Run(WasiModules.Hello("hello\n"));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited));
            Assert.That(outcome.ExitCode, Is.EqualTo(0));
            Assert.That(outcome.Trap, Is.Null);
            Assert.That(outcome.Fault, Is.Null);
            Assert.That(outcome.Stdout, Is.EqualTo("hello\n"));
            Assert.That(outcome.Stderr, Is.Empty);
            Assert.That(outcome.FuelConsumed, Is.GreaterThan(0).And.LessThan(FUEL));
            Assert.That(outcome.PeakMemoryBytes, Is.EqualTo(65536));
        });
    }

    [Test]
    public void AnExitCodeIsKeptTest()
    {
        var outcome = Run(WasiModules.Exit(7));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited));
            Assert.That(outcome.ExitCode, Is.EqualTo(7));
        });
    }

    [Test]
    public void RunningOutOfFuelIsAVerdictWithoutWordsTest()
    {
        var outcome = Run(WasiModules.Spin(), fuel: 1_000_000);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.OutOfFuel));
            Assert.That(outcome.Trap, Is.EqualTo(TrapCode.OutOfFuel));
            Assert.That(outcome.ExitCode, Is.EqualTo(-1));
            Assert.That(outcome.Fault, Is.Null, "a budget stop is not named");
            Assert.That(outcome.Stderr, Is.Empty);
            Assert.That(outcome.FuelConsumed, Is.EqualTo(1_000_000));
        });
    }

    [Test]
    public void TheWallClockBackstopInterruptsTest()
    {
        var outcome = Run(WasiModules.Spin(), fuel: long.MaxValue / 2, wallMs: 200);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Interrupted));
            Assert.That(outcome.Trap, Is.EqualTo(TrapCode.Interrupt));
            Assert.That(outcome.Fault, Is.Null);
        });
    }

    [Test]
    public void ACrashIsNamedInFixedWordsTest()
    {
        var outcome = Run(WasiModules.OutOfBounds());

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Trap));
            Assert.That(outcome.Trap, Is.EqualTo(TrapCode.MemoryOutOfBounds));
            Assert.That(outcome.ExitCode, Is.EqualTo(-1));
            Assert.That(outcome.Fault, Is.EqualTo("wasm trap: out of bounds memory access (a pointer outside the memory, or a stack that overflowed)"));
            Assert.That(outcome.Stderr, Is.EqualTo(outcome.Fault), "a guest that said nothing has the host's words as its stderr");
        });
    }

    [Test]
    public void AModuleThatCannotBeInstantiatedNeverRunsTest()
    {
        var outcome = Run(WasiModules.UnknownImport("x"));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.NotInstantiated));
            Assert.That(outcome.ExitCode, Is.EqualTo(-1));
            Assert.That(outcome.Fault, Does.StartWith("module failed to instantiate: "));
            Assert.That(outcome.PeakMemoryBytes, Is.EqualTo(0));
        });
    }

    [Test]
    public void AModuleWithoutStartIsNoCommandTest()
    {
        var outcome = Run(WasiModules.NoStart("x"));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.NoStart));
            Assert.That(outcome.Fault, Is.EqualTo("module exports no _start"));
        });
    }

    [Test]
    public void AnExitStatusWasiDoesNotHaveIsAnEngineErrorTest()
    {
        // WASI preview 1 has exit statuses below 126; wasmtime 48 fails a proc_exit above that
        // with an error that is neither a trap nor an exit.
        var outcome = Run(WasiModules.Exit(200));

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.EngineError));
            Assert.That(outcome.ExitCode, Is.EqualTo(-1));
            Assert.That(outcome.Trap, Is.Null);
            Assert.That(outcome.Fault, Is.EqualTo(WasmHost.ENGINE_ERROR_FAULT));
            Assert.That(outcome.Stderr, Is.EqualTo(WasmHost.ENGINE_ERROR_FAULT));
            Assert.That(outcome.EngineError?.Message, Does.Contain("invalid exit status"));
            Assert.That(Run(WasiModules.Exit(125)).ExitCode, Is.EqualTo(125), "the highest status WASI has");
        });
    }

    [Test]
    public void VerifyKeepsLettingAnEngineErrorThroughTest()
    {
        // Verify's sandbox has always failed the batch on such an end (the engine's exception);
        // the shared host keeps that for it until a release makes it a verdict.
        var wasi = VerifyRuntimeCatalog.Resolve(null, VerifyRuntimeCatalog.WASI, out var reason);
        Assert.That(wasi, Is.Not.Null, reason);

        using var sandbox = new VerifyWasmSandbox(Path.Combine(m_root, "verify"));
        var task = new VerifyTaskData
        {
            RuntimeId = VerifyRuntimeCatalog.WASI,
            EntryPoint = "main.wasm",
            Sources = [new VerifySourceFileData { Name = "main.wasm", Content = Convert.ToBase64String(WasiModules.Exit(200)) }]
        };

        var exception = Assert.Throws<WasmtimeException>(() => sandbox.Execute(wasi!, task, VerifySandboxDefaults.Resolve(null, null)));
        Assert.That(exception!.Message, Does.Contain("invalid exit status"));
    }

    [Test]
    public void TheMemoryCeilingRefusesGrowthTest()
    {
        // The guest grows 16 pages at a time until a grow is refused, then exits with its size.
        var outcome = Run(WasiModules.GrowUntilFull(), memoryBytes: 64 * 65536);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited));
            Assert.That(outcome.ExitCode, Is.EqualTo(49), "1 page + 3 grows of 16; the fourth would pass 64");
            Assert.That(outcome.PeakMemoryBytes, Is.EqualTo(49 * 65536));
        });
    }

    [Test]
    public void TheMemoryTestIsAskedOnlyOfAFailedEndTest()
    {
        var asked = new List<string>();
        bool Always(string stderr)
        {
            asked.Add(stderr);
            return true;
        }

        var failed = Run(WasiModules.Exit(3), outOfMemory: Always);
        var crashed = Run(WasiModules.OutOfBounds(), outOfMemory: Always);
        var succeeded = Run(WasiModules.Exit(0), outOfMemory: Always);
        var stopped = Run(WasiModules.Spin(), fuel: 1_000_000, outOfMemory: Always);
        var refused = Run(WasiModules.UnknownImport("x"), outOfMemory: Always);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Verdict, Is.EqualTo(WasmHostVerdict.OutOfMemory));
            Assert.That(failed.ExitCode, Is.EqualTo(3), "the exit code stays");
            Assert.That(crashed.Verdict, Is.EqualTo(WasmHostVerdict.OutOfMemory));
            Assert.That(crashed.Trap, Is.EqualTo(TrapCode.MemoryOutOfBounds), "the trap stays");
            Assert.That(succeeded.Verdict, Is.EqualTo(WasmHostVerdict.Exited));
            Assert.That(stopped.Verdict, Is.EqualTo(WasmHostVerdict.OutOfFuel));
            Assert.That(refused.Verdict, Is.EqualTo(WasmHostVerdict.NotInstantiated));
            Assert.That(asked, Has.Count.EqualTo(2));
            Assert.That(asked[1], Does.StartWith(WasmHostTraps.FAULT_PREFIX), "the test reads the stderr the outcome carries");
        });
    }

    [Test]
    public void OutputIsCappedTest()
    {
        var outcome = Run(WasiModules.Hello("0123456789\n"), stdoutLimitBytes: 4);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Stdout, Is.EqualTo("0123"));
            Assert.That(outcome.StdoutTruncated, Is.True);
        });
    }

    [Test]
    public void TheCaptureFolderIsLeftEmptyTest()
    {
        var capture = Path.Combine(m_root, "capture-clean");
        Run(WasiModules.Echo(), capture: capture, stdin: "abc\n");
        Run(WasiModules.OutOfBounds(), capture: capture);
        Run(WasiModules.UnknownImport("x"), capture: capture);

        Assert.That(Directory.GetFileSystemEntries(capture), Is.Empty, "every run deletes its stdin/stdout/stderr files");
    }

    #endregion

    #region Folder Tests

    [Test]
    public void AGuestReadsTheInFolderAndWritesTheOutFolderTest()
    {
        var (input, output) = Folders("in-out");
        File.WriteAllText(Path.Combine(input, WasmHostModules.INPUT_NAME), "page one\fpage two\n");

        var outcome = Run(WasmHostModules.Copy(), preopens: [new WasmHostPreopen(input, "/in"), new WasmHostPreopen(output, "/out", Writable: true)]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited), outcome.Stderr);
            Assert.That(outcome.ExitCode, Is.EqualTo(0));
            Assert.That(File.ReadAllText(Path.Combine(output, WasmHostModules.OUTPUT_NAME)), Is.EqualTo("page one\fpage two\n"));
        });
    }

    [Test]
    public void AReadOnlyFolderRefusesTheGuestsWritesTest()
    {
        var (input, output) = Folders("read-only");
        File.WriteAllText(Path.Combine(input, WasmHostModules.INPUT_NAME), "text");

        var outcome = Run(WasmHostModules.Copy(), preopens: [new WasmHostPreopen(input, "/in"), new WasmHostPreopen(output, "/out")]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.Verdict, Is.EqualTo(WasmHostVerdict.Exited));
            Assert.That(outcome.ExitCode, Is.EqualTo(WasmHostModules.OUTPUT_FAILED), "the output could not be opened");
            Assert.That(Directory.GetFileSystemEntries(output), Is.Empty);
        });
    }

    [Test]
    public void TheGuestSeesNothingButItsFoldersTest()
    {
        // The input lies next to the folders, not in them: the guest cannot reach it.
        var (input, output) = Folders("contained");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(input)!, WasmHostModules.INPUT_NAME), "outside");

        var outcome = Run(WasmHostModules.Copy(), preopens: [new WasmHostPreopen(input, "/in"), new WasmHostPreopen(output, "/out", Writable: true)]);

        Assert.Multiple(() =>
        {
            Assert.That(outcome.ExitCode, Is.EqualTo(WasmHostModules.INPUT_FAILED), "the input does not exist for the guest");
            Assert.That(Directory.GetFileSystemEntries(output), Is.Empty);
        });
    }

    [Test]
    public void AFolderThatIsNotThereIsAHostFaultNotAVerdictTest()
    {
        // The host's own folders are the host's business: a run that cannot be set up throws,
        // so a caller tells a broken node from a guest that failed.
        var missing = Path.Combine(m_root, "not-there");

        var exception = Assert.Throws<InvalidOperationException>(() => Run(WasiModules.Hello("x"), preopens: [new WasmHostPreopen(missing, "/in")]));

        Assert.That(exception!.Message, Does.Contain("preopen").And.Contain(missing));
    }

    #endregion

    #region Tools

    private WasmHostRunOutcome Run(byte[] bytes, long fuel = FUEL, long memoryBytes = MEMORY_BYTES, int wallMs = WALL_MS,
        int stdoutLimitBytes = 0, string? stdin = null, string? capture = null, IReadOnlyList<WasmHostPreopen>? preopens = null,
        Func<string, bool>? outOfMemory = null)
    {
        using var module = Wasmtime.Module.FromBytes(m_host.Engine, "main.wasm", bytes);
        return m_host.Run(module, new WasmHostRunRequest
        {
            Argv = ["main.wasm"],
            Preopens = preopens ?? [],
            Stdin = stdin,
            CaptureDirectory = capture ?? Path.Combine(m_root, "capture"),
            Fuel = fuel,
            MemoryBytes = memoryBytes,
            WallMs = wallMs,
            StdoutLimitBytes = stdoutLimitBytes,
            OutOfMemory = outOfMemory
        });
    }

    private (string Input, string Output) Folders(string name)
    {
        var root = Path.Combine(m_root, name);
        var input = Path.Combine(root, "in");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        return (input, output);
    }

    #endregion
}
