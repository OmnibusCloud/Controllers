using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Controller.Render.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderFrame : WitActivityAdapterRenderFrameBase<WitActivityRenderFrame>
{
    #region Constructors

    public WitActivityAdapterRenderFrame(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        IWitTempStorage tempStorage,
        ILogger logger)
        : base(processingManager, blobService, tempStorage, logger)
    {
    }

    #endregion

    #region Properties

    protected override RenderEngine BenchmarkEngine => RenderEngine.Cycles;

    protected override string FrameBenchmarkDatasetId => RenderBenchmarkHelper.STILL_BENCHMARK_SCENE_DATASET;

    protected override bool RequiresMatchingTaskEngine => false;

    #endregion
}
