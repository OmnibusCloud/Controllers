using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Returns a unified preflight view across still, frame-range, tiled-still, and video render modes.
/// </summary>
[Activity("Render.Preflight")]
[CanRunInParallelOnClient(true)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[MemoryPackable]
public sealed partial class WitActivityRenderPreflight : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Frame { get; init; }

    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? StartFrame { get; init; }

    [MemoryPackOrder(2)]
    [MemoryPackAllowSerialize]
    public IWitParameter? EndFrame { get; init; }

    [MemoryPackOrder(3)]
    [MemoryPackAllowSerialize]
    public IWitParameter? TilesX { get; init; }

    [MemoryPackOrder(4)]
    [MemoryPackAllowSerialize]
    public IWitParameter? TilesY { get; init; }

    [MemoryPackOrder(5)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    [MemoryPackOrder(6)]
    [MemoryPackAllowSerialize]
    public IWitParameter? TileOptions { get; init; }

    [MemoryPackOrder(7)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Video { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.Preflight({Frame}, {StartFrame}, {EndFrame}, {TilesX}, {TilesY}, {Options}, {TileOptions}, {Video})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderPreflight && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderPreflight InnerClone()
    {
        return new WitActivityRenderPreflight
        {
            Frame = Frame,
            StartFrame = StartFrame,
            EndFrame = EndFrame,
            TilesX = TilesX,
            TilesY = TilesY,
            Options = Options,
            TileOptions = TileOptions,
            Video = Video
        };
    }

    #endregion
}
