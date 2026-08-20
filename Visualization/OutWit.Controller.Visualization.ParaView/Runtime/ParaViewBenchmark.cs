using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Processes;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The node benchmark of <c>ParaView.RenderFrame</c>: N complete task cycles, each one a FRESH
/// pvpython process that builds the procedural Wavelet scene (contours, clip, slice) and renders one
/// 1920×1080 PNG — exactly the shape of a production task, where every frame pays process startup,
/// scene/pipeline execution, render and readback. The rate is output pixels per second of the whole
/// cycle. Measuring only a steady-state render loop (the v2 dataset) overrated GPU nodes badly: a
/// real small-frame task is startup-dominated (~2.5 s of a ~3 s cycle), which a per-frame loop never
/// sees — the fleet's software-GL Linux node ran tasks at 70% of the GPU workstation's speed while
/// its v2 rate said 45%.
/// </summary>
public static class ParaViewBenchmark
{
    #region Constants

    /// <summary>Rate unit: output pixels per second of complete task cycles.</summary>
    public const string UNIT = "paraview-pixels@v1";

    /// <summary>Identifies the measurement procedure; v3 measures whole task cycles — fresh process per frame (v1/v2 rates are not comparable).</summary>
    public const string DATASET_ID = "paraview-benchmark-wavelet@v3";

    /// <summary>Embedded resource name of the benchmark runner.</summary>
    public const string RUNNER_RESOURCE = "runner/benchmark_frames.py";

    /// <summary>File name of the benchmark runner inside the benchmark workspace.</summary>
    public const string RUNNER_FILE_NAME = "benchmark_frames.py";

    /// <summary>Task document file name.</summary>
    public const string TASK_FILE_NAME = "benchmark.json";

    /// <summary>Status document file name.</summary>
    public const string STATUS_FILE_NAME = "benchmark_status.json";

    /// <summary>Frame width of one benchmark cycle (a representative production output).</summary>
    public const int CYCLE_WIDTH = 1920;

    /// <summary>Frame height of one benchmark cycle.</summary>
    public const int CYCLE_HEIGHT = 1080;

    /// <summary>Wavelet half-extent: (2n+1)^3 points — 61^3 ≈ 227k points, contoured four times.</summary>
    public const int WAVELET_EXTENT = 30;

    /// <summary>Untimed cycles before measurement: the first pvpython launch on a cold page cache costs ~3× a warm one.</summary>
    public const int WARMUP_CYCLES = 1;

    /// <summary>Timed cycles never stop before this many — one ~3 s cycle is too noisy a sample.</summary>
    public const int MIN_CYCLES = 2;

    /// <summary>Upper bound on timed cycles so the benchmark stays a ~10 s affair even with a long MinDuration.</summary>
    public const int MAX_CYCLES = 8;

    /// <summary>Timed duration when the engine passes no positive MinDuration.</summary>
    public static readonly TimeSpan FALLBACK_TARGET = TimeSpan.FromSeconds(5);

    /// <summary>Wall-clock limit of ONE cycle (startup + scene + one frame).</summary>
    public static readonly TimeSpan CYCLE_WALL_CLOCK_LIMIT = TimeSpan.FromMinutes(5);

    /// <summary>Custom result keys (mirrors the Render controller's vocabulary).</summary>
    public const string CUSTOM_RENDER_WINDOW = "render-window";
    public const string CUSTOM_RENDER_DEVICE = "render-device";
    public const string CUSTOM_RENDER_RESOLUTION = "render-resolution";
    public const string CUSTOM_CYCLES = "task-cycles";
    public const string CUSTOM_CYCLE_SECONDS = "cycle-seconds";
    public const string CUSTOM_RENDER_SECONDS = "render-seconds";
    public const string CUSTOM_PARAVIEW_VERSION = "paraview-version";
    public const string CUSTOM_SCENE_POINTS = "scene-points";

    private const string SOFTWARE_RENDER_WINDOW = "vtkOSOpenGLRenderWindow";

    #endregion

    #region Functions

    /// <summary>
    /// Runs the benchmark through the resolved pvpython: warm-up cycle(s), then timed full task
    /// cycles until the target duration (and at least <see cref="MIN_CYCLES"/>) or <see cref="MAX_CYCLES"/>.
    /// </summary>
    /// <param name="pvpythonPath">Resolved pvpython executable.</param>
    /// <param name="tempStorage">Node temp storage the benchmark workspace is created under.</param>
    /// <param name="options">Engine benchmark options (MinDuration → timed seconds, WarmupIterations → warm-up cycles) or null for defaults.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="cancellationToken">Kills the runner process tree when signaled.</param>
    /// <returns>The measured rate (pixels/second of complete cycles) with the run metadata in <see cref="WitBenchmarkResult.Custom"/>.</returns>
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
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: pvpython '{pvpythonPath}' does not exist.");

