using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Tile-specific options for tiled still rendering.
/// Kept separate from <see cref="RenderOptionsData"/> because different scripts may need different tiling inputs.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only, and deploy the server before any client that writes a new member (default MemoryPack mode rejects payloads with unknown members).
public partial class TileOptionsData : ModelBase
{
    #region Properties

    /// <summary>
    /// Tile overlap in pixels.
    /// Current tiled slice uses center-priority crop when overlap is greater than zero.
    /// </summary>
    [MemoryPackOrder(0)]
    public int OverlapPx { get; set; }

    /// <summary>
    /// Stitching strategy for overlap regions.
    /// </summary>
    [MemoryPackOrder(1)]
    public TileBlendMode BlendMode { get; set; } = TileBlendMode.CenterPriorityCrop;

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is TileOptionsData other
               && OverlapPx == other.OverlapPx
               && BlendMode == other.BlendMode;
    }

    public override ModelBase Clone()
    {
        return new TileOptionsData
        {
            OverlapPx = OverlapPx,
            BlendMode = BlendMode
        };
    }

    #endregion
}
