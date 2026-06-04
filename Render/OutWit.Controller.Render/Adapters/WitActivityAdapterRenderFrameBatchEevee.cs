using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderFrameBatchEevee : WitActivityAdapterRenderFrameBatchBase<WitActivityRenderFrameBatchEevee>
{
    public WitActivityAdapterRenderFrameBatchEevee(
        IWitProcessingManager processingManager,
        IWitBlobService blobService,
        IWitTempStorage tempStorage,
        ILogger logger)
        : base(processingManager, blobService, tempStorage, logger)
    {
    }

    protected override RenderEngine BenchmarkEngine => RenderEngine.Eevee;
}
