using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Model;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The node benchmark of <c>ParaView.Compose</c>: deliberately CHEAP. A composition is one process
/// that opens a small result and saves a state — startup-dominated, seconds long, one per job — so
/// the measurement is a warm-up cycle plus one or two timed cycles of exactly that (the embedded
/// benchmark <c>.frd</c>, default presentation), reported as compose cycles per second. The rate only
/// has to rank nodes for <c>Grid.Delegate</c>'s single task; it never sizes a fan-out, and it must
/// not park every node in a long second benchmark whenever the controller updates.
/// </summary>
public static class ParaViewComposeBenchmark
{
    #region Constants

    /// <summary>Rate unit: complete compose cycles per second.</summary>
    public const string UNIT = "paraview-compose-cycles@v1";

    /// <summary>Identifies the measurement procedure and dataset.</summary>
    public const string DATASET_ID = "paraview-compose-static-frd@v1";

    /// <summary>Logical path the benchmark data is materialized at.</summary>
    public const string DATA_LOGICAL_PATH = "data/benchmark.frd";

    /// <summary>Untimed cycles before measurement (the first pvpython launch on a cold page cache costs ~3× a warm one).</summary>
    public const int WARMUP_CYCLES = 1;

    /// <summary>Timed cycles never stop before this many.</summary>
    public const int MIN_CYCLES = 1;

    /// <summary>Upper bound on timed cycles.</summary>
    public const int MAX_CYCLES = 2;

    /// <summary>Timed duration when the engine passes no positive MinDuration.</summary>
    public static readonly TimeSpan FALLBACK_TARGET = TimeSpan.FromSeconds(3);

    /// <summary>Custom result keys.</summary>
    public const string CUSTOM_CYCLES = "compose-cycles";
    public const string CUSTOM_CYCLE_SECONDS = "cycle-seconds";
    public const string CUSTOM_PARAVIEW_VERSION = "paraview-version";
    public const string CUSTOM_RENDER_WINDOW = "render-window";

    #endregion

    #region Functions

    /// <summary>
    /// Runs the benchmark through the resolved pvpython.
    /// </summary>
    /// <param name="pvpythonPath">Resolved pvpython executable.</param>
    /// <param name="tempStorage">Node temp storage the benchmark workspace is created under.</param>
    /// <param name="options">Engine benchmark options (MinDuration → timed seconds, WarmupIterations → warm-up cycles) or null for defaults.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="cancellationToken">Kills the composer process tree when signaled.</param>
    /// <returns>The measured rate (compose cycles per second) with the run metadata in <see cref="WitBenchmarkResult.Custom"/>.</returns>
    /// <exception cref="InvalidOperationException">pvpython is missing, or a cycle failed, timed out, or wrote no usable status.</exception>
    /// <exception cref="OperationCanceledException">The benchmark was cancelled.</exception>
    public static async Task<WitBenchmarkResult> MeasureAsync(
        string pvpythonPath,
        IWitTempStorage tempStorage,
        IWitBenchmarkOptions? options,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(pvpythonPath))
            throw new InvalidOperationException($"{ParaViewComposeExecutor.ACTIVITY_NAME} benchmark: pvpython '{pvpythonPath}' does not exist.");

        IWitBenchmarkOptions benchmarkOptions = options ?? WitBenchmarkOptions.Default;
        var target = benchmarkOptions.MinDuration <= TimeSpan.Zero
            ? FALLBACK_TARGET
            : benchmarkOptions.MinDuration;
        var warmupCycles = Math.Max(WARMUP_CYCLES, benchmarkOptions.WarmupIterations);

        using var workspace = ParaViewTaskWorkspace.Create(tempStorage, Guid.NewGuid(), 0);

        var dataPath = workspace.WriteEmbedded(ParaViewRuntimeInfo.BENCHMARK_FRD_RESOURCE, Path.Combine(workspace.PackageRoot, "data"), "benchmark.frd");
        var runnerPath = workspace.WriteEmbedded(ParaViewRuntimeInfo.COMPOSE_RUNNER_RESOURCE, workspace.RunnerDirectory, ParaViewRuntimeInfo.COMPOSE_RUNNER_FILE_NAME);
        var pluginPath = workspace.WriteEmbedded(ParaViewRuntimeInfo.FRD_READER_RESOURCE, workspace.PluginsDirectory, ParaViewRuntimeInfo.FRD_READER_FILE_NAME);

        var materialized = new ParaViewMaterializedAttachment(DATA_LOGICAL_PATH, dataPath, string.Empty, new FileInfo(dataPath).Length);
        var task = ParaViewComposeExecutor.BuildTask(new ParaViewDataSceneData(), new ParaViewOutputOptionsData(), workspace, materialized, pluginPath);
        await File.WriteAllTextAsync(workspace.ComposeTaskFilePath, task.ToJson(), cancellationToken);

