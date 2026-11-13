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
/// The guest's world is <see cref="VerifyGuestWiring"/> (argv, environment, read-only root,
/// stdio), its clock and randomness <see cref="VerifyDeterministicLinker"/>'s, a suite's
/// verdict <see cref="VerifySuiteEvaluator"/>'s.
/// </summary>
public sealed class VerifyWasmSandbox : IDisposable
{
    #region Constants

    /// <summary>Epoch tick granularity — the wall-clock interrupt resolution.</summary>
    private const int EPOCH_TICK_MS = 10;

    private const string GUEST_MEMORY = "memory";
    private const string START_EXPORT = "_start";

    /// <summary>
    /// The most elements one table of a guest may hold. A table is host memory the linear
    /// memory cap does not count (eight bytes an element or more), so it has a cap of its
    /// own: over a hundred times the largest table of the bundled runtimes.
    /// </summary>
    public const uint MAX_TABLE_ELEMENTS = 1_048_576;

    /// <summary>Tables one guest may declare (a runtime module declares one).</summary>
    internal const long MAX_TABLES = 8;

    /// <summary>One linear memory per guest: the memory cap is per memory, a second one would double it.</summary>
    internal const long MAX_MEMORIES = 1;

    /// <summary>Instances one store may hold: the guest's own, with room for none of its making.</summary>
    internal const long MAX_INSTANCES = 4;

    #endregion

    #region Fields

    private readonly WasmEngine m_engine;
    private readonly Timer m_epochTimer;
    private readonly string m_ioDir;
    private readonly object m_modulesLock = new();
    private readonly Dictionary<string, WasmModule> m_modules = new();
    private readonly VerifyTaskModuleCache m_taskModules;
    private readonly VerifyToolRunner m_tools;
    private bool m_disposed;

    #endregion

    #region Constructors

    static VerifyWasmSandbox()
    {
        // Before the first wasmtime P/Invoke: in the shared load context the cloud client uses,
        // the native library is found only through the module-aware resolver.
        VerifyNativeLibrary.EnsureRegistered();
    }

    /// <summary>
    /// Creates a sandbox whose working folder is a new one in the operating system's temp
    /// path: for tests and for a host without a temp storage of its own.
    /// </summary>
    public VerifyWasmSandbox()
        : this(null)
    {
    }

