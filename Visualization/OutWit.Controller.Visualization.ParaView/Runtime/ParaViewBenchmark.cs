using Microsoft.Extensions.Logging;
using OutWit.Controller.Visualization.ParaView.Processes;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Visualization.ParaView.Runtime;

/// <summary>
/// The node benchmark of <c>ParaView.RenderFrame</c>: one pvpython process builds a procedural
/// Wavelet scene (contours, clip, slice — a representative isosurface workload) and renders frames
/// to PNG at a fixed size, re-executing the contour pipeline on every frame (one isosurface value
/// alternates between two fixed levels) exactly as a real task pays for its pipeline in every
/// process — without that, VTK's filter caching leaves only rasterization + readback in the loop and
/// a 32-core node measures nearly the same as a 2-core one. The rate is output pixels per second —
/// the unit the activity's work estimate (pixels + bytes / 64) is expressed in — so the grid
/// allocator can weigh a fast workstation against a small VM instead of treating every node as 1.0.
/// </summary>
public static class ParaViewBenchmark
{
    #region Constants

    /// <summary>Rate unit: output pixels per second of the procedural benchmark scene.</summary>
    public const string UNIT = "paraview-pixels@v1";

    /// <summary>Identifies the measurement procedure; v2 re-executes the pipeline every frame (v1 rates are not comparable).</summary>
    public const string DATASET_ID = "paraview-benchmark-wavelet@v2";

    /// <summary>Embedded resource name of the benchmark runner.</summary>
    public const string RUNNER_RESOURCE = "runner/benchmark_frames.py";

    /// <summary>File name of the benchmark runner inside the benchmark workspace.</summary>
    public const string RUNNER_FILE_NAME = "benchmark_frames.py";

    /// <summary>Task document file name.</summary>
    public const string TASK_FILE_NAME = "benchmark.json";

    /// <summary>Status document file name.</summary>
    public const string STATUS_FILE_NAME = "benchmark_status.json";

    /// <summary>Square frame size of the benchmark scene.</summary>
    public const int RESOLUTION = 512;

    /// <summary>Wavelet half-extent: (2n+1)^3 points — 61^3 ≈ 227k points, contoured four times.</summary>
    public const int WAVELET_EXTENT = 30;

    /// <summary>Frames rendered before timing starts (pipeline execution, GL context, first readback).</summary>
    public const int WARMUP_FRAMES = 1;

    /// <summary>Upper bound on timed frames: a node faster than 25 ms/frame stops here rather than at the target duration.</summary>
    public const int MAX_FRAMES = 120;

    /// <summary>Timed duration when the engine passes no positive MinDuration.</summary>
    public static readonly TimeSpan FALLBACK_TARGET = TimeSpan.FromSeconds(3);

    /// <summary>Whole-process wall-clock limit (startup + scene + warm-up + timed loop).</summary>
    public static readonly TimeSpan WALL_CLOCK_LIMIT = TimeSpan.FromMinutes(5);

    /// <summary>Custom result keys (mirrors the Render controller's vocabulary).</summary>
    public const string CUSTOM_RENDER_WINDOW = "render-window";
    public const string CUSTOM_RENDER_DEVICE = "render-device";
    public const string CUSTOM_RENDER_RESOLUTION = "render-resolution";
    public const string CUSTOM_RENDER_FRAMES = "render-frames";
    public const string CUSTOM_RENDER_SECONDS = "render-seconds";
    public const string CUSTOM_PARAVIEW_VERSION = "paraview-version";
    public const string CUSTOM_SCENE_POINTS = "scene-points";

    private const string SOFTWARE_RENDER_WINDOW = "vtkOSOpenGLRenderWindow";

    #endregion

    #region Functions

    /// <summary>
    /// Runs the benchmark through the resolved pvpython and converts the status document into the
    /// engine's benchmark result.
    /// </summary>
    /// <param name="pvpythonPath">Resolved pvpython executable.</param>
    /// <param name="tempStorage">Node temp storage the benchmark workspace is created under.</param>
    /// <param name="options">Engine benchmark options (MinDuration → timed seconds, WarmupIterations → warm-up frames) or null for defaults.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="cancellationToken">Kills the runner process tree when signaled.</param>
    /// <returns>The measured rate (pixels/second) with the run metadata in <see cref="WitBenchmarkResult.Custom"/>.</returns>
    /// <exception cref="InvalidOperationException">pvpython is missing, or the runner failed, timed out, or wrote no usable status.</exception>
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
        var warmupFrames = Math.Max(WARMUP_FRAMES, benchmarkOptions.WarmupIterations);

        using var workspace = ParaViewTaskWorkspace.Create(tempStorage, Guid.NewGuid(), 0);

