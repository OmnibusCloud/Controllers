using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Wasm;
using Wasmtime;
using WasmModule = Wasmtime.Module;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// The execution core: runs one task under a pinned WASI runtime in a fresh, isolated
/// store within a fuel / memory envelope, and maps the outcome to a verdict. One sandbox owns two
/// wasm hosts (<see cref="WasmHost"/>): one for the runtime modules the controller ships, compiled
/// once each (the persistent-batch economy), and one for the modules a task brings or the compiler
/// builds from its sources, whose compiling stays on the worker that asked for it and is priced
/// before it starts (<see cref="VerifyModuleShape"/>). Tasks are otherwise independent, so callers
/// run many <see cref="Execute"/> calls concurrently across a node's cores.
///
/// The guest's world is <see cref="VerifyGuestWiring"/> (argv, environment, read-only root),
/// its clock, randomness and files <see cref="WasmHostLinker"/>'s and <see cref="WasmHostSyscalls"/>'s,
/// the run itself the host's, a suite's verdict <see cref="VerifySuiteEvaluator"/>'s.
///
/// A verdict is a statement about the task. Whatever is the node's own trouble (the wall-clock
/// backstop, a file it could not write, an engine it could not make) is a result without a
/// verdict: <see cref="VerifyVerdict.NoResult"/> with a reason code, for the task alone.
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

    /// <summary>The largest module a raw WASI task may bring, bytes.</summary>
    public const int MAX_TASK_MODULE_BYTES = 8 << 20;

    /// <summary>
    /// The price of a module's shape from which its compile waits for its turn: one such compile
    /// at a time in the process. What a compile of that kind takes in memory (hundreds of
    /// megabytes, over a gigabyte at the largest budget) is in no task's memory cap, so the
    /// workers of a batch must not all be in one at once.
    /// </summary>
    public const long HEAVY_COMPILE_FUEL = 1_000_000_000;

    private const string BUILT_PROGRAM = "program";

    #endregion

    #region Fields

    // How long closing the sandbox waits for the files of its runs to be deleted.
    private static readonly TimeSpan CLEANUP_WAIT = TimeSpan.FromSeconds(5);

    private readonly WasmHost m_host;
    private readonly WasmHost m_taskHost;
    private readonly string m_ioDir;
    private readonly object m_modulesLock = new();
    private readonly object m_heavyCompile = new();
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
    /// Creates a sandbox: two engines with fuel accounting, epoch interruption and NaN
    /// canonicalization (the one for task-supplied modules compiles on the calling thread
    /// alone), and one folder for the files of running tasks.
    /// </summary>
    /// <param name="ioDirectory">
    /// The folder the files of running tasks are written to, or null for a new one in the
    /// operating system's temp path. On a node it is a scope of the client's temp storage, so
    /// nothing of a task leaves the client's own folders. Created when missing, deleted on Dispose.
    /// </param>
    /// <exception cref="InvalidOperationException">The loaded wasmtime binding is not the one this controller was built against (the message names both versions), the native library is not the pinned one or none was found (the message names both hashes), or the binding cannot configure the engine.</exception>
    public VerifyWasmSandbox(string? ioDirectory)
    {
        m_host = new WasmHost();
        try
        {
            m_taskHost = new WasmHost(parallelCompilation: false);
        }
        catch
        {
            m_host.Dispose();
            throw;
        }

        m_taskModules = new VerifyTaskModuleCache(m_taskHost.Engine, m_taskHost.CompiledCodeKey);
        m_tools = new VerifyToolRunner(m_host);

        m_ioDir = ioDirectory ?? Path.Combine(Path.GetTempPath(), $"witai_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_ioDir);
    }

    #endregion

    #region Execution

    /// <summary>
    /// Executes one task: its suite case-by-case if present, otherwise a single run.
    /// Never throws for a fault of the guest or of the node: a guest's failure is a verdict, and
    /// the node's own failure is a result without one (<see cref="VerifyVerdict.NoResult"/> with a
    /// reason code), for this task alone.
    /// </summary>
    /// <param name="runtime">The resolved runtime.</param>
    /// <param name="task">The task.</param>
    /// <param name="limits">The task's effective limits.</param>
    /// <param name="cancellationToken">Checked between a suite's cases and between the steps of a C/C++ build; a run already started ends within its budget.</param>
    /// <param name="onNodeFault">Told what failed on the node when the task has no verdict for that; for the node's own log, never for a result.</param>
    /// <returns>The task's result.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled between two cases of a suite or two steps of a build.</exception>
    public VerifyResultData Execute(VerifyResolvedRuntime runtime, VerifyTaskData task, VerifyLimitsData limits,
        CancellationToken cancellationToken = default, Action<Exception>? onNodeFault = null)
    {
        try
        {
            return ExecuteTask(runtime, task, limits, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Nothing the program did: the wall-clock backstop, a disk that is full, an engine
            // error outside a guest's call. The reason travels as a code; the rest stays here.
            onNodeFault?.Invoke(exception);
            return Faulted(task.TaskIndex, exception is VerifyNodeFaultException fault ? fault.Code : VerifyFault.NODE_FAULT);
        }
    }

    private VerifyResultData ExecuteTask(VerifyResolvedRuntime runtime, VerifyTaskData task, VerifyLimitsData limits, CancellationToken cancellationToken)
    {
        if (runtime.Descriptor.TaskSupplied)
        {
            // The task's own module: a bad one is the task's verdict, not the node's.
            var entry = VerifyGuestWiring.EntrySource(task);
            if (entry == null)
                return Verdict(task.TaskIndex, VerifyVerdict.CompileError, "a wasi task needs an entry source: the module bytes, base64");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(entry.Content);
            }
            catch (FormatException)
            {
                return Verdict(task.TaskIndex, VerifyVerdict.CompileError, "the entry of a wasi task must hold the module bytes in base64");
            }

            if (bytes.Length > MAX_TASK_MODULE_BYTES)
                return Verdict(task.TaskIndex, VerifyVerdict.CompileError, $"the module is larger than {MAX_TASK_MODULE_BYTES >> 20} MiB");

            return RunTaskModule(runtime, task, entry.Name, bytes, "module", limits, cancellationToken);
        }

        var module = GetOrCompile(runtime);
        if (!runtime.Descriptor.SourcesAsRoot)
            return Run(m_host, runtime, module, task, null, limits, 0, cancellationToken);

        // The guest's file system is the task's sources (Lua): written once for the task, every
        // case of a suite reads the same files, removed when the task is done.
        var problem = VerifyTaskSources.Problem(task);
        if (problem != null)
            return Verdict(task.TaskIndex, VerifyVerdict.CompileError, problem);

        var sourceRoot = Path.Combine(m_ioDir, $"{Guid.NewGuid():N}.src");
        try
        {
            var sources = VerifyGuestWiring.Sources(sourceRoot, VerifyTaskSources.Write(task, sourceRoot));

            // C and C++: the module is the compiler; the task runs the program it builds.
            if (runtime.Descriptor.Language == VerifyRuntimeLanguage.C)
                return RunBuilt(runtime, module, task, sources, limits, cancellationToken);

            return Run(m_host, runtime, module, task, sources, limits, 0, cancellationToken);
        }
        finally
        {
            // Off the task's path: a file scanner can hold a delete for a long time.
            WasmHostDiscard.Path(sourceRoot);
        }
    }

    /// <summary>
    /// Builds a C/C++ task's program with the runtime's compiler under the compile budget, then
    /// runs it as a raw WASI program under the task's own limits. A build that fails is the task's
    /// CompileError with the compiler's diagnostics; the program sees no file system.
    /// </summary>
    private VerifyResultData RunBuilt(VerifyResolvedRuntime runtime, WasmModule compiler, VerifyTaskData task, WasmHostPreopen sources, VerifyLimitsData limits, CancellationToken cancellationToken)
    {
        var workDir = Path.Combine(m_ioDir, $"{Guid.NewGuid():N}.build");
        try
        {
            var sysroot = VerifyGuestWiring.Root(runtime, runtime.RootPath!, VerifyClangBuild.SYSROOT);
            var (program, failure) = VerifyClangBuild.Build(m_tools, compiler, sysroot, task, sources, workDir, cancellationToken);
            if (program == null)
                return Verdict(task.TaskIndex, VerifyVerdict.CompileError, failure ?? "the build failed");

            return RunTaskModule(runtime, task, BUILT_PROGRAM, program, "built program", limits, cancellationToken);
        }
        finally
        {
            WasmHostDiscard.Path(workDir);
        }
    }

    /// <summary>
    /// Runs a module that came with the task or was built from its sources. The engine validates
    /// it, its shape is priced, and only a task whose fuel budget pays for the compile has it
    /// compiled: on the calling worker, once however many workers want it.
    /// </summary>
    private VerifyResultData RunTaskModule(VerifyResolvedRuntime runtime, VerifyTaskData task, string name, byte[] bytes, string what,
        VerifyLimitsData limits, CancellationToken cancellationToken)
    {
        var invalid = WasmModule.Validate(m_taskHost.Engine, bytes);
        if (invalid != null)
            return Verdict(task.TaskIndex, VerifyVerdict.CompileError, $"the {what} failed to compile: {invalid}");

        var shape = VerifyModuleShape.Scan(bytes);
        if (shape.Problem != null)
            return Verdict(task.TaskIndex, VerifyVerdict.CompileError, shape.Problem);

        // Compiling is work for the task like any other: what it costs comes out of the fuel
        // budget, and a budget that does not hold it is spent before anything is compiled.
        if (shape.CompileFuel >= limits.FuelBudget)
        {
            return new VerifyResultData
            {
                TaskIndex = task.TaskIndex,
                Verdict = VerifyVerdict.Timeout,
                ExitCode = -1,
                FuelConsumed = limits.FuelBudget
            };
        }

        VerifyTaskModuleLease lease;
        try
        {
            // The lease keeps the compiled module alive for the whole task while other workers
            // churn the cache.
            if (shape.ShapeFuel >= HEAVY_COMPILE_FUEL)
            {
                lock (m_heavyCompile)
                {
                    lease = m_taskModules.Lease(name, bytes);
                }
            }
            else
            {
                lease = m_taskModules.Lease(name, bytes);
            }
        }
        catch (WasmtimeException exception)
        {
            return Verdict(task.TaskIndex, VerifyVerdict.CompileError, $"the {what} failed to compile: {exception.Message}");
        }

        using (lease)
        {
            return Run(m_taskHost, runtime, lease.Module, task, null, limits, shape.CompileFuel, cancellationToken);
        }
    }

    private VerifyResultData Run(WasmHost host, VerifyResolvedRuntime runtime, WasmModule module, VerifyTaskData task, WasmHostPreopen? sources,
        VerifyLimitsData limits, long compileFuel, CancellationToken cancellationToken)
    {
        VerifyResultData result;
        if (task.Suite is { Cases.Count: > 0 })
        {
            result = VerifySuiteEvaluator.Evaluate(task, limits.StdoutLimitBytes,
                (args, stdin, stdoutCap) => RunProgram(host, runtime, module, task, sources, args, stdin, limits, compileFuel, stdoutCap), cancellationToken);
        }
        else
        {
            var run = RunProgram(host, runtime, module, task, sources, task.Args, task.Stdin, limits, compileFuel, limits.StdoutLimitBytes);
            if (run.Fault != null)
                return Faulted(task.TaskIndex, run.Fault);

            result = new VerifyResultData
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

        // The compile is paid once a task, whatever the number of its runs.
        if (result.Fault.Length == 0)
            result.FuelConsumed += compileFuel;

        return result;
    }

    /// <summary>One guest invocation in its own store (the host's run), mapped to Verify's verdicts.</summary>
    private VerifyRunOutcome RunProgram(WasmHost host, VerifyResolvedRuntime runtime, WasmModule module, VerifyTaskData task,
        WasmHostPreopen? sources, IReadOnlyList<string> args, string? stdin, VerifyLimitsData limits, long compileFuel, int stdoutCap)
    {
        var fuel = Math.Max(1, limits.FuelBudget - compileFuel);
        var outcome = host.Run(module, new WasmHostRunRequest
        {
            Argv = VerifyGuestWiring.BuildArgv(runtime, task, args),
            Environment = VerifyGuestWiring.Environment(runtime),
            Preopens = VerifyGuestWiring.Preopens(runtime, sources),
            Stdin = stdin,
            CaptureDirectory = m_ioDir,
            Fuel = fuel,
            // The budget is the program's own: what instantiation costs (a unit a byte of data
            // segments where the platform copies them, nothing where it maps an image) is in
            // neither the budget nor the fuel reported, so both are the same on every platform.
            FuelAfterInstantiation = true,
            MemoryBytes = limits.MemoryBytes,
            // A backstop of the node's own, sized from the fuel: it gives no verdict.
            WallMs = BackstopMs ?? VerifyBackstop.WallMs(limits.FuelBudget),
            RandomSeed = task.RandomSeed,
            StdoutLimitBytes = stdoutCap,
            StopAtStdoutLimit = true,
            StderrLimitBytes = limits.StderrLimitBytes,
            ChargeHostCalls = true,
            DiscardInBackground = true,
            OutOfMemory = LooksLikeOutOfMemory
        });

        // The clock, not fuel, ended the run: that says something about this node (slow, busy,
        // or starved of processor time) and nothing about the program.
        if (outcome.Verdict == WasmHostVerdict.Interrupted)
            return VerifyRunOutcome.Faulted(VerifyFault.WALL_CLOCK, outcome.WallMs);

        var verdict = VerdictOf(outcome);

        // A program that floods stdout violates the small-output contract regardless
        // of whether its bytes matched — surface it distinctly.
        if (outcome.StdoutTruncated && verdict is VerifyVerdict.Pass)
            verdict = VerifyVerdict.OutputExceeded;

        var exited = outcome.Trap == null && outcome.Fault == null && outcome.Verdict is WasmHostVerdict.Exited or WasmHostVerdict.OutOfMemory;

        // A run a trap ended (a crash or a budget stop) reports its whole budget and no peak.
        var trapped = outcome.Trap != null;
        return new VerifyRunOutcome(verdict, outcome.ExitCode, exited, outcome.Stdout, outcome.StdoutTruncated, (int)outcome.StdoutBytes, outcome.Stderr, outcome.StderrTruncated,
            trapped ? fuel : outcome.FuelConsumed, trapped ? 0 : outcome.PeakMemoryBytes, outcome.WallMs);
    }

    private static VerifyVerdict VerdictOf(WasmHostRunOutcome outcome)
    {
        return outcome.Verdict switch
        {
            WasmHostVerdict.Exited => outcome.ExitCode == 0 ? VerifyVerdict.Pass : VerifyVerdict.RuntimeError,
            // Out of fuel, in the program's own instructions or in a call that costs fuel.
            WasmHostVerdict.OutOfFuel => VerifyVerdict.Timeout,
            // An allocation past the store cap usually surfaces as a graceful in-guest OOM
            // (a clean nonzero exit), not a hard trap; recognised by the runtime's OOM marker.
            WasmHostVerdict.OutOfMemory => VerifyVerdict.MemoryExceeded,
            // The run ended at the write that took stdout past its cap.
            WasmHostVerdict.OutputLimit => VerifyVerdict.OutputExceeded,
            // Unresolved imports, a type mismatch against WASI, a memory the store cannot
            // grant: the module never ran. A task-supplied module owns this verdict.
            WasmHostVerdict.NotInstantiated => VerifyVerdict.CompileError,
            // A crash (an access outside the memory included: a wild pointer or a stack that
            // overflowed is a fault of the program, not a refusal of memory), no _start, or a
            // call of the guest the engine refused: all are the program's.
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

    #endregion

    #region Helpers

    /// <summary>A task without a verdict: the node could not run it to an end of its own.</summary>
    /// <param name="taskIndex">The task.</param>
    /// <param name="fault">The reason code (<see cref="VerifyFault"/>).</param>
    /// <returns>The result.</returns>
    internal static VerifyResultData Faulted(int taskIndex, string fault)
    {
        return new VerifyResultData
        {
            TaskIndex = taskIndex,
            Verdict = VerifyVerdict.NoResult,
            ExitCode = -1,
            Fault = fault
        };
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

    #endregion

    #region IDisposable

    /// <summary>Releases the compiled modules, the engines, their epoch threads and the working folder.</summary>
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
        m_taskHost.Dispose();
        m_host.Dispose();

        // What the runs handed over to be deleted is under this folder: let that end first, so
        // that the two do not meet in it.
        WasmHostDiscard.Drain(CLEANUP_WAIT);

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

    /// <summary>
    /// The folder the files of running tasks live in; gone after Dispose. What a run leaves in
    /// it is deleted in the background, so it is empty soon after a run, not at its return.
    /// </summary>
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

    /// <summary>
    /// The wall-clock backstop of every run, milliseconds, in place of the one sized from the
    /// run's fuel (<see cref="VerifyBackstop"/>); null for that one. For tests of the backstop.
    /// </summary>
    internal int? BackstopMs { get; set; }

    #endregion
}