    /// <summary>
    /// Creates a sandbox: one engine with fuel accounting, epoch interruption and NaN
    /// canonicalization, one epoch timer, one folder for the stdio redirects.
    /// </summary>
    /// <param name="ioDirectory">
    /// The folder the redirects of running tasks are written to, or null for a new one in the
    /// operating system's temp path. On a node it is a scope of the client's temp storage, so
    /// nothing of a task leaves the client's own folders. Created when missing, deleted on Dispose.
    /// </param>
    public VerifyWasmSandbox(string? ioDirectory)
    {
        m_engine = new WasmEngine(CreateConfig());

        // One timer thread advances the epoch for every store; per-store deadlines
        // (SetEpochDeadline) convert a wall-time budget into a tick count.
        m_epochTimer = new Timer(_ => m_engine.IncrementEpoch(), null, EPOCH_TICK_MS, EPOCH_TICK_MS);

        m_taskModules = new VerifyTaskModuleCache(m_engine);
        m_tools = new VerifyToolRunner(m_engine, EPOCH_TICK_MS);

        m_ioDir = ioDirectory ?? Path.Combine(Path.GetTempPath(), $"witai_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_ioDir);
    }

    /// <summary>
    /// The engine every store shares. NaN canonicalization pins float bit patterns across
    /// platforms; fuel and epoch give the CPU and wall-clock envelopes. Beyond that the
    /// engine runs what a guest built for wasm32-wasi needs and nothing more:
    /// one linear memory with 32-bit addresses (the store's cap is per memory, and 64-bit
    /// tables are what the table-size panic GHSA-p8xm-42r7-89xg of wasmtime 44.0.0 needed),
    /// no shared memory, no GC heap, no exceptions (on by default since wasmtime 47, the
    /// subject of four advisories since; no runtime of the catalog uses them), no components;
    /// relaxed SIMD in its deterministic mode, so a module that uses it computes the same
    /// bits on every CPU. A guest may use 2 MiB of the native call stack, four times the
    /// engine's default; every run is on a thread that holds it (<see cref="VerifyGuestThread"/>).
    /// </summary>
    private static Config CreateConfig()
    {
        return new Config()
            .WithMaximumStackSize(VerifyGuestThread.ENGINE_STACK_BYTES)
            .WithFuelConsumption(true)
            .WithEpochInterruption(true)
            .WithCraneliftNaNCanonicalization(true)
            .WithMemory64(false)
            .WithMultiMemory(false)
            .WithWasmThreads(false)
            .WithGc(false)
            .WithExceptions(false)
            .WithComponentModel(false)
            .WithRelaxedSIMD(true, true);
    }

    #endregion

    #region Execution

    /// <summary>
    /// Executes one task: its suite case-by-case if present, otherwise a single run.
    /// Never throws for guest-side faults — every failure mode is a verdict.
    /// </summary>
    /// <param name="runtime">The resolved runtime.</param>
    /// <param name="task">The task.</param>
    /// <param name="limits">The task's effective limits.</param>
    /// <param name="cancellationToken">Checked between a suite's cases and between the steps of a C/C++ build; a run already started ends within its wall limit.</param>
    /// <returns>The task's result.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled between two cases of a suite or two steps of a build.</exception>
    public VerifyResultData Execute(VerifyResolvedRuntime runtime, VerifyTaskData task, VerifyLimitsData limits, CancellationToken cancellationToken = default)
    {
        if (runtime.Descriptor.TaskSupplied)
        {
            // The task's own module: a bad one is the task's verdict, not the node's. The lease
            // keeps the compiled module alive for the whole task while other workers churn the cache.
            VerifyTaskModuleLease lease;
            try
            {
                lease = LeaseTaskModule(task);
            }
            catch (FormatException exception)
            {
                return Verdict(task.TaskIndex, VerifyVerdict.CompileError, exception.Message);
            }
            catch (WasmtimeException exception)
            {
                return Verdict(task.TaskIndex, VerifyVerdict.CompileError, $"module failed to compile: {exception.Message}");
            }

            using (lease)
            {
                return Run(runtime, lease.Module, task, limits, cancellationToken);
            }
        }

        WasmModule module;
        try
        {
            module = GetOrCompile(runtime);
        }
        catch (WasmtimeException exception)
        {
            return Verdict(task.TaskIndex, VerifyVerdict.RuntimeUnavailable, $"runtime module failed to compile: {exception.Message}");
        }

        return Run(runtime, module, task, limits, cancellationToken);
    }

    private VerifyResultData Run(VerifyResolvedRuntime runtime, WasmModule module, VerifyTaskData task, VerifyLimitsData limits, CancellationToken cancellationToken)
    {
        if (!runtime.Descriptor.SourcesAsRoot)
            return Run(runtime, module, task, null, limits, cancellationToken);

        // The guest's file system is the task's sources (Lua): written once for the task, every
        // case of a suite reads the same files, removed when the task is done.
        var problem = VerifyTaskSources.Problem(task);
        if (problem != null)
            return Verdict(task.TaskIndex, VerifyVerdict.CompileError, problem);

        var sourceRoot = Path.Combine(m_ioDir, $"{Guid.NewGuid():N}.src");
        try
        {
            VerifyTaskSources.Write(task, sourceRoot);

            // C and C++: the module is the compiler; the task runs the program it builds.
            if (runtime.Descriptor.Language == VerifyRuntimeLanguage.C)
                return RunBuilt(runtime, module, task, sourceRoot, limits, cancellationToken);

            return Run(runtime, module, task, sourceRoot, limits, cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(sourceRoot);
        }
    }

    /// <summary>
    /// Builds a C/C++ task's program with the runtime's compiler under the compile budget, then
    /// runs it as a raw WASI program under the task's own limits. A build that fails is the task's
    /// CompileError with the compiler's diagnostics; the program sees no file system.
    /// </summary>
    private VerifyResultData RunBuilt(VerifyResolvedRuntime runtime, WasmModule compiler, VerifyTaskData task, string sourceRoot, VerifyLimitsData limits, CancellationToken cancellationToken)
    {
        var workDir = Path.Combine(m_ioDir, $"{Guid.NewGuid():N}.build");
        try
        {
            var (program, failure) = VerifyClangBuild.Build(m_tools, compiler, runtime.RootPath!, task, sourceRoot, workDir, cancellationToken);
            if (program == null)
                return Verdict(task.TaskIndex, VerifyVerdict.CompileError, failure ?? "the build failed");

            WasmModule built;
            try
            {
                built = WasmModule.FromBytes(m_engine, "program", program);
            }
            catch (WasmtimeException exception)
            {
                return Verdict(task.TaskIndex, VerifyVerdict.CompileError, $"the built program failed to compile: {exception.Message}");
            }

            using (built)
            {
                return Run(runtime, built, task, null, limits, cancellationToken);
            }
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private VerifyResultData Run(VerifyResolvedRuntime runtime, WasmModule module, VerifyTaskData task, string? sourceRoot, VerifyLimitsData limits, CancellationToken cancellationToken)
    {
        if (task.Suite is { Cases.Count: > 0 })
            return VerifySuiteEvaluator.Evaluate(task, (args, stdin) => RunProgram(runtime, module, task, sourceRoot, args, stdin, limits), cancellationToken);

        var run = RunProgram(runtime, module, task, sourceRoot, task.Args, task.Stdin, limits);
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

    /// <summary>One guest invocation in its own store. The heart of the sandbox.</summary>
    private VerifyRunOutcome RunProgram(VerifyResolvedRuntime runtime, WasmModule module, VerifyTaskData task,
        string? sourceRoot, IReadOnlyList<string> args, string? stdin, VerifyLimitsData limits)
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
            store.SetWasiConfiguration(VerifyGuestWiring.Build(runtime, task, args, sourceRoot, stdinPath, stdoutPath, stderrPath));
            store.Fuel = (ulong)Math.Max(1, limits.FuelBudget);
            // The memory cap is per linear memory, so the guest gets one; tables are host
            // memory outside that cap, so their size and number are capped as well.
            store.SetLimits(
                memorySize: Math.Max(1, limits.MemoryBytes),
                tableElements: MAX_TABLE_ELEMENTS,
                instances: MAX_INSTANCES,
                tables: MAX_TABLES,
                memories: MAX_MEMORIES);
            store.SetEpochDeadline(WallToTicks(limits.WallTimeMs));

            using var linker = VerifyDeterministicLinker.Create(m_engine, task.RandomSeed);

            var start = DateTime.UtcNow;
            var (verdict, exitCode, trapMemoryFault, peakMemory, fault) = VerifyGuestThread.Run(() => Invoke(linker, store, module));
            var wallMs = (int)(DateTime.UtcNow - start).TotalMilliseconds;

            var fuelConsumed = trapMemoryFault ? limits.FuelBudget : (long)((ulong)limits.FuelBudget - store.Fuel);

            // Dispose the store BEFORE reading — wasmtime holds OS handles on the stdio
            // redirect files until then.
            store.Dispose();
            storeDisposed = true;

            var (stdout, stdoutTruncated) = ReadCapped(stdoutPath, limits.StdoutLimitBytes);
            var (stderr, stderrTruncated) = ReadCapped(stderrPath, limits.StderrLimitBytes);

            // A module that could not be instantiated never ran, and a trap ends a run without a
            // word from the program: when it said nothing, the host's reason is its stderr.
            if (fault != null && stderr.Length == 0)
                stderr = fault;

            // An allocation past the store cap usually surfaces as a graceful in-guest OOM
            // (a clean nonzero exit), not a hard trap. Reclassify it to MemoryExceeded so the
            // verdict stays useful — best-effort, by the runtime's OOM marker.
            if (verdict is VerifyVerdict.RuntimeError && LooksLikeOutOfMemory(stderr))
                verdict = VerifyVerdict.MemoryExceeded;

            // A program that floods stdout violates the small-output contract regardless
            // of whether its bytes matched — surface it distinctly.
            if (stdoutTruncated && verdict is VerifyVerdict.Pass)
                verdict = VerifyVerdict.OutputExceeded;

            return new VerifyRunOutcome(verdict, exitCode, stdout, stdoutTruncated, stderr, stderrTruncated,
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

    private static InvokeOutcome Invoke(Linker linker, Store store, WasmModule module)
    {
        Instance instance;
        try
        {
            instance = linker.Instantiate(store, module);
        }
        catch (WasmtimeException exception) when (exception.ExitCode is { } code)
        {
            // A guest that proc_exits during module init (before _start) still lands here.
            return code == 0 ? new(VerifyVerdict.Pass, 0, false, 0) : new(VerifyVerdict.RuntimeError, code, false, 0);
        }
        catch (TrapException trap)
        {
            return MapTrap(trap);
        }
        catch (WasmtimeException exception)
        {
            // Unresolved imports, a type mismatch against WASI, a memory the store cannot
            // grant: the module never ran. A task-supplied module owns this verdict.
            return new(VerifyVerdict.CompileError, -1, false, 0, $"module failed to instantiate: {exception.Message}");
        }

        try
        {
            var startAction = instance.GetAction(START_EXPORT);
            if (startAction == null)
                return new(VerifyVerdict.RuntimeError, -1, false, PeakMemory(instance), $"module exports no {START_EXPORT}");

            startAction();
            return new(VerifyVerdict.Pass, 0, false, PeakMemory(instance));
        }
        catch (WasmtimeException exception) when (exception.ExitCode is { } code)
        {
            // WASI proc_exit — a clean guest exit with a code (0 = success).
            var peak = PeakMemory(instance);
            return code == 0 ? new(VerifyVerdict.Pass, 0, false, peak) : new(VerifyVerdict.RuntimeError, code, false, peak);
        }
        catch (TrapException trap)
        {
            return MapTrap(trap);
        }
    }

    private static InvokeOutcome MapTrap(TrapException trap)
    {
        var verdict = trap.Type switch
        {
            // Both fuel exhaustion and epoch interruption mean "did not finish in budget".
            TrapCode.OutOfFuel => VerifyVerdict.Timeout,
            TrapCode.Interrupt => VerifyVerdict.Timeout,
            // Anything else is a crash, an access outside the memory included: a wild pointer or
            // a stack that overflowed is a fault of the program, not a refusal of memory. Memory
            // refused to an allocator surfaces as the program's own message (LooksLikeOutOfMemory).
            _ => VerifyVerdict.RuntimeError
        };

        // A crash is named in fixed words. A budget stop is not: one node may stop on fuel where
        // another stops on the clock, and the outputs must not tell them apart.
        var fault = verdict == VerifyVerdict.Timeout ? null : $"wasm trap: {TrapText(trap.Type)}";
        return new(verdict, -1, true, 0, fault);
    }

    private static string TrapText(TrapCode code)
    {
        return code switch
        {
            TrapCode.MemoryOutOfBounds => "out of bounds memory access (a pointer outside the memory, or a stack that overflowed)",
            TrapCode.StackOverflow => "call stack exhausted",
            TrapCode.Unreachable => "unreachable code reached (abort, a failed assertion, a trap instruction)",
            TrapCode.IntegerDivisionByZero => "integer division by zero",
            TrapCode.IntegerOverflow => "integer overflow",
            TrapCode.BadConversionToInteger => "invalid conversion to integer",
            TrapCode.IndirectCallToNull => "call through a null function pointer",
            TrapCode.BadSignature => "call through a function pointer of the wrong type",
            TrapCode.TableOutOfBounds => "out of bounds table access",
            TrapCode.HeapMisaligned => "misaligned memory access",
            _ => code.ToString()
        };
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

    #region Modules

    private WasmModule GetOrCompile(VerifyResolvedRuntime runtime)
    {
        // Keyed by the module file: runtimes that differ only in their layers share one compile.
        lock (m_modulesLock)
        {
            if (m_modules.TryGetValue(runtime.ModulePath, out var cached))
                return cached;

            var module = WasmModule.FromFile(m_engine, runtime.ModulePath);
            m_modules[runtime.ModulePath] = module;
            return module;
        }
    }

    /// <summary>The raw WASI runtime: the entry source holds the module bytes, base64.</summary>
    /// <exception cref="FormatException">The entry is missing or not base64.</exception>
    /// <exception cref="WasmtimeException">The bytes are not a valid module.</exception>
    private VerifyTaskModuleLease LeaseTaskModule(VerifyTaskData task)
    {
        var entry = VerifyGuestWiring.EntrySource(task) ?? throw new FormatException("a wasi task needs an entry source: the module bytes, base64");

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(entry.Content);
        }
        catch (FormatException)
        {
            throw new FormatException($"entry '{entry.Name}' of a wasi task must hold the module bytes in base64");
        }

        return m_taskModules.Lease(entry.Name, bytes);
    }

    #endregion

    #region Helpers

    private static ulong WallToTicks(int wallMs)
    {
        var ticks = (wallMs + EPOCH_TICK_MS - 1) / EPOCH_TICK_MS;
        return (ulong)Math.Max(1, ticks);
    }

    private static bool LooksLikeOutOfMemory(string stderr)
    {
        // Python raises MemoryError and Ruby NoMemoryError (the same marker is in its name);
        // QuickJS-NG prints "out of memory" on allocation failure, Lua "not enough memory", and
        // C++'s operator new without exceptions "bad_alloc was thrown in -fno-exceptions mode".
        return stderr.Contains("MemoryError", StringComparison.Ordinal)
               || stderr.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
               || stderr.Contains("not enough memory", StringComparison.Ordinal)
               || stderr.Contains("bad_alloc was thrown", StringComparison.Ordinal);
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

    private static VerifyResultData Verdict(int taskIndex, VerifyVerdict verdict, string reason)
    {
        return new VerifyResultData
        {
            TaskIndex = taskIndex,
            Verdict = verdict,
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // a task's sources in the working folder; best-effort, the folder goes on Dispose
        }
    }

    #endregion

    #region IDisposable

    /// <summary>Releases the compiled modules, the engine, the epoch timer and the stdio folder.</summary>
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

        m_taskModules.Dispose();
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

    #region Properties

    /// <summary>The folder the stdio redirects of running tasks live in; empty between runs, gone after Dispose.</summary>
    public string IoDirectory => m_ioDir;

    /// <summary>Compiled task-supplied modules held right now (bounded by <see cref="TaskModuleCacheCapacity"/>).</summary>
    public int TaskModuleCacheCount => m_taskModules.Count;

    /// <summary>The most task-supplied modules the sandbox keeps compiled at once.</summary>
    public static int TaskModuleCacheCapacity => VerifyTaskModuleCache.DEFAULT_CAPACITY;

    #endregion

    #region Nested Types

    /// <summary>What one instantiate-and-start attempt came to; Fault is the host's reason when the guest never ran.</summary>
    private sealed record InvokeOutcome(VerifyVerdict Verdict, int ExitCode, bool MemoryFault, long PeakMemory, string? Fault = null);

    #endregion
}
