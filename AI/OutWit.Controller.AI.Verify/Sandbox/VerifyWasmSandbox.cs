using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using Wasmtime;
using WasmEngine = Wasmtime.Engine;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// The execution core: runs one task under a pinned WASI runtime in a fresh, isolated
/// store within a fuel / memory / wall-clock envelope, and maps the outcome to a verdict.
/// One sandbox instance owns one wasmtime engine and compiles each runtime module once
/// (the persistent-batch economy); tasks are otherwise independent, so callers run many
/// <see cref="Execute"/> calls concurrently across a node's cores.
///
/// WASI clock and randomness are shadowed with deterministic implementations: the same
/// task yields byte-identical output (and, with the clock pinned, byte-identical fuel)
/// on any node — the integrity foundation the controller compares against.
/// </summary>
public sealed class VerifyWasmSandbox : IDisposable
{
    #region Constants

    /// <summary>Epoch tick granularity — the wall-clock interrupt resolution.</summary>
    private const int EPOCH_TICK_MS = 10;

    /// <summary>Fixed value returned by the pinned clock (nanoseconds); constant → deterministic.</summary>
    private const long PINNED_CLOCK_NS = 0;

    private const string WASI_MODULE = "wasi_snapshot_preview1";
    private const string GUEST_MEMORY = "memory";
    private const int ERRNO_SUCCESS = 0;

    #endregion

    #region Fields

    private readonly WasmEngine m_engine;
    private readonly Timer m_epochTimer;
    private readonly string m_ioDir;
    private readonly object m_modulesLock = new();
    private readonly Dictionary<string, WasmModule> m_modules = new();
    private bool m_disposed;

    #endregion

    #region Constructors