        var runnerPath = workspace.WriteEmbedded(RUNNER_RESOURCE, workspace.RunnerDirectory, RUNNER_FILE_NAME);
        var taskFilePath = Path.Combine(workspace.Root, TASK_FILE_NAME);
        var statusFilePath = Path.Combine(workspace.Root, STATUS_FILE_NAME);

        await File.WriteAllTextAsync(taskFilePath, BuildTaskJson(workspace.OutputDirectory, statusFilePath, warmupFrames, target), cancellationToken);

        var arguments = BuildArguments(runnerPath, taskFilePath);
        var environment = ParaViewRunnerEnvironment.Build(
            pvpythonPath, workspace.HomeDirectory, workspace.TempDirectory, ParaViewRunnerEnvironment.ForceSoftwareRenderingByDefault());

        var outcome = await ParaViewProcessRunner.RunAsync(
            pvpythonPath, arguments, workspace.PackageRoot, environment, WALL_CLOCK_LIMIT, logger, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        var data = ParaViewBenchmarkRunData.TryRead(statusFilePath);
        if (outcome.TimedOut)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: the runner exceeded its {WALL_CLOCK_LIMIT.TotalMinutes:0}-minute wall-clock limit and was terminated.{Describe(data, outcome)}");

        if (outcome.ExitCode != 0)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: the runner exited with code {outcome.ExitCode}.{Describe(data, outcome)}");

        if (data == null)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: the runner exited successfully but wrote no status document.{Describe(null, outcome)}");

        if (!data.Ok)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: the runner reported a failure at stage '{data.Stage}': {data.Error}{Describe(null, outcome)}");

        if (data.ComputeRate() <= 0)
            throw new InvalidOperationException($"ParaView.RenderFrame benchmark: the runner rendered {data.Frames} frame(s) in {data.RenderSeconds:F3} s — nothing to measure.{Describe(null, outcome)}");

        return ToResult(data);
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
    /// Converts a benchmark run into the engine's benchmark result.
    /// </summary>
    /// <param name="data">A successful run.</param>
    /// <returns>The result (rate in pixels/second, metadata in Custom).</returns>
    public static WitBenchmarkResult ToResult(ParaViewBenchmarkRunData data)
    {
        return new WitBenchmarkResult
        {
            Rate = data.ComputeRate(),
            Unit = UNIT,
            Elapsed = TimeSpan.FromSeconds(data.RenderSeconds),
            Iterations = data.Frames,
            DatasetId = DATASET_ID,
            Custom = BuildCustom(data)
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
    /// The task document the benchmark runner reads.
    /// </summary>
    /// <param name="outputDirectory">Where the benchmark frame is written.</param>
    /// <param name="statusFilePath">Where the runner writes its status.</param>
    /// <param name="warmupFrames">Untimed warm-up frames.</param>
    /// <param name="target">Timed duration.</param>
    /// <returns>JSON text.</returns>
    public static string BuildTaskJson(string outputDirectory, string statusFilePath, int warmupFrames, TimeSpan target)
    {
        var document = new Dictionary<string, object>
        {
            ["width"] = RESOLUTION,
            ["height"] = RESOLUTION,
            ["warmup_frames"] = warmupFrames,
            ["target_seconds"] = target.TotalSeconds,
            ["max_frames"] = MAX_FRAMES,
            ["extent"] = WAVELET_EXTENT,
            ["output_dir"] = outputDirectory,
            ["status_path"] = statusFilePath
        };

        return System.Text.Json.JsonSerializer.Serialize(document);
    }

    #endregion

    #region Tools

    private static IReadOnlyDictionary<string, string> BuildCustom(ParaViewBenchmarkRunData data)
    {
        var renderWindow = string.IsNullOrWhiteSpace(data.RenderWindow) ? "unknown" : data.RenderWindow;
        var device = string.Equals(renderWindow, SOFTWARE_RENDER_WINDOW, StringComparison.Ordinal) ? "CPU" : "GPU";

        return new Dictionary<string, string>
        {
            [CUSTOM_RENDER_WINDOW] = renderWindow,
            [CUSTOM_RENDER_DEVICE] = device,
            [CUSTOM_RENDER_RESOLUTION] = $"{data.Width}x{data.Height}",
            [CUSTOM_RENDER_FRAMES] = data.Frames.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [CUSTOM_RENDER_SECONDS] = data.RenderSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            [CUSTOM_PARAVIEW_VERSION] = string.IsNullOrWhiteSpace(data.ParaviewVersion) ? "unknown" : data.ParaviewVersion,
            [CUSTOM_SCENE_POINTS] = data.Points.ToString(System.Globalization.CultureInfo.InvariantCulture)
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
