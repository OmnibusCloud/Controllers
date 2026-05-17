using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Dcc.Activities;

/// <summary>
/// Host-side cleanup activity that removes the source neutral DCC scene variable from the pool.
/// </summary>
[Activity("Render.ClearScene")]
[MemoryPackable]
public sealed partial class WitActivityRenderClearScene : WitActivityCommand
{
    #region Functions

    protected override string InnerString()
    {
        return $"Render.ClearScene({Scene})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not WitActivityRenderClearScene other)
            return false;

        return base.Is(modelBase, tolerance)
               && ValueUtils.Check(Scene, other.Scene);
    }

    public override WitActivityRenderClearScene Clone()
    {
        return new WitActivityRenderClearScene
        {
            Scene = Scene?.Clone() as IWitParameter
        };
    }

    #endregion

    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Scene { get; init; }

    #endregion
}
