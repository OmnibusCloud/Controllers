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

    public const string STILL_BENCHMARK_SCENE_DATASET = "benchmark-still@v1";
    public const string STILL_BENCHMARK_SCENE_CYCLES_DATASET = "benchmark-still-cycles@v1";
    public const string STILL_BENCHMARK_SCENE_EEVEE_DATASET = "benchmark-still-eevee@v1";
    public const string STILL_BENCHMARK_SCENE_GREASE_PENCIL_DATASET = "benchmark-still-grease-pencil@v1";
    public const string VIDEO_BENCHMARK_SCENE_DATASET = "benchmark-video@v1";
    public const string RUNTIME_DIAGNOSTICS_DATASET = "runtime-diagnostics@v1";
    public const string PREFLIGHT_FRAMES_DATASET = "preflight-frames@v1";
    public const string PREFLIGHT_STILL_TILED_DATASET = "tiled-still@v1";
    public const string PREFLIGHT_VIDEO_DATASET = "preflight-video@v1";
    public const string PREFLIGHT_DATASET = "preflight-unified@v1";

    private const int BENCHMARK_RESOLUTION = 128;
    private const int BENCHMARK_SAMPLES = 8;
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

    public static BlenderRunner? TryCreateBlenderRunner(ILogger logger)
    {
        var controllerAssemblyPath = typeof(WitControllerRenderModule).Assembly.Location;
        var blenderDir = RenderBinaryResolver.ResolveBlenderRoot(controllerAssemblyPath);
        var runner = new BlenderRunner(blenderDir, logger);
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
