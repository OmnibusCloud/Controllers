using System.Runtime.ExceptionServices;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Wasm;
using Wasmtime;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// The execution core: runs one task under a pinned WASI runtime in a fresh, isolated
/// store within a fuel / memory / wall-clock envelope, and maps the outcome to a verdict.
/// One sandbox instance owns one wasm host (<see cref="WasmHost"/>: one wasmtime engine and its
/// epoch timer) and compiles each runtime module once (the persistent-batch economy); tasks are
/// otherwise independent, so callers run many <see cref="Execute"/> calls concurrently across a
/// node's cores.
///
/// The guest's world is <see cref="VerifyGuestWiring"/> (argv, environment, read-only root),
/// its clock and randomness <see cref="WasmHostLinker"/>'s, the run itself the host's, a suite's
/// verdict <see cref="VerifySuiteEvaluator"/>'s.
/// </summary>
public sealed class VerifyWasmSandbox : IDisposable
{
    #region Constants

    /// <summary>
    /// The most elements one table of a guest may hold. A table is host memory the linear
    /// memory cap does not count (eight bytes an element or more), so it has a cap of its
    /// own: over a hundred times the largest table of the bundled runtimes.
    /// </summary>
    public const uint MAX_TABLE_ELEMENTS = WasmHostLimits.MAX_TABLE_ELEMENTS;

    #endregion

    #region Fields

    private readonly WasmHost m_host;
    private readonly string m_ioDir;
    private readonly object m_modulesLock = new();
    private readonly Dictionary<string, WasmModule> m_modules = new();
    private readonly VerifyTaskModuleCache m_taskModules;
    private readonly VerifyToolRunner m_tools;
    private bool m_disposed;

    #endregion

    #region Constructors

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
    /// <exception cref="InvalidOperationException">The loaded wasmtime binding is not the one this controller was built against (the message names both), or it cannot configure the engine.</exception>
    public VerifyWasmSandbox(string? ioDirectory)
    {
        m_host = new WasmHost();

        m_taskModules = new VerifyTaskModuleCache(m_host.Engine);
        m_tools = new VerifyToolRunner(m_host);

        m_ioDir = ioDirectory ?? Path.Combine(Path.GetTempPath(), $"witai_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_ioDir);
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
                built = WasmModule.FromBytes(m_host.Engine, "program", program);
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

    /// <summary>One guest invocation in its own store (the host's run), mapped to Verify's verdicts.</summary>
    private VerifyRunOutcome RunProgram(VerifyResolvedRuntime runtime, WasmModule module, VerifyTaskData task,
        string? sourceRoot, IReadOnlyList<string> args, string? stdin, VerifyLimitsData limits)
    {
        var outcome = m_host.Run(module, new WasmHostRunRequest
        {
            Argv = VerifyGuestWiring.BuildArgv(runtime, task, args),
            Environment = VerifyGuestWiring.Environment(runtime),
            Preopens = VerifyGuestWiring.Preopens(runtime, sourceRoot),
            Stdin = stdin,
            CaptureDirectory = m_ioDir,
            Fuel = limits.FuelBudget,
            MemoryBytes = limits.MemoryBytes,
            WallMs = limits.WallTimeMs,
            RandomSeed = task.RandomSeed,
            StdoutLimitBytes = limits.StdoutLimitBytes,
            StderrLimitBytes = limits.StderrLimitBytes,
            OutOfMemory = LooksLikeOutOfMemory
        });

        // Kept as Verify has always had it: a run the engine ended without a trap or an exit code
        // (a proc_exit with a status of 126 or more) is no verdict here but the engine's own
        // exception, which fails the batch. Making it a verdict changes results: a release's call.
        if (outcome.EngineError != null)
            ExceptionDispatchInfo.Capture(outcome.EngineError).Throw();

        var verdict = VerdictOf(outcome);

        // A program that floods stdout violates the small-output contract regardless
        // of whether its bytes matched — surface it distinctly.
        if (outcome.StdoutTruncated && verdict is VerifyVerdict.Pass)
            verdict = VerifyVerdict.OutputExceeded;

        // A run a trap ended (a crash or a budget stop) reports its whole budget and no peak: a
        // budget stop must read the same whether fuel or the clock ended it.
        var trapped = outcome.Trap != null;
        return new VerifyRunOutcome(verdict, outcome.ExitCode, outcome.Stdout, outcome.StdoutTruncated, outcome.Stderr, outcome.StderrTruncated,
            trapped ? limits.FuelBudget : outcome.FuelConsumed, trapped ? 0 : outcome.PeakMemoryBytes, outcome.WallMs);
    }

    private static VerifyVerdict VerdictOf(WasmHostRunOutcome outcome)
    {
        return outcome.Verdict switch
        {
            WasmHostVerdict.Exited => outcome.ExitCode == 0 ? VerifyVerdict.Pass : VerifyVerdict.RuntimeError,
            // Both fuel exhaustion and epoch interruption mean "did not finish in budget".
            WasmHostVerdict.OutOfFuel or WasmHostVerdict.Interrupted => VerifyVerdict.Timeout,
            // An allocation past the store cap usually surfaces as a graceful in-guest OOM
            // (a clean nonzero exit), not a hard trap; recognised by the runtime's OOM marker.
            WasmHostVerdict.OutOfMemory => VerifyVerdict.MemoryExceeded,
            // Unresolved imports, a type mismatch against WASI, a memory the store cannot
            // grant: the module never ran. A task-supplied module owns this verdict.
            WasmHostVerdict.NotInstantiated => VerifyVerdict.CompileError,
            // A crash (an access outside the memory included: a wild pointer or a stack that
            // overflowed is a fault of the program, not a refusal of memory), or no _start.
            _ => VerifyVerdict.RuntimeError
        };
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

            var module = WasmModule.FromFile(m_host.Engine, runtime.ModulePath);
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

        lock (m_modulesLock)
        {
            foreach (var module in m_modules.Values)
                module.Dispose();
            m_modules.Clear();
        }

        m_taskModules.Dispose();
        m_host.Dispose();

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

    /// <summary>
    /// Whether a guest's traps are explicit checks in this process: true where the host enforces
    /// hardware stack protection (a hardware trap could not be resumed from), false elsewhere.
    /// </summary>
    public bool ExplicitTraps => m_host.ExplicitTraps;

    /// <summary>Compiled task-supplied modules held right now (bounded by <see cref="TaskModuleCacheCapacity"/>).</summary>
    public int TaskModuleCacheCount => m_taskModules.Count;

    /// <summary>The most task-supplied modules the sandbox keeps compiled at once.</summary>
    public static int TaskModuleCacheCapacity => VerifyTaskModuleCache.DEFAULT_CAPACITY;

    #endregion
}
