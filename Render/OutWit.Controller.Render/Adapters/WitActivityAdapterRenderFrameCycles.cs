using Microsoft.Extensions.Logging;
using OutWit.Controller.Render.Activities;
using OutWit.Controller.Render.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Adapters;

internal sealed class WitActivityAdapterRenderFrameCycles : WitActivityAdapterRenderFrameBase<WitActivityRenderFrameCycles>
{
    #region Constructors

    public WitActivityAdapterRenderFrameCycles(
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

    #endregion
}
