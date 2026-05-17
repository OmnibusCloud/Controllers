using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Host-side bootstrap blob-backed typed-scene build activity.
/// Current implementation validates an existing .blend blob reference and returns it unchanged.
/// </summary>
[Activity("Render.BuildBlendFromRefs")]
[MemoryPackable]
public sealed partial class WitActivityRenderBuildBlendFromRefs : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Scene { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.BuildBlendFromRefs({Scene})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderBuildBlendFromRefs && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderBuildBlendFromRefs InnerClone()
    {
        return new WitActivityRenderBuildBlendFromRefs
        {
            Scene = Scene
        };
    }

    #endregion
}
