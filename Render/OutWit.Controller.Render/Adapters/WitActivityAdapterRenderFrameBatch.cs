using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

/// <summary>Generic (engine-agnostic) persistent-batch adapter — accepts any engine, mirrors <see cref="WitActivityAdapterRenderFrame"/>.</summary>
internal sealed class WitActivityAdapterRenderFrameBatch : WitActivityAdapterRenderFrameBatchBase<WitActivityRenderFrameBatch>
{
    public WitActivityAdapterRenderFrameBatch(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        IWitTempStorage tempStorage,
        ILogger logger)
        : base(processingManager, blobService, tempStorage, logger)
    {
    }

    protected override RenderEngine BenchmarkEngine => RenderEngine.Cycles;

    protected override string FrameBenchmarkDatasetId => RenderBenchmarkHelper.STILL_BENCHMARK_SCENE_DATASET;

    protected override bool RequiresMatchingTaskEngine => false;
}
