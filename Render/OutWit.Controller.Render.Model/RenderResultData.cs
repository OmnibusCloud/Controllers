using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Result of rendering a single frame or tile.
/// Contains the task index and a blob reference to the rendered image.
/// Shared between SDK, controller, and plugins.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only, and deploy the server before any client that writes a new member (default MemoryPack mode rejects payloads with unknown members).
public partial class RenderResultData : ModelBase
{
    #region Properties

    /// <summary>
    /// Task index for ordering (matches <see cref="RenderTaskData.TaskIndex"/>).
    /// </summary>
    [MemoryPackOrder(0)]
    public int Index { get; set; }

    /// <summary>
    /// Blob identifier of the rendered image in blob storage.
    /// </summary>
    [MemoryPackOrder(1)]
    public Guid ImageBlobId { get; set; }

    /// <summary>
    /// Tile region — normalized X minimum (0.0-1.0).
    /// </summary>
    [MemoryPackOrder(2)]
    public float TileMinX { get; set; }

    /// <summary>
    /// Tile region — normalized X maximum (0.0-1.0).
    /// </summary>
    [MemoryPackOrder(3)]
    public float TileMaxX { get; set; } = 1f;

    /// <summary>
    /// Tile region — normalized Y minimum (0.0-1.0).
    /// </summary>
    [MemoryPackOrder(4)]
    public float TileMinY { get; set; }

    /// <summary>
    /// Tile region — normalized Y maximum (0.0-1.0).
    /// </summary>
    [MemoryPackOrder(5)]
    public float TileMaxY { get; set; } = 1f;

    /// <summary>
    /// Actual rendered X minimum including overlap expansion.
    /// When not set explicitly, the logical tile bounds are used.
    /// </summary>
    [MemoryPackOrder(6)]
    public float RenderMinX { get; set; }

    /// <summary>
    /// Actual rendered X maximum including overlap expansion.
    /// When not set explicitly, the logical tile bounds are used.
    /// </summary>
    [MemoryPackOrder(7)]
    public float RenderMaxX { get; set; } = 1f;

    /// <summary>
    /// Actual rendered Y minimum including overlap expansion.
    /// When not set explicitly, the logical tile bounds are used.
    /// </summary>
    [MemoryPackOrder(8)]
    public float RenderMinY { get; set; }

    /// <summary>
    /// Actual rendered Y maximum including overlap expansion.
    /// When not set explicitly, the logical tile bounds are used.
    /// </summary>
    [MemoryPackOrder(9)]
    public float RenderMaxY { get; set; } = 1f;

    #endregion

    #region Functions

    [MemoryPackIgnore]
    public bool HasExplicitRenderBounds => RenderMinX != 0f || RenderMaxX != 1f || RenderMinY != 0f || RenderMaxY != 1f;

    [MemoryPackIgnore]
    public float EffectiveRenderMinX => HasExplicitRenderBounds ? RenderMinX : TileMinX;

    [MemoryPackIgnore]
    public float EffectiveRenderMaxX => HasExplicitRenderBounds ? RenderMaxX : TileMaxX;

    [MemoryPackIgnore]
    public float EffectiveRenderMinY => HasExplicitRenderBounds ? RenderMinY : TileMinY;

    [MemoryPackIgnore]
    public float EffectiveRenderMaxY => HasExplicitRenderBounds ? RenderMaxY : TileMaxY;

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderResultData other)
            return false;

        return Index.Is(other.Index)
               && ImageBlobId.Is(other.ImageBlobId)
               && TileMinX.Is(other.TileMinX, tolerance)
               && TileMaxX.Is(other.TileMaxX, tolerance)
               && TileMinY.Is(other.TileMinY, tolerance)
               && TileMaxY.Is(other.TileMaxY, tolerance)
               && RenderMinX.Is(other.RenderMinX, tolerance)
               && RenderMaxX.Is(other.RenderMaxX, tolerance)
               && RenderMinY.Is(other.RenderMinY, tolerance)
               && RenderMaxY.Is(other.RenderMaxY, tolerance);
    }

    public override ModelBase Clone()
    {
        return new RenderResultData
        {
            Index = Index,
            ImageBlobId = ImageBlobId,
            TileMinX = TileMinX,
            TileMaxX = TileMaxX,
            TileMinY = TileMinY,
            TileMaxY = TileMaxY,
            RenderMinX = RenderMinX,
            RenderMaxX = RenderMaxX,
            RenderMinY = RenderMinY,
            RenderMaxY = RenderMaxY
        };
    }

    #endregion
}
