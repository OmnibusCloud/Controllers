using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Utils;

internal static class RenderBenchmarkHelper
{
    #region Constants

    private const int DEFAULT_MAX_BENCHMARK_ITERATIONS = 16;

    public const string BLENDER_VERSION_UNIT = "version-checks@v1";
    public const string FRAME_UNIT = "render-pixels@v1";
    public const string RUNTIME_DIAGNOSTICS_UNIT = "runtime-diagnostics@v1";
    public const string VALIDATE_BLEND_UNIT = "blend-validations@v1";
    public const string PREFLIGHT_FRAMES_UNIT = "frame-preflights@v1";
    public const string PREFLIGHT_STILL_TILED_UNIT = "tiled-preflights@v1";
    public const string PREFLIGHT_VIDEO_UNIT = "video-preflights@v1";
    public const string PREFLIGHT_UNIT = "unified-preflights@v1";

    // @v2 = the heavier 512px / 4×4-grid render-benchmark scene (see BENCHMARK_RENDER_* below);
    // bumped from @v1 so a v2-scene rate is never mislabeled as a v1 rate after this controller ships.
    public const string STILL_BENCHMARK_SCENE_DATASET = "benchmark-still@v2";
    public const string STILL_BENCHMARK_SCENE_CYCLES_DATASET = "benchmark-still-cycles@v2";
    public const string STILL_BENCHMARK_SCENE_EEVEE_DATASET = "benchmark-still-eevee@v2";
    public const string STILL_BENCHMARK_SCENE_GREASE_PENCIL_DATASET = "benchmark-still-grease-pencil@v2";
    public const string VIDEO_BENCHMARK_SCENE_DATASET = "benchmark-video@v1";
    public const string RUNTIME_DIAGNOSTICS_DATASET = "runtime-diagnostics@v1";
    public const string PREFLIGHT_FRAMES_DATASET = "preflight-frames@v1";
    public const string PREFLIGHT_STILL_TILED_DATASET = "tiled-still@v1";
    public const string PREFLIGHT_VIDEO_DATASET = "preflight-video@v1";
    public const string PREFLIGHT_DATASET = "preflight-unified@v1";

    private const int BENCHMARK_RESOLUTION = 128;
    private const int BENCHMARK_SAMPLES = 8;

    // Node render-benchmark calibration.
    //
    // v1 (256px @ 128 spp, 3×3 grid) was calibrated only on RTX 3080 Ti + Ryzen 9 5950X vs an
    // Ubuntu RTX 1080 Ti — there it ordered discrete GPUs correctly (3080 Ti > 1080 Ti) and beat
    // CPU. But once an Apple M4 (integrated, unified-memory) joined, the scene proved too LIGHT to
    // separate integrated from discrete: the Cycles rate INVERTED vs reality (bench Mac 11.58M >
    // Linux 8.35M, yet a real Cycles lava video ran ~17% FASTER on the 1080 Ti). A discrete GPU's
    // per-frame FIXED cost (PCIe scene upload + kernel launch + BVH build) is what 256px fails to
    // amortize, so the M4's no-PCIe unified path over-scores. v2 raises the per-frame compute so
    // the discrete GPU's raw throughput + memory bandwidth dominate that fixed cost:
    //   • resolution 256 → 512  (4× pixels → saturates the GPU, amortizes launch/upload)
    //   • grid       3   → 4    (9 → 16 subdiv-4 icospheres → heavier BVH, memory-bandwidth signal)
    //   • samples/bounces unchanged (don't compound weak-node cost on both axes at once)
    // The still-scene dataset ids are bumped @v1 → @v2 so a v2-scene rate is never mislabeled
    // as a v1 rate. NOTE: the allocator weights by Rate alone (it does not gate on DatasetId), and
    // ComputeRate scales with resX·resY, so a v2 node reports a ~4× larger absolute Rate than a v1
    // node for the same hardware — v1 and v2 nodes must therefore not be mixed in one job (update
    // all clients together). Same-version ratios are unaffected, which is the property Grid needs.
    // Weak CPU-only nodes pay ~4× on their single Cycles benchmark frame (the adaptive loop still
    // renders ≥1) — acceptable for a one-time probe.
    // PENDING: re-verify the Mac/Linux/Windows ordering on the 3-machine cluster after deploy;
    // tune these constants + rebuild if the integrated/discrete order is still off.
    private const int BENCHMARK_RENDER_RESOLUTION = 512;
    private const int BENCHMARK_RENDER_SAMPLES = 128;
    private const int BENCHMARK_RENDER_GRID = 4;
    private const int BENCHMARK_RENDER_MAX_BOUNCES = 8;
    private const int BENCHMARK_RENDER_WARMUP_FRAMES = 1;
    private const int BENCHMARK_RENDER_MAX_FRAMES = 24;
    // Raised 1.5 → 3.0 s: a 512px frame is ~4× heavier, so a strong GPU would otherwise complete
    // only 1 frame inside a 1.5 s budget. 3 s lets fast nodes average over a few frames; weak nodes
    // still break after their first frame (already past budget), so their cost is unchanged.
    private static readonly TimeSpan BENCHMARK_RENDER_FALLBACK_TARGET = TimeSpan.FromSeconds(3.0);

