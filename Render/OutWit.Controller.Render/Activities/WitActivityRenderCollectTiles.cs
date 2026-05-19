using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Collects tile render results and stitches them into a final still image.
/// </summary>
[Activity("Render.CollectTiles")]
[RequiresResources(RequiresLocalAccess = true)]
[MemoryPackable]
public sealed partial class WitActivityRenderCollectTiles : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Results { get; init; }

    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    [MemoryPackOrder(2)]
    [MemoryPackAllowSerialize]
    public IWitParameter? TileOptions { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.CollectTiles({Results}, {Options}, {TileOptions})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderCollectTiles && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderCollectTiles InnerClone()
    {
        return new WitActivityRenderCollectTiles
        {
            Results = Results,
            Options = Options,
            TileOptions = TileOptions
        };
    }

    #endregion
}