    public VerifyWasmSandbox()
    {
        // NaN canonicalization pins float bit patterns across platforms; fuel and epoch
        // give the CPU and wall-clock envelopes. One engine is shared by every store.
        m_engine = new WasmEngine(new Config()
            .WithFuelConsumption(true)
            .WithEpochInterruption(true)
            .WithCraneliftNaNCanonicalization(true));

        // One timer thread advances the epoch for every store; per-store deadlines
        // (SetEpochDeadline) convert a wall-time budget into a tick count.
        m_epochTimer = new Timer(_ => m_engine.IncrementEpoch(), null, EPOCH_TICK_MS, EPOCH_TICK_MS);

        m_ioDir = Path.Combine(Path.GetTempPath(), $"witai_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_ioDir);
    }

    #endregion

    #region Execution

    /// <summary>
    /// Executes one task: its suite case-by-case if present, otherwise a single run.
    /// Never throws for guest-side faults — every failure mode is a verdict.
    /// </summary>
    public VerifyResultData Execute(VerifyResolvedRuntime runtime, VerifyTaskData task, VerifyLimitsData limits)
    {
        WasmModule module;
        try
        {
            module = GetOrCompile(runtime);
        }
        catch (WasmtimeException exception)
        {
            return Unavailable(task.TaskIndex, $"runtime module failed to compile: {exception.Message}");
        }

        return task.Suite is { Cases.Count: > 0 }
            ? ExecuteSuite(runtime, module, task, limits)
            : ExecuteSingle(runtime, module, task, limits);
    }

    private VerifyResultData ExecuteSingle(VerifyResolvedRuntime runtime, WasmModule module, VerifyTaskData task, VerifyLimitsData limits)
    {
        var run = RunProgram(runtime, module, task, task.Args, task.Stdin, limits);

        return new VerifyResultData
        {
            TaskIndex = task.TaskIndex,
            Verdict = run.Verdict,
            ExitCode = run.ExitCode,
            Stdout = run.Stdout,
            StdoutTruncated = run.StdoutTruncated,
            Stderr = run.Stderr,
            StderrTruncated = run.StderrTruncated,
            FuelConsumed = run.FuelConsumed,
            PeakMemoryBytes = run.PeakMemoryBytes,
            WallMs = run.WallMs
        };
    }

    private VerifyResultData ExecuteSuite(VerifyResolvedRuntime runtime, WasmModule module, VerifyTaskData task, VerifyLimitsData limits)
    {
        var suite = task.Suite!;
        var caseResults = new List<VerifyCaseResultData>(suite.Cases.Count);

        long totalFuel = 0;
        long peakMemory = 0;
        var totalWall = 0;
        var allPassed = true;
        var firstFailure = VerifyVerdict.Pass;
        var firstFailureExit = 0;
        var lastStdout = "";
        var lastStdoutTruncated = false;
        var lastStderr = "";
        var lastStderrTruncated = false;

        for (var i = 0; i < suite.Cases.Count; i++)
        {
            var testCase = suite.Cases[i];
            var args = task.Args.Concat(testCase.Args).ToList();
            var run = RunProgram(runtime, module, task, args, testCase.Stdin, limits);

            totalFuel += run.FuelConsumed;
            peakMemory = Math.Max(peakMemory, run.PeakMemoryBytes);
            totalWall += run.WallMs;
            lastStdout = run.Stdout;
            lastStdoutTruncated = run.StdoutTruncated;
            lastStderr = run.Stderr;
            lastStderrTruncated = run.StderrTruncated;

            var passed = run.Verdict == VerifyVerdict.Pass
                         && run.ExitCode == testCase.ExpectedExitCode
                         && (testCase.ExpectedStdout == null || run.Stdout == testCase.ExpectedStdout);

            caseResults.Add(new VerifyCaseResultData
            {
                CaseIndex = i,
                Passed = passed,
                Verdict = run.Verdict,
                ExitCode = run.ExitCode,
                ActualStdout = run.Stdout
            });

            if (!passed && allPassed)
            {
                allPassed = false;
                // A resource verdict (Timeout/Memory/Output) is reported as-is; a clean
                // run that simply mismatched expectations is a Fail.
                firstFailure = run.Verdict == VerifyVerdict.Pass ? VerifyVerdict.Fail : run.Verdict;
                firstFailureExit = run.ExitCode;
            }
        }

        return new VerifyResultData
        {
            TaskIndex = task.TaskIndex,
            Verdict = allPassed ? VerifyVerdict.Pass : firstFailure,
            ExitCode = allPassed ? 0 : firstFailureExit,
            Stdout = lastStdout,
            StdoutTruncated = lastStdoutTruncated,
            Stderr = lastStderr,
            StderrTruncated = lastStderrTruncated,
            CaseResults = caseResults,
            FuelConsumed = totalFuel,
            PeakMemoryBytes = peakMemory,
            WallMs = totalWall
        };
    }

    /// <summary>One guest invocation in its own store. The heart of the sandbox.</summary>
    private RunOutcome RunProgram(VerifyResolvedRuntime runtime, WasmModule module, VerifyTaskData task,
        IReadOnlyList<string> args, string? stdin, VerifyLimitsData limits)
    {
        var stdoutPath = Path.Combine(m_ioDir, $"{Guid.NewGuid():N}.out");
        var stderrPath = stdoutPath + ".err";
        var stdinPath = stdin == null ? null : stdoutPath + ".in";
        if (stdinPath != null)
            File.WriteAllText(stdinPath, stdin);

        var store = new Store(m_engine);
        var storeDisposed = false;
        try
        {
            store.SetWasiConfiguration(BuildWasi(runtime, task, args, stdinPath, stdoutPath, stderrPath));
            store.Fuel = (ulong)Math.Max(1, limits.FuelBudget);
            store.SetLimits(memorySize: Math.Max(1, limits.MemoryBytes));
            store.SetEpochDeadline(WallToTicks(limits.WallTimeMs));

            using var linker = BuildLinker(task.RandomSeed);

            var start = DateTime.UtcNow;
            var (verdict, exitCode, trapMemoryFault, peakMemory) = Invoke(linker, store, module);
            var wallMs = (int)(DateTime.UtcNow - start).TotalMilliseconds;

            var fuelConsumed = trapMemoryFault ? limits.FuelBudget : (long)((ulong)limits.FuelBudget - store.Fuel);

            // Dispose the store BEFORE reading — wasmtime holds OS handles on the stdio
            // redirect files until then.
            store.Dispose();
            storeDisposed = true;

            var (stdout, stdoutTruncated) = ReadCapped(stdoutPath, limits.StdoutLimitBytes);
            var (stderr, stderrTruncated) = ReadCapped(stderrPath, limits.StderrLimitBytes);

            // An allocation past the store cap usually surfaces as a graceful in-guest OOM
            // (a clean nonzero exit), not a hard trap. Reclassify it to MemoryExceeded so the
            // verdict stays useful — best-effort, by the runtime's OOM marker.
            if (verdict is VerifyVerdict.RuntimeError && LooksLikeOutOfMemory(stderr))
                verdict = VerifyVerdict.MemoryExceeded;

            // A program that floods stdout violates the small-output contract regardless
            // of whether its bytes matched — surface it distinctly.
            if (stdoutTruncated && verdict is VerifyVerdict.Pass)
                verdict = VerifyVerdict.OutputExceeded;

            return new RunOutcome(verdict, exitCode, stdout, stdoutTruncated, stderr, stderrTruncated,
                fuelConsumed, peakMemory, wallMs);
        }
        finally
        {
            if (!storeDisposed)
                store.Dispose(); // releases wasmtime's handles on the stdio files
            TryDelete(stdoutPath);
            TryDelete(stderrPath);
            if (stdinPath != null)
                TryDelete(stdinPath);
        }
    }

    private static (VerifyVerdict Verdict, int ExitCode, bool MemoryFault, long PeakMemory) Invoke(Linker linker, Store store, WasmModule module)
    {
        Instance instance;
        try
        {
            instance = linker.Instantiate(store, module);
        }
        catch (WasmtimeException exception) when (exception.ExitCode is { } code)
        {
            // A guest that proc_exits during module init (before _start) still lands here.
            return code == 0 ? (VerifyVerdict.Pass, 0, false, 0) : (VerifyVerdict.RuntimeError, code, false, 0);
        }
        catch (TrapException trap)
        {
            return MapTrap(trap);
        }

        try
        {
            var startAction = instance.GetAction("_start");
            if (startAction == null)
                return (VerifyVerdict.RuntimeError, -1, false, PeakMemory(instance));

            startAction();
            return (VerifyVerdict.Pass, 0, false, PeakMemory(instance));
        }
        catch (WasmtimeException exception) when (exception.ExitCode is { } code)
        {
            // WASI proc_exit — a clean guest exit with a code (0 = success).
            var peak = PeakMemory(instance);
            return code == 0 ? (VerifyVerdict.Pass, 0, false, peak) : (VerifyVerdict.RuntimeError, code, false, peak);
        }
        catch (TrapException trap)
        {
            return MapTrap(trap);
        }
    }

    private static (VerifyVerdict Verdict, int ExitCode, bool MemoryFault, long PeakMemory) MapTrap(TrapException trap)
    {
        var verdict = trap.Type switch
        {
            // Both fuel exhaustion and epoch interruption mean "did not finish in budget".
            TrapCode.OutOfFuel => VerifyVerdict.Timeout,
            TrapCode.Interrupt => VerifyVerdict.Timeout,
            // A hard out-of-bounds under the memory cap; in-guest OOM usually exits cleanly instead.
            TrapCode.MemoryOutOfBounds => VerifyVerdict.MemoryExceeded,
            _ => VerifyVerdict.RuntimeError
        };
        return (verdict, -1, true, 0);
    }

    private static long PeakMemory(Instance instance)
    {
        // WASM linear memory only grows, so its current length is the peak.
        try
        {
            return instance.GetMemory(GUEST_MEMORY)?.GetLength() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    #region Sandbox Wiring

    private WasiConfiguration BuildWasi(VerifyResolvedRuntime runtime, VerifyTaskData task,
        IReadOnlyList<string> args, string? stdinPath, string stdoutPath, string stderrPath)
    {
        var argv = BuildArgv(runtime, task, args);
        var wasi = new WasiConfiguration().WithArgs(argv);

        // Pin the interpreter's environment for reproducibility (hash seed off, no bytecode writes).
        if (runtime.Descriptor.Language == VerifyRuntimeLanguage.Python)
        {
            wasi = wasi
                .WithEnvironmentVariable("PYTHONHASHSEED", "0")
                .WithEnvironmentVariable("PYTHONDONTWRITEBYTECODE", "1")
                .WithEnvironmentVariable("PYTHONUNBUFFERED", "1");
        }

        // Stdlib preopen, read-only. The task's own sources travel inline via -c/-e, so
        // v1 needs no writable guest FS — the strongest containment posture.
        if (runtime.RootPath != null)
            wasi = wasi.WithPreopenedDirectory(runtime.RootPath, "/", WasiDirectoryPermissions.Read, WasiFilePermissions.Read);

        if (stdinPath != null)
            wasi = wasi.WithStandardInput(stdinPath);

        return wasi.WithStandardOutput(stdoutPath).WithStandardError(stderrPath);
    }

    private static string[] BuildArgv(VerifyResolvedRuntime runtime, VerifyTaskData task, IReadOnlyList<string> args)
    {
        var entry = task.Sources.FirstOrDefault(s => s.Name == task.EntryPoint)
                    ?? task.Sources.FirstOrDefault();
        var code = entry?.Content ?? "";

        // v1 executes the entry source inline (no writable guest FS). Multi-file programs
        // are a planned extension (a writable preopen + materialized sources).
        var argv = new List<string> { runtime.Descriptor.Argv0 };
        switch (runtime.Descriptor.Language)
        {
            case VerifyRuntimeLanguage.Python:
                argv.Add("-B");
                argv.Add("-c");
                argv.Add(code);
                break;
            case VerifyRuntimeLanguage.JavaScript:
                argv.Add("-e");
                argv.Add(code);
                break;
        }

        argv.AddRange(args);
        return argv.ToArray();
    }

    /// <summary>
    /// A WASI linker whose clock and randomness are replaced with deterministic
    /// implementations, so a task's execution — output and fuel — is reproducible.
    /// </summary>
    private Linker BuildLinker(long randomSeed)
    {
        var linker = new Linker(m_engine) { AllowShadowing = true };
        linker.DefineWasi();

        // Fixed clock: every clock_time_get returns the same instant. Removes the last
        // source of fuel non-determinism found in the runtime spike.
        linker.DefineFunction(WASI_MODULE, "clock_time_get",
            (Caller caller, int clockId, long precision, int timePtr) =>
            {
                caller.GetMemory(GUEST_MEMORY)?.WriteInt64(timePtr, PINNED_CLOCK_NS);
                return ERRNO_SUCCESS;
            });

        // Seeded PRNG: deterministic bytes from the task seed (xorshift64*), so any
        // randomness the guest draws is identical on every node.
        var state = new RandomState(unchecked((ulong)randomSeed) | 1UL);
        linker.DefineFunction(WASI_MODULE, "random_get",
            (Caller caller, int bufferPtr, int length) =>
            {
                var memory = caller.GetMemory(GUEST_MEMORY);
                if (memory == null)
                    return ERRNO_SUCCESS;

                var span = memory.GetSpan(bufferPtr, length);
                for (var i = 0; i < length; i++)
                    span[i] = state.NextByte();

                return ERRNO_SUCCESS;
            });

        return linker;
    }

    #endregion

    #region Helpers

    private WasmModule GetOrCompile(VerifyResolvedRuntime runtime)
    {
        lock (m_modulesLock)
        {
            if (m_modules.TryGetValue(runtime.Descriptor.Id, out var cached))
                return cached;

            var module = WasmModule.FromFile(m_engine, runtime.ModulePath);
            m_modules[runtime.Descriptor.Id] = module;
            return module;
        }
    }

    private static ulong WallToTicks(int wallMs)
    {
        var ticks = (wallMs + EPOCH_TICK_MS - 1) / EPOCH_TICK_MS;
        return (ulong)Math.Max(1, ticks);
    }

    private static bool LooksLikeOutOfMemory(string stderr)
    {
        // Python raises MemoryError; QuickJS-NG prints "out of memory" on allocation failure.
        return stderr.Contains("MemoryError", StringComparison.Ordinal)
               || stderr.Contains("out of memory", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Text, bool Truncated) ReadCapped(string path, int capBytes)
    {
        if (!File.Exists(path))
            return ("", false);

        var bytes = File.ReadAllBytes(path);
        if (capBytes > 0 && bytes.Length > capBytes)
            return (System.Text.Encoding.UTF8.GetString(bytes, 0, capBytes), true);

        return (System.Text.Encoding.UTF8.GetString(bytes), false);
    }

    private static VerifyResultData Unavailable(int taskIndex, string reason)
    {
        return new VerifyResultData
        {
            TaskIndex = taskIndex,
            Verdict = VerifyVerdict.RuntimeUnavailable,
            ExitCode = -1,
            Stderr = reason
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // temp file; best-effort
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (m_disposed)
            return;

        m_disposed = true;
        m_epochTimer.Dispose();

        lock (m_modulesLock)
        {
            foreach (var module in m_modules.Values)
                module.Dispose();
            m_modules.Clear();
        }

        m_engine.Dispose();

        try
        {
            if (Directory.Exists(m_ioDir))
                Directory.Delete(m_ioDir, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    #endregion

    #region Nested Types

    private sealed record RunOutcome(
        VerifyVerdict Verdict, int ExitCode,
        string Stdout, bool StdoutTruncated, string Stderr, bool StderrTruncated,
        long FuelConsumed, long PeakMemoryBytes, int WallMs);

    private sealed class RandomState(ulong seed)
    {
        private ulong m_state = seed;

        public byte NextByte()
        {
            // xorshift64* — deterministic, seed-driven; quality is irrelevant, reproducibility is the point.
            m_state ^= m_state >> 12;
            m_state ^= m_state << 25;
            m_state ^= m_state >> 27;
            return (byte)((m_state * 0x2545F4914F6CDD1DUL) >> 56);
        }
    }

    #endregion
}