    public const string CUSTOM_RENDER_DEVICE = "render-device";
    public const string CUSTOM_RENDER_BACKEND = "render-backend";
    public const string CUSTOM_AVAILABLE_BACKENDS = "available-backends";
    public const string CUSTOM_RENDER_RESOLUTION = "render-resolution";
    public const string CUSTOM_RENDER_SAMPLES = "render-samples";
    public const string CUSTOM_RENDER_FRAMES = "render-frames";
    public const string CUSTOM_RENDER_SECONDS = "render-seconds";

    private const int BENCHMARK_STILL_FRAME = 1;
    private const int BENCHMARK_VIDEO_START_FRAME = 1;
    private const int BENCHMARK_VIDEO_END_FRAME = 16;
    private const int BENCHMARK_TILES_X = 3;
    private const int BENCHMARK_TILES_Y = 2;
    private const int BENCHMARK_TILE_OVERLAP = 12;
    private const int BENCHMARK_VIDEO_FRAME_RATE = 24;
    private const int BENCHMARK_VIDEO_CONSTANT_RATE_FACTOR = 23;

    #endregion

    #region Functions

    public static BlenderRunner? TryCreateBlenderRunner(ILogger logger, IWitTempStorage? tempStorage = null)
    {
        var controllerAssemblyPath = typeof(WitControllerRenderModule).Assembly.Location;
        var blenderDir = RenderBinaryResolver.ResolveBlenderRoot(controllerAssemblyPath);
        var runner = new BlenderRunner(blenderDir, logger, tempStorage);
        return runner.IsAvailable ? runner : null;
    }

    public static string? FindBenchmarkScene()
    {
        return FindStillBenchmarkScene();
    }

    public static string? FindStillBenchmarkScene()
    {
        return FindFirstExistingPath(
            "benchmark_scene_still.blend",
            "benchmark_scene.blend");
    }

    public static string? FindVideoBenchmarkScene()
    {
        return FindFirstExistingPath("benchmark_scene_video.blend");
    }

    public static RenderOptionsData CreateBenchmarkRenderOptions()
    {
        return CreateBenchmarkRenderOptions(RenderEngine.Cycles);
    }

    public static RenderOptionsData CreateBenchmarkRenderOptions(RenderEngine engine)
    {
        return new RenderOptionsData
        {
            Format = RenderFormat.PNG,
            Engine = engine,
            Samples = BENCHMARK_SAMPLES,
            ResolutionX = BENCHMARK_RESOLUTION,
            ResolutionY = BENCHMARK_RESOLUTION,
            Denoise = false
        };
    }

