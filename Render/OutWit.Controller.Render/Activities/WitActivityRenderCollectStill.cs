using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Collects a single still-frame render result into one final blob.
/// Host-side activity intended for the public still script surface.
/// </summary>
[Activity("Render.CollectStill")]
[MemoryPackable]
public sealed partial class WitActivityRenderCollectStill : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Results { get; init; }

    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.CollectStill({Results}, {Options})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderCollectStill && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderCollectStill InnerClone()
    {
        return new WitActivityRenderCollectStill
        {
            Results = Results,
            Options = Options
        };
    }

    #endregion
}
