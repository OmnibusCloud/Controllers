using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Encodes an ordered frame sequence into a final MP4 video using ffmpeg.
/// Host-side activity for the first production RenderVideo path.
/// </summary>
[Activity("Render.EncodeVideo")]
[RequiresResources(RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityRenderEncodeVideo : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Frames { get; init; }

    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.EncodeVideo({Frames}, {Options})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderEncodeVideo && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderEncodeVideo InnerClone()
    {
        return new WitActivityRenderEncodeVideo
        {
            Frames = Frames,
            Options = Options
        };
    }

    #endregion
}
