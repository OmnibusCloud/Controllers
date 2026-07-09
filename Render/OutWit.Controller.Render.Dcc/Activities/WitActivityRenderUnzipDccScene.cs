using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Activities;

/// <summary>
/// Host-side decompression of a gzip-packed neutral DCC scene payload.
/// Scene payloads compress roughly 6-10x (deformation-heavy characters go 40 MB → 7 MB),
/// so initiators upload the gzipped MemoryPack bytes and the *Packed job scripts expand
/// them here before the build activity.
/// </summary>
[Activity("Render.UnzipDccScene")]
[RequiresResources(RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityRenderUnzipDccScene : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"Render.UnzipDccScene({Packed})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderUnzipDccScene && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderUnzipDccScene InnerClone()
    {
        return new WitActivityRenderUnzipDccScene
        {
            Packed = Packed
        };
    }

    #endregion

    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Packed { get; init; }

    #endregion
}
