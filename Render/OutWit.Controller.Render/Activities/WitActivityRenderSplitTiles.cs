using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Splits a still render into tile tasks for distributed execution.
/// </summary>
[Activity("Render.SplitTiles")]
[MemoryPackable]
public sealed partial class WitActivityRenderSplitTiles : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Scene { get; init; }

    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Frame { get; init; }

    [MemoryPackOrder(2)]
    [MemoryPackAllowSerialize]
    public IWitParameter? TilesX { get; init; }

    [MemoryPackOrder(3)]
    [MemoryPackAllowSerialize]
    public IWitParameter? TilesY { get; init; }

    [MemoryPackOrder(4)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    [MemoryPackOrder(5)]
    [MemoryPackAllowSerialize]
    public IWitParameter? TileOptions { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.SplitTiles({Scene}, {Frame}, {TilesX}, {TilesY}, {Options}, {TileOptions})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderSplitTiles && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderSplitTiles InnerClone()
    {
        return new WitActivityRenderSplitTiles
        {
            Scene = Scene,
            Frame = Frame,
            TilesX = TilesX,
            TilesY = TilesY,
            Options = Options,
            TileOptions = TileOptions
        };
    }

    #endregion
}
