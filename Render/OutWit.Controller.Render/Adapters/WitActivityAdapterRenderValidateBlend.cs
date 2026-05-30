using Microsoft.Extensions.Logging;
using System.Text.Json;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Data.ActivityAdapters;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderValidateBlend : WitActivityAdapterFunction<WitActivityRenderValidateBlend>
{
    #region Constructors

    public WitActivityAdapterRenderValidateBlend(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        IWitTempStorage tempStorage,
        ILogger logger)
        : base(processingManager, logger)
    {
        BlobService = blobService;
        TempStorage = tempStorage;
    }

    #endregion

    #region Benchmarking

    public override async global::System.Threading.Tasks.Task<IWitBenchmarkResult> RunBenchmark(
        IWitBenchmarkOptions? options,
        global::System.Threading.CancellationToken cancellationToken)
    {
        var runner = RenderBenchmarkHelper.TryCreateBlenderRunner(Logger, TempStorage);
        if (runner == null)
        {
            Logger.LogWarning("Render.ValidateBlend benchmark skipped because Blender is unavailable.");
            return RenderBenchmarkHelper.CreateUnavailableResult(
                RenderBenchmarkHelper.VALIDATE_BLEND_UNIT,
                RenderBenchmarkHelper.STILL_BENCHMARK_SCENE_DATASET);
        }

        var benchmarkBlend = RenderBenchmarkHelper.FindBenchmarkScene();
        if (benchmarkBlend == null)
        {
            Logger.LogWarning("Render.ValidateBlend benchmark skipped because the benchmark scene is missing.");
            return RenderBenchmarkHelper.CreateUnavailableResult(
                RenderBenchmarkHelper.VALIDATE_BLEND_UNIT,
                RenderBenchmarkHelper.STILL_BENCHMARK_SCENE_DATASET);
        }

        return await RenderBenchmarkHelper.MeasureAsync(
            options,
            unit: RenderBenchmarkHelper.VALIDATE_BLEND_UNIT,
            datasetId: RenderBenchmarkHelper.STILL_BENCHMARK_SCENE_DATASET,
            action: async ct =>
            {
                var isValid = await runner.ValidateBlendAsync(benchmarkBlend, ct);
                if (!isValid)
                    throw new global::System.InvalidOperationException("Benchmark scene validation failed during Render.ValidateBlend benchmark.");
            },
            cancellationToken: cancellationToken);
    }

    #endregion

    #region Functions

    protected override WitActivityRenderValidateBlend CreateActivity(IWitParameter[] parameters)
    {
        if (parameters.Length != 1)
            throw new global::System.ArgumentException($"Render.ValidateBlend expects 1 parameter, got {parameters.Length}");

        return new WitActivityRenderValidateBlend
        {
            Scene = parameters[0]
        };
    }

    protected override async global::System.Threading.Tasks.Task Process(
        WitActivityRenderValidateBlend activity,
        IWitVariablesCollection pool,
        IWitActivityStatus? activityStatus,
        WitProcessingStatus status)
    {
        if (!pool.TryGetValue(activity.Scene, out global::System.Guid sceneBlobId))
            throw new global::System.InvalidOperationException("Failed to get Blob parameter 'scene'");

        var blendPath = await BlobService.GetLocalPathAsync(sceneBlobId);
        var validation = await GetBlenderRunner().ValidateBlendDetailedAsync(blendPath, ProcessingManager.CancellationToken(status.JobId));

        if (!pool.TrySetValue(activity.ReturnReference, JsonSerializer.Serialize(validation)))
            throw new global::System.InvalidOperationException($"Failed to set return value '{activity.ReturnReference}' for Render.ValidateBlend.");
    }

    private BlenderRunner GetBlenderRunner()
    {
        var runner = RenderBenchmarkHelper.TryCreateBlenderRunner(Logger, TempStorage);
        if (runner == null)
            throw new global::System.InvalidOperationException("Blender is not available in the current render controller runtime.");

        return runner;
    }

    #endregion

    #region Properties

    private IWitBlobService BlobService { get; }

    private IWitTempStorage TempStorage { get; }

    #endregion
}