        var arguments = ParaViewComposeExecutor.BuildArguments(runnerPath, workspace.ComposeTaskFilePath);
        var openGlWindow = await ParaViewRenderingBackend.ResolveWindowAsync(pvpythonPath, tempStorage, logger, cancellationToken);

        try
        {
            return await MeasureCyclesAsync(pvpythonPath, arguments, workspace, openGlWindow, warmupCycles, target, logger, cancellationToken);
        }
        catch (InvalidOperationException error) when (ParaViewRenderingBackend.IsEglWindow(openGlWindow))
        {
            logger?.LogWarning("{ActivityName} benchmark: the EGL composer failed ({Message}); demoting this node to OSMesa and re-measuring", ParaViewComposeExecutor.ACTIVITY_NAME, error.Message);
            ParaViewRenderingBackend.Demote(pvpythonPath, logger);
            return await MeasureCyclesAsync(pvpythonPath, arguments, workspace, ParaViewRunnerEnvironment.OSMESA_WINDOW, warmupCycles, target, logger, cancellationToken);
        }
    }

    /// <summary>
    /// The result a node reports when it has no usable ParaView runtime: rate 0 keeps the allocator
    /// from handing it work.
    /// </summary>
    /// <returns>A zero-rate result in the benchmark's unit.</returns>
    public static WitBenchmarkResult CreateUnavailableResult()
    {
        return new WitBenchmarkResult
        {
            Rate = 0,
            Unit = UNIT,
            Elapsed = TimeSpan.Zero,
            Iterations = 0,
            DatasetId = DATASET_ID
        };
    }

    #endregion

    #region Tools

    private static async Task<WitBenchmarkResult> MeasureCyclesAsync(
        string pvpythonPath,
        IReadOnlyList<string> arguments,
        ParaViewTaskWorkspace workspace,
        string? openGlWindow,
        int warmupCycles,
        TimeSpan target,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < warmupCycles; index++)
            await RunCycleAsync(pvpythonPath, arguments, workspace, openGlWindow, logger, cancellationToken);

        var cycles = 0;
        ParaViewComposeStatus lastCycle = null!;
        var stopwatch = Stopwatch.StartNew();

        while (cycles < MAX_CYCLES)
        {
            lastCycle = await RunCycleAsync(pvpythonPath, arguments, workspace, openGlWindow, logger, cancellationToken);
            cycles++;

            if (cycles >= MIN_CYCLES && stopwatch.Elapsed >= target)
                break;
        }

        stopwatch.Stop();
        if (stopwatch.Elapsed <= TimeSpan.Zero)
            throw new InvalidOperationException($"{ParaViewComposeExecutor.ACTIVITY_NAME} benchmark: {cycles} cycle(s) took no measurable time — nothing to measure.");

        return new WitBenchmarkResult
        {
            Rate = cycles / stopwatch.Elapsed.TotalSeconds,
            Unit = UNIT,
            Elapsed = stopwatch.Elapsed,
            Iterations = cycles,
            DatasetId = DATASET_ID,
            Custom = new Dictionary<string, string>
            {
                [CUSTOM_CYCLES] = cycles.ToString(CultureInfo.InvariantCulture),
                [CUSTOM_CYCLE_SECONDS] = (stopwatch.Elapsed.TotalSeconds / Math.Max(1, cycles)).ToString("F3", CultureInfo.InvariantCulture),
                [CUSTOM_PARAVIEW_VERSION] = string.IsNullOrWhiteSpace(lastCycle.ParaviewVersion) ? "unknown" : lastCycle.ParaviewVersion,
                [CUSTOM_RENDER_WINDOW] = openGlWindow ?? "default"
            }
        };
    }

    private static async Task<ParaViewComposeStatus> RunCycleAsync(
        string pvpythonPath,
        IReadOnlyList<string> arguments,
        ParaViewTaskWorkspace workspace,
        string? openGlWindow,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        // Every cycle composes afresh: the composer refuses to overwrite a state, and a stale status
        // must never be read as this cycle's.
        if (File.Exists(workspace.ComposeStatusFilePath))
            File.Delete(workspace.ComposeStatusFilePath);
        if (File.Exists(workspace.StatePath))
            File.Delete(workspace.StatePath);

        var (_, status) = await ParaViewComposeExecutor.RunComposeOnceAsync(pvpythonPath, arguments, workspace, openGlWindow, logger, cancellationToken);
        return status;
    }

    #endregion
}
