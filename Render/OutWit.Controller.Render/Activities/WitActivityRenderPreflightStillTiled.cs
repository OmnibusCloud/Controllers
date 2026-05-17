using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Attributes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Render.Activities;

/// <summary>
/// Validates whether the current packaged runtime can execute a tiled still render request.
/// </summary>
[Activity("Render.PreflightStillTiled")]
[CanRunInParallelOnClient(true)]
[RequiresOs(Platform = "Windows,Linux,OSX")]
[MemoryPackable]
public sealed partial class WitActivityRenderPreflightStillTiled : WitActivityFunction
{
    #region Properties

    [MemoryPackOrder(0)]
    [MemoryPackAllowSerialize]
    public IWitParameter? TilesX { get; init; }

    [MemoryPackOrder(1)]
    [MemoryPackAllowSerialize]
    public IWitParameter? TilesY { get; init; }

    [MemoryPackOrder(2)]
    [MemoryPackAllowSerialize]
    public IWitParameter? Options { get; init; }

    [MemoryPackOrder(3)]
    [MemoryPackAllowSerialize]
    public IWitParameter? TileOptions { get; init; }

    #endregion

    #region Functions

    protected override string InnerString()
    {
        return $"Render.PreflightStillTiled({TilesX}, {TilesY}, {Options}, {TileOptions})";
    }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is WitActivityRenderPreflightStillTiled && base.Is(modelBase, tolerance);
    }

    protected override WitActivityRenderPreflightStillTiled InnerClone()
    {
        return new WitActivityRenderPreflightStillTiled
        {
            TilesX = TilesX,
            TilesY = TilesY,
            Options = Options,
            TileOptions = TileOptions
        };
    }

    #endregion
}
