using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Host-side bootstrap typed-scene build activity.
/// Current implementation uploads an inline prepared .blend payload and returns its blob id.
/// </summary>
[Activity("Render.BuildBlend")]
[MemoryPackable]
public sealed partial class WitActivityRenderBuildBlend : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Scene { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.BuildBlend({Scene})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderBuildBlend && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderBuildBlend InnerClone()
    {
        return new WitActivityRenderBuildBlend
        {
            Scene = Scene
        };
    }

    #endregion
}