    public static string GetFrameBenchmarkDatasetId(RenderEngine engine)
    {
        return engine switch
        {
            RenderEngine.Cycles => STILL_BENCHMARK_SCENE_CYCLES_DATASET,
            RenderEngine.Eevee => STILL_BENCHMARK_SCENE_EEVEE_DATASET,
            RenderEngine.GreasePencil => STILL_BENCHMARK_SCENE_GREASE_PENCIL_DATASET,
            _ => STILL_BENCHMARK_SCENE_DATASET
        };
    }

    public static TileOptionsData CreateBenchmarkTileOptions()
    {
        return new TileOptionsData
        {
            OverlapPx = BENCHMARK_TILE_OVERLAP,
            BlendMode = TileBlendMode.CenterPriorityCrop
        };
    }

    public static int BenchmarkTilesX => BENCHMARK_TILES_X;

    public static int BenchmarkTilesY => BENCHMARK_TILES_Y;

    public static int BenchmarkStillFrame => BENCHMARK_STILL_FRAME;

    public static int BenchmarkVideoStartFrame => BENCHMARK_VIDEO_START_FRAME;

    public static int BenchmarkVideoEndFrame => BENCHMARK_VIDEO_END_FRAME;

    public static VideoOptionsData CreateBenchmarkVideoOptions()
    {
        return new VideoOptionsData
        {
            FrameRate = BENCHMARK_VIDEO_FRAME_RATE,
            ConstantRateFactor = BENCHMARK_VIDEO_CONSTANT_RATE_FACTOR
        };
    }

