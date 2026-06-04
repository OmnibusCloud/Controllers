using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderFrameBatchCycles : WitActivityAdapterRenderFrameBatchBase<WitActivityRenderFrameBatchCycles>
{
    public WitActivityAdapterRenderFrameBatchCycles(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        IWitTempStorage tempStorage,
        ILogger logger)
        : base(processingManager, blobService, tempStorage, logger)
    {
    }

    protected override RenderEngine BenchmarkEngine => RenderEngine.Cycles;
}