        IWitBenchmarkOptions benchmarkOptions = options ?? WitBenchmarkOptions.Default;
        var target = benchmarkOptions.MinDuration <= TimeSpan.Zero
            ? FALLBACK_TARGET
            : benchmarkOptions.MinDuration;
        var warmupCycles = Math.Max(WARMUP_CYCLES, benchmarkOptions.WarmupIterations);

        using var workspace = ParaViewTaskWorkspace.Create(tempStorage, Guid.NewGuid(), 0);

        var runnerPath = workspace.WriteEmbedded(RUNNER_RESOURCE, workspace.RunnerDirectory, RUNNER_FILE_NAME);
        var taskFilePath = Path.Combine(workspace.Root, TASK_FILE_NAME);
        var statusFilePath = Path.Combine(workspace.Root, STATUS_FILE_NAME);

        await File.WriteAllTextAsync(taskFilePath, BuildTaskJson(workspace.OutputDirectory, statusFilePath), cancellationToken);

        var arguments = BuildArguments(runnerPath, taskFilePath);
        var openGlWindow = await ParaViewRenderingBackend.ResolveWindowAsync(pvpythonPath, tempStorage, logger, cancellationToken);

        try
        {
            return await MeasureCyclesAsync(pvpythonPath, arguments, workspace, statusFilePath, openGlWindow, warmupCycles, target, logger, cancellationToken);
        }
        catch (InvalidOperationException error) when (ParaViewRenderingBackend.IsEglWindow(openGlWindow))
        {
            // Same self-healing as the task path: a flaky EGL stack demotes the node and the
            // benchmark measures the backend tasks will actually use.
            logger?.LogWarning("ParaView.RenderFrame benchmark: the EGL runner failed ({Message}); demoting this node to OSMesa and re-measuring", error.Message);
            ParaViewRenderingBackend.Demote(pvpythonPath, logger);
            return await MeasureCyclesAsync(pvpythonPath, arguments, workspace, statusFilePath, ParaViewRunnerEnvironment.OSMESA_WINDOW, warmupCycles, target, logger, cancellationToken);
        }
    }

    /// <summary>
    /// The result a node reports when it has no usable ParaView runtime: rate 0 keeps the allocator
    /// from handing it work (the engine interprets 0 as "cannot run this activity").
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

    /// <summary>
    /// Converts the timed cycles into the engine's benchmark result.
    /// </summary>
    /// <param name="lastCycle">Status of the last timed cycle (render window, version, scene size).</param>
    /// <param name="cycles">Timed cycle count.</param>
    /// <param name="elapsed">Total wall time of the timed cycles.</param>
    /// <param name="renderSeconds">Sum of in-process render seconds across the timed cycles (shows the startup share).</param>
    /// <returns>The result (rate in pixels/second of complete cycles, metadata in Custom).</returns>
    public static WitBenchmarkResult ToResult(ParaViewBenchmarkRunData lastCycle, int cycles, TimeSpan elapsed, double renderSeconds)
    {
        return new WitBenchmarkResult
        {
            Rate = cycles * (double)CYCLE_WIDTH * CYCLE_HEIGHT / elapsed.TotalSeconds,
            Unit = UNIT,
            Elapsed = elapsed,
            Iterations = cycles,
            DatasetId = DATASET_ID,
            Custom = BuildCustom(lastCycle, cycles, elapsed, renderSeconds)
        };
    }

    /// <summary>
    /// The pvpython argument array: options, the benchmark runner, then its own arguments.
    /// </summary>
    /// <param name="runnerPath">Benchmark runner script path.</param>
    /// <param name="taskFilePath">Task document path.</param>
    /// <returns>The argument array.</returns>
    public static IReadOnlyList<string> BuildArguments(string runnerPath, string taskFilePath)
    {
        return new List<string>(ParaViewTaskExecutor.PVPYTHON_OPTIONS) { runnerPath, "--task-file", taskFilePath };
    }

    /// <summary>
    /// The task document of one cycle: exactly one frame, no in-process warm-up or timed loop — the
    /// cycle boundary (and the startup cost) lives in this class, not in the runner.
    /// </summary>
    /// <param name="outputDirectory">Where the benchmark frame is written.</param>
    /// <param name="statusFilePath">Where the runner writes its status.</param>
    /// <returns>JSON text.</returns>
    public static string BuildTaskJson(string outputDirectory, string statusFilePath)
    {
        var document = new Dictionary<string, object>
        {
            ["width"] = CYCLE_WIDTH,
            ["height"] = CYCLE_HEIGHT,
            ["warmup_frames"] = 0,
            ["target_seconds"] = 0.0,
            ["max_frames"] = 1,
            ["extent"] = WAVELET_EXTENT,
            ["output_dir"] = outputDirectory,
            ["status_path"] = statusFilePath
        };

        return System.Text.Json.JsonSerializer.Serialize(document);
    }

    #endregion

    #region Tools

    private static async Task<WitBenchmarkResult> MeasureCyclesAsync(
        string pvpythonPath,
        IReadOnlyList<string> arguments,
        ParaViewTaskWorkspace workspace,
        string statusFilePath,
        string? openGlWindow,
        int warmupCycles,
        TimeSpan target,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var environment = ParaViewRunnerEnvironment.Build(
            pvpythonPath, workspace.HomeDirectory, workspace.TempDirectory, openGlWindow);

        for (var index = 0; index < warmupCycles; index++)
            await RunCycleAsync(pvpythonPath, arguments, workspace, environment, statusFilePath, logger, cancellationToken);

        var cycles = 0;
        var renderSeconds = 0.0;
        ParaViewBenchmarkRunData lastCycle = null!;
        var stopwatch = Stopwatch.StartNew();

        while (cycles < MAX_CYCLES)
        {
            lastCycle = await RunCycleAsync(pvpythonPath, arguments, workspace, environment, statusFilePath, logger, cancellationToken);
            cycles++;
            renderSeconds += lastCycle.RenderSeconds;

            if (cycles >= MIN_CYCLES && stopwatch.Elapsed >= target)
                break;
        }

        stopwatch.Stop();
        if (stopwatch.Elapsed <= TimeSpan.Zero)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: {cycles} cycle(s) took no measurable time — nothing to measure.");

        return ToResult(lastCycle, cycles, stopwatch.Elapsed, renderSeconds);
    }

    private static async Task<ParaViewBenchmarkRunData> RunCycleAsync(
        string pvpythonPath,
        IReadOnlyList<string> arguments,
        ParaViewTaskWorkspace workspace,
        IReadOnlyDictionary<string, string> environment,
        string statusFilePath,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (File.Exists(statusFilePath))
            File.Delete(statusFilePath);

        var outcome = await ParaViewProcessRunner.RunAsync(
            pvpythonPath, arguments, workspace.PackageRoot, environment, CYCLE_WALL_CLOCK_LIMIT, logger, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var data = ParaViewBenchmarkRunData.TryRead(statusFilePath);
        if (outcome.TimedOut)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: a cycle exceeded its {CYCLE_WALL_CLOCK_LIMIT.TotalMinutes:0}-minute wall-clock limit and was terminated.{Describe(data, outcome)}");

        if (outcome.ExitCode != 0)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: the runner exited with code {outcome.ExitCode}.{Describe(data, outcome)}");

        if (data == null)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: the runner exited successfully but wrote no status document.{Describe(null, outcome)}");

        if (!data.Ok)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: the runner reported a failure at stage '{data.Stage}': {data.Error}{Describe(null, outcome)}");

        if (data.Frames != 1)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: a cycle rendered {data.Frames} frame(s) instead of exactly one.{Describe(null, outcome)}");

        return data;
    }

    private static IReadOnlyDictionary<string, string> BuildCustom(ParaViewBenchmarkRunData lastCycle, int cycles, TimeSpan elapsed, double renderSeconds)
    {
        var renderWindow = string.IsNullOrWhiteSpace(lastCycle.RenderWindow) ? "unknown" : lastCycle.RenderWindow;
        var device = string.Equals(renderWindow, SOFTWARE_RENDER_WINDOW, StringComparison.Ordinal) ? "CPU" : "GPU";

        return new Dictionary<string, string>
        {
            [CUSTOM_RENDER_WINDOW] = renderWindow,
            [CUSTOM_RENDER_DEVICE] = device,
            [CUSTOM_RENDER_RESOLUTION] = $"{CYCLE_WIDTH}x{CYCLE_HEIGHT}",
            [CUSTOM_CYCLES] = cycles.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [CUSTOM_CYCLE_SECONDS] = (elapsed.TotalSeconds / Math.Max(1, cycles)).ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            [CUSTOM_RENDER_SECONDS] = renderSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            [CUSTOM_PARAVIEW_VERSION] = string.IsNullOrWhiteSpace(lastCycle.ParaviewVersion) ? "unknown" : lastCycle.ParaviewVersion,
            [CUSTOM_SCENE_POINTS] = lastCycle.Points.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static string Describe(ParaViewBenchmarkRunData? data, ParaViewProcessOutcome outcome)
    {
        var parts = new List<string>();
        if (data != null)
            parts.Add($"stage={data.Stage}" + (string.IsNullOrWhiteSpace(data.Error) ? string.Empty : $"; error={data.Error}"));
        if (!string.IsNullOrWhiteSpace(outcome.StderrTail))
            parts.Add($"stderr: {outcome.StderrTail}");
        if (!string.IsNullOrWhiteSpace(outcome.StdoutTail))
            parts.Add($"stdout: {outcome.StdoutTail}");

        return parts.Count == 0 ? string.Empty : " " + string.Join(" | ", parts);
    }

    #endregion
}