    public static async Task<WitBenchmarkResult> MeasureAsync(
        IWitBenchmarkOptions? options,
        string unit,
        string? datasetId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        Func<long, TimeSpan, double>? rateFactory = null,
        int? maxIterations = null)
    {
        IWitBenchmarkOptions benchmarkOptions = options ?? WitBenchmarkOptions.Default;
        var warmupIterations = Math.Max(0, benchmarkOptions.WarmupIterations);
        var targetDuration = benchmarkOptions.MinDuration < TimeSpan.Zero
            ? TimeSpan.Zero
            : benchmarkOptions.MinDuration;
        var iterationLimit = Math.Max(1, maxIterations ?? DEFAULT_MAX_BENCHMARK_ITERATIONS);

        for (var index = 0; index < warmupIterations; index++)
            await action(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        long iterations = 0;

        while (iterations < iterationLimit)
        {
            await action(cancellationToken);
            iterations++;

            if (stopwatch.Elapsed >= targetDuration)
                break;
        }

        stopwatch.Stop();
        var rate = rateFactory?.Invoke(iterations, stopwatch.Elapsed)
            ?? CalculateOperationsPerSecond(iterations, stopwatch.Elapsed);

        return new WitBenchmarkResult
        {
            Rate = rate,
            Unit = unit,
            Elapsed = stopwatch.Elapsed,
            Iterations = iterations,
            DatasetId = datasetId
        };
    }

    /// <summary>
    /// Runs the redesigned node render benchmark: one Blender process renders a procedural
    /// compute-bound scene with in-process render-only timing (see <see cref="BlenderBenchmarkScript"/>),
    /// so the resulting <see cref="WitBenchmarkResult.Rate"/> is real render throughput
    /// (pixel-samples/second) rather than Blender startup speed. The device actually used is
    /// recorded in <see cref="WitBenchmarkResult.Custom"/>.
    /// </summary>
    public static async Task<WitBenchmarkResult> MeasureRenderAsync(
        BlenderRunner runner,
        RenderEngine engine,
        IWitBenchmarkOptions? options,
        string unit,
        string? datasetId,
        CancellationToken cancellationToken)
    {
        IWitBenchmarkOptions benchmarkOptions = options ?? WitBenchmarkOptions.Default;
        var target = benchmarkOptions.MinDuration <= TimeSpan.Zero
            ? BENCHMARK_RENDER_FALLBACK_TARGET
            : benchmarkOptions.MinDuration;
        var warmupFrames = Math.Max(BENCHMARK_RENDER_WARMUP_FRAMES, benchmarkOptions.WarmupIterations);

        var data = await runner.RunBenchmarkRenderAsync(
            engine,
            samples: BENCHMARK_RENDER_SAMPLES,
            resolution: BENCHMARK_RENDER_RESOLUTION,
            gridSize: BENCHMARK_RENDER_GRID,
            maxBounces: BENCHMARK_RENDER_MAX_BOUNCES,
            warmupFrames: warmupFrames,
            targetSeconds: target.TotalSeconds,
            maxFrames: BENCHMARK_RENDER_MAX_FRAMES,
            cancellationToken);

        return new WitBenchmarkResult
        {
            Rate = data.ComputeRate(),
            Unit = unit,
            Elapsed = TimeSpan.FromSeconds(data.RenderSeconds),
            Iterations = data.FramesRendered,
            DatasetId = datasetId,
            Custom = BuildRenderCustom(data)
        };
    }

    public static WitBenchmarkResult CreateUnavailableResult(string unit, string? datasetId)
    {
        return new WitBenchmarkResult
        {
            Rate = 0,
            Unit = unit,
            Elapsed = TimeSpan.Zero,
            Iterations = 0,
            DatasetId = datasetId
        };
    }

    private static IReadOnlyDictionary<string, string> BuildRenderCustom(RenderBenchmarkRunData data)
    {
        // Cycles reports a concrete compute backend (GPU/CPU). Eevee / Grease Pencil are GPU
        // rasterizers with no Cycles-style device split, so label them GPU.
        var device = data.UsesGpu || data.Engine != RenderEngine.Cycles ? "GPU" : "CPU";

        return new Dictionary<string, string>
        {
            [CUSTOM_RENDER_DEVICE] = device,
            [CUSTOM_RENDER_BACKEND] = data.SelectedRenderBackend?.ToString()
                ?? (string.IsNullOrWhiteSpace(data.RawBackend) ? "unknown" : data.RawBackend!),
            [CUSTOM_AVAILABLE_BACKENDS] = data.AvailableRenderBackends.Length > 0
                ? string.Join(",", data.AvailableRenderBackends)
                : "none",
            [CUSTOM_RENDER_RESOLUTION] = $"{data.ResolutionX}x{data.ResolutionY}",
            [CUSTOM_RENDER_SAMPLES] = data.Samples.ToString(),
            [CUSTOM_RENDER_FRAMES] = data.FramesRendered.ToString(),
            [CUSTOM_RENDER_SECONDS] = data.RenderSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static string? FindFirstExistingPath(params string[] fileNames)
    {
        var controllerAssemblyPath = typeof(WitControllerRenderModule).Assembly.Location;
        var controllerDir = Path.GetDirectoryName(controllerAssemblyPath) ?? AppContext.BaseDirectory;

        foreach (var root in EnumerateBenchmarkRoots(controllerDir))
        {
            foreach (var fileName in fileNames)
            {
                var directPath = Path.Combine(root, fileName);
                if (File.Exists(directPath))
                    return directPath;

                var nestedPath = Path.Combine(root, "benchmarks", fileName);
                if (File.Exists(nestedPath))
                    return nestedPath;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateBenchmarkRoots(string controllerDir)
    {
        var roots = new List<string>
        {
            controllerDir,
            Path.Combine(controllerDir, "benchmarks")
        };

        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            roots.Add(Path.Combine(dir, "@Prerequisites", "benchmark", "render"));
            roots.Add(Path.Combine(dir, "@Controllers", "Debug", "render.module"));
            dir = Path.GetDirectoryName(dir);
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static double CalculateOperationsPerSecond(long iterations, TimeSpan elapsed)
    {
        return elapsed.TotalSeconds > 0
            ? iterations / elapsed.TotalSeconds
            : 0;
    }

    #endregion
}
