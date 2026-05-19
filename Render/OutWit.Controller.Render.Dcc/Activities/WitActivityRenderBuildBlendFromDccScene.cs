using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Activities;

/// <summary>
/// Host-side neutral DCC scene build activity.
/// Current implementation validates the neutral DCC contract and reserves the build boundary for future .blend generation.
/// </summary>
[Activity("Render.BuildBlendFromDccScene")]
[RequiresResources(RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityRenderBuildBlendFromDccScene : WitActivityFunction
{
    #region Functions

    protected override string InnerString()
    {
        return $"Render.BuildBlendFromDccScene({Scene})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderBuildBlendFromDccScene && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderBuildBlendFromDccScene InnerClone()
    {
        return new WitActivityRenderBuildBlendFromDccScene
        {
            Scene = Scene
        };
    }

    #endregion

    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Scene { get; init; }

    #endregion
}
