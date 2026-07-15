using System.Diagnostics;
using Wasmtime;
using WasmEngine = Wasmtime.Engine;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// Wasmtime sandbox spike: proves the facts the Verify controller is built on —
/// WASI runtimes execute, fuel/memory/epoch limits interrupt hostile programs, and
/// output (and fuel accounting) is byte-deterministic across runs and across OSes
/// (committed golden files; produced on win-x64, re-checked wherever the suite runs).
///
/// Opt-in, like every prerequisite-bound suite in this repo: every test Assert.Ignores unless the pinned runtime modules are
/// present under @Data/runtimes/ — fetch them with download-spike-runtimes.ps1.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class WasmSandboxSpikeTests
{
    #region Constants

    private const string PYTHON_DETERMINISM_PROGRAM = """
        import hashlib, math, random
        random.seed(42)
        vals = [random.random() for _ in range(1000)]
        acc = 0.0
        for i in range(1, 20000):
            acc += math.sin(i) * math.sqrt(i) / (0.1 + i)
        s = repr(acc) + '|' + repr(0.1 + 0.2) + '|' + repr(sum(vals))
        s += '|' + hashlib.sha256(s.encode()).hexdigest()
        print(s)
        d = {'z': 1, 'a': 2, 'm': 3}
        print(list(d.items()))
        print(hash('determinism-check'))
        """;

    private const string QJS_DETERMINISM_PROGRAM = """
        let acc = 0.0;
        for (let i = 1; i < 20000; i++) acc += Math.sin(i) * Math.sqrt(i) / (0.1 + i);
        console.log(acc.toPrecision(17), (0.1+0.2).toPrecision(17), JSON.stringify({z:1,a:2,m:3}));
        """;

    #endregion

    #region Fields

    private string m_runtimesDir = null!;
    private string m_pythonDir = null!;
    private string m_ioDir = null!;

    private WasmEngine m_plainEngine = null!;
    private WasmEngine m_fuelEngine = null!;
    private WasmEngine m_epochEngine = null!;

    private Lazy<WasmModule> m_qjsPlain = null!;
    private Lazy<WasmModule> m_qjsFuel = null!;
    private Lazy<WasmModule> m_pythonPlain = null!;
    private Lazy<WasmModule> m_pythonFuel = null!;
    private Lazy<WasmModule> m_pythonEpoch = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var projectRoot = FindTestProjectRoot();
        if (projectRoot == null)
            Assert.Ignore("Test project root not found");

        m_runtimesDir = Path.Combine(projectRoot, "@Data", "runtimes");
        m_pythonDir = Path.Combine(m_runtimesDir, "python-3.14.6");

        if (!File.Exists(Path.Combine(m_runtimesDir, "qjs-wasi.wasm")) ||
            !File.Exists(Path.Combine(m_pythonDir, "python.wasm")))
        {
            Assert.Ignore(
                $"WASM runtime modules not found under '{m_runtimesDir}' — " +
                "run download-spike-runtimes.ps1 to opt in to the sandbox spike tests.");
        }

        m_ioDir = Path.Combine(Path.GetTempPath(), $"witai_wasm_spike_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_ioDir);

        // NaN canonicalization keeps float bit patterns identical across platforms —
        // part of the determinism claim, so it is on for every engine flavor.
        m_plainEngine = new WasmEngine(new Config().WithCraneliftNaNCanonicalization(true));
        m_fuelEngine = new WasmEngine(new Config().WithFuelConsumption(true).WithCraneliftNaNCanonicalization(true));
        m_epochEngine = new WasmEngine(new Config().WithEpochInterruption(true).WithCraneliftNaNCanonicalization(true));

        var qjsPath = Path.Combine(m_runtimesDir, "qjs-wasi.wasm");
        var pythonPath = Path.Combine(m_pythonDir, "python.wasm");

        m_qjsPlain = new Lazy<WasmModule>(() => WasmModule.FromFile(m_plainEngine, qjsPath));
        m_qjsFuel = new Lazy<WasmModule>(() => WasmModule.FromFile(m_fuelEngine, qjsPath));
        m_pythonPlain = new Lazy<WasmModule>(() => WasmModule.FromFile(m_plainEngine, pythonPath));
        m_pythonFuel = new Lazy<WasmModule>(() => WasmModule.FromFile(m_fuelEngine, pythonPath));
        m_pythonEpoch = new Lazy<WasmModule>(() => WasmModule.FromFile(m_epochEngine, pythonPath));
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        foreach (var module in new[] { m_qjsPlain, m_qjsFuel, m_pythonPlain, m_pythonFuel, m_pythonEpoch })
            if (module is { IsValueCreated: true })
                module.Value.Dispose();

        m_plainEngine?.Dispose();
        m_fuelEngine?.Dispose();
        m_epochEngine?.Dispose();

        if (m_ioDir != null && Directory.Exists(m_ioDir))
            Directory.Delete(m_ioDir, recursive: true);
    }

    #endregion

    #region Hello World Tests

    [Test]
    public void QuickJsExecutesHelloWorldTest()
    {
        var result = RunJs(m_plainEngine, m_qjsPlain.Value, "console.log('hello from quickjs', 6*7);");

        Assert.That(result.Trap, Is.Null);
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Stdout.Trim(), Is.EqualTo("hello from quickjs 42"));
    }

    [Test]
    public void PythonExecutesHelloWorldTest()
    {
        var result = RunPython(m_plainEngine, m_pythonPlain.Value, "import sys; print('hello from', sys.version_info[:3])");

        Assert.That(result.Trap, Is.Null);
        Assert.That(result.ExitCode, Is.Zero);
        Assert.That(result.Stdout.Trim(), Is.EqualTo("hello from (3, 14, 6)"));
    }

    #endregion

    #region Limit Tests

    [Test]
    public void FuelLimitInterruptsInfiniteLoopTest()
    {
        var stopwatch = Stopwatch.StartNew();
        var result = RunJs(m_fuelEngine, m_qjsFuel.Value, "for(;;){}", fuel: 500_000_000);
        stopwatch.Stop();

        Assert.That(result.Trap, Is.EqualTo("OutOfFuel"));
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void MemoryLimitSurfacesAsMemoryErrorVerdictTest()
    {
        // The store cap turns a hostile allocation into an ordinary in-guest MemoryError:
        // the interpreter exits cleanly with a nonzero code instead of trapping the host.
        var bomb = RunPython(m_plainEngine, m_pythonPlain.Value,
            "b = bytearray(512*1024*1024); print('allocated', len(b))",
            memoryLimit: 256L * 1024 * 1024);

        Assert.That(bomb.Trap, Is.Null);
        Assert.That(bomb.ExitCode, Is.EqualTo(1));
        Assert.That(bomb.Stderr, Does.Contain("MemoryError"));

        var withinLimit = RunPython(m_plainEngine, m_pythonPlain.Value,
            "b = bytearray(64*1024*1024); print('allocated', len(b))",
            memoryLimit: 256L * 1024 * 1024);

        Assert.That(withinLimit.ExitCode, Is.Zero);
        Assert.That(withinLimit.Stdout.Trim(), Is.EqualTo("allocated 67108864"));
    }

    [Test]
    public void EpochDeadlineInterruptsInfiniteLoopTest()
    {
        using var timer = new Timer(_ => m_epochEngine.IncrementEpoch(), null, dueTime: 10, period: 10);

        var stopwatch = Stopwatch.StartNew();
        var result = RunPython(m_epochEngine, m_pythonEpoch.Value, "while True: pass", epochDeadline: 100);
        stopwatch.Stop();

        Assert.That(result.Trap, Is.EqualTo("Interrupt"));
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)));
    }

    #endregion

    #region Sandbox Containment Tests

    [Test]
    public void SandboxDeniesSocketsTest()
    {
        var result = RunPython(m_plainEngine, m_pythonPlain.Value, "import socket; s = socket.socket()");

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Stderr, Does.Contain("OSError"));
    }

    [Test]
    public void SandboxSeesNoHostFilesystemTest()
    {
        var result = RunPython(m_plainEngine, m_pythonPlain.Value, "print(open('/etc/passwd').read())");

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Stderr, Does.Contain("FileNotFoundError"));
    }

    [Test]
    public void SandboxDeniesWritesToReadOnlyPreopenTest()
    {
        var result = RunPython(m_plainEngine, m_pythonPlain.Value, "open('/lib/evil.txt', 'w').write('x')");

        Assert.That(result.ExitCode, Is.Not.Zero);
        Assert.That(result.Stderr, Does.Contain("PermissionError"));
    }

    #endregion

    #region Determinism Tests

    [Test]
    public void PythonOutputMatchesCommittedGoldenTest()
    {
        var runs = Enumerable.Range(0, 3)
            .Select(_ => RunPython(m_plainEngine, m_pythonPlain.Value, PYTHON_DETERMINISM_PROGRAM).Stdout)
            .ToList();

        Assert.That(runs.Distinct().Count(), Is.EqualTo(1), "repeat runs diverged");
        Assert.That(runs[0], Is.EqualTo(ReadGolden("python-det.golden.txt")),
            "output differs from the committed golden (produced on win-x64) — cross-OS byte-equality is broken");
    }

    [Test]
    public void QuickJsOutputMatchesCommittedGoldenTest()
    {
        var runs = Enumerable.Range(0, 3)
            .Select(_ => RunJs(m_plainEngine, m_qjsPlain.Value, QJS_DETERMINISM_PROGRAM).Stdout)
            .ToList();

        Assert.That(runs.Distinct().Count(), Is.EqualTo(1), "repeat runs diverged");
        Assert.That(runs[0], Is.EqualTo(ReadGolden("qjs-det.golden.txt")),
            "output differs from the committed golden (produced on win-x64) — cross-OS byte-equality is broken");
    }

    [Test]
    public void FuelConsumptionIsStableAcrossRunsTest()
    {
        // Fuel counts executed instructions. It is NOT byte-exact across identical runs:
        // CPython startup reads the WASI clock, and instruction counts downstream of the
        // timestamp differ by a handful of instructions (~50 out of 363M observed on
        // linux-x64; win-x64's coarser clock usually collides to equality). Fuel is a
        // limit/budget mechanism — integrity byte-comparison uses outputs, never fuel.
        // Pinning clock_time_get (planned import-control work) should restore exactness.
        var first = RunPython(m_fuelEngine, m_pythonFuel.Value, "print(sum(i*i for i in range(100000)))", fuel: 50_000_000_000);
        var second = RunPython(m_fuelEngine, m_pythonFuel.Value, "print(sum(i*i for i in range(100000)))", fuel: 50_000_000_000);

        Assert.That(first.ExitCode, Is.Zero);
        Assert.That(second.ExitCode, Is.Zero);
        Assert.That(first.FuelConsumed, Is.Not.Null);
        Assert.That((double)second.FuelConsumed!,
            Is.EqualTo((double)first.FuelConsumed!).Within(0.01).Percent);
    }

    [Test]
    public void ParallelExecutionsProduceIdenticalResultsTest()
    {
        // Concurrent stores over one compiled module must not interfere — batch execution
        // will run tasks in parallel across cores, so cross-store isolation is load-bearing.
        var degree = Math.Min(8, Environment.ProcessorCount);
        var outputs = new string[degree];

        Parallel.For(0, degree, new ParallelOptions { MaxDegreeOfParallelism = degree }, i =>
        {
            var result = RunPython(m_plainEngine, m_pythonPlain.Value, PYTHON_DETERMINISM_PROGRAM);
            outputs[i] = result.ExitCode == 0 ? result.Stdout : $"FAILED: trap={result.Trap} stderr={result.Stderr}";
        });

        Assert.That(outputs.Distinct().Count(), Is.EqualTo(1),
            $"parallel runs diverged: {string.Join(" | ", outputs.Distinct().Take(3))}");
        Assert.That(outputs[0], Does.Not.StartWith("FAILED"));
    }

    #endregion

    #region Functions

    private static string? FindTestProjectRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "OutWit.Controller.AI.Verify.Tests.csproj")))
            directory = directory.Parent;

        return directory?.FullName;
    }

    private static string ReadGolden(string fileName)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Sandbox", "Golden", fileName);
        // goldens are byte-exact modulo git's line-ending normalization on checkout
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    private WasmRunResult RunPython(WasmEngine engine, WasmModule module, string code,
        ulong? fuel = null, long? memoryLimit = null, ulong? epochDeadline = null)
    {
        return Run(engine, module, ["python.wasm", "-B", "-c", code],
            preopens: [(m_pythonDir, "/")],
            environment: [("PYTHONHASHSEED", "0"), ("PYTHONDONTWRITEBYTECODE", "1")],
            fuel: fuel, memoryLimit: memoryLimit, epochDeadline: epochDeadline);
    }

    private WasmRunResult RunJs(WasmEngine engine, WasmModule module, string code,
        ulong? fuel = null, long? memoryLimit = null, ulong? epochDeadline = null)
    {
        return Run(engine, module, ["qjs", "-e", code],
            preopens: null, environment: null,
            fuel: fuel, memoryLimit: memoryLimit, epochDeadline: epochDeadline);
    }

    /// <summary>
    /// One sandboxed WASI execution: fresh store (no state bleed), stdio redirected to
    /// files (wasmtime holds the handles until the store is disposed), optional
    /// fuel / memory / epoch envelope — the shape the ExecuteBatch activity productizes.
    /// </summary>
    private WasmRunResult Run(WasmEngine engine, WasmModule module, string[] argv,
        (string Host, string Guest)[]? preopens, (string Key, string Value)[]? environment,
        ulong? fuel, long? memoryLimit, ulong? epochDeadline)
    {
        var store = new Store(engine);

        var wasi = new WasiConfiguration().WithArgs(argv);

        foreach (var (key, value) in environment ?? [])
            wasi = wasi.WithEnvironmentVariable(key, value);

        foreach (var (host, guest) in preopens ?? [])
            wasi = wasi.WithPreopenedDirectory(host, guest,
                WasiDirectoryPermissions.Read, WasiFilePermissions.Read);

        var stdoutPath = Path.Combine(m_ioDir, $"{Guid.NewGuid():N}.out");
        var stderrPath = stdoutPath + ".err";
        wasi = wasi.WithStandardOutput(stdoutPath).WithStandardError(stderrPath);

        store.SetWasiConfiguration(wasi);

        if (fuel.HasValue)
            store.Fuel = fuel.Value;
        if (memoryLimit.HasValue)
            store.SetLimits(memoryLimit.Value, null, null, null, null);
        if (epochDeadline.HasValue)
            store.SetEpochDeadline(epochDeadline.Value);

        using var linker = new Linker(engine);
        linker.DefineWasi();

        var stopwatch = Stopwatch.StartNew();
        var exitCode = 0;
        string? trap = null;
        try
        {
            var instance = linker.Instantiate(store, module);
            instance.GetAction("_start")!();
        }
        catch (TrapException exception)
        {
            trap = exception.Type.ToString();
            exitCode = -1;
        }
        catch (WasmtimeException exception) when (exception.ExitCode.HasValue)
        {
            exitCode = exception.ExitCode.Value;
        }
        stopwatch.Stop();

        ulong? fuelConsumed = null;
        if (fuel.HasValue && trap == null)
            fuelConsumed = fuel.Value - store.Fuel;

        store.Dispose();

        var stdout = File.Exists(stdoutPath) ? File.ReadAllText(stdoutPath) : "";
        var stderr = File.Exists(stderrPath) ? File.ReadAllText(stderrPath) : "";
        File.Delete(stdoutPath);
        File.Delete(stderrPath);

        return new WasmRunResult(exitCode, trap, stdout, stderr, stopwatch.Elapsed, fuelConsumed);
    }

    #endregion

    #region Nested Types

    private sealed record WasmRunResult(int ExitCode, string? Trap, string Stdout, string Stderr, TimeSpan Elapsed, ulong? FuelConsumed);

    #endregion
}
