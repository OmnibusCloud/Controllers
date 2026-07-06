using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Texture-slot binding contract for the first DCC scene slice.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DccTextureSlotData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccTextureSlotData other
               && Slot.Is(other.Slot)
               && ImageAssetId.Is(other.ImageAssetId)
               && UvScaleX.Is(other.UvScaleX, tolerance)
               && UvScaleY.Is(other.UvScaleY, tolerance)
               && UvOffsetX.Is(other.UvOffsetX, tolerance)
               && UvOffsetY.Is(other.UvOffsetY, tolerance)
               && UvRotationDegrees.Is(other.UvRotationDegrees, tolerance)
               && UvTransformKeyframes.Count == other.UvTransformKeyframes.Count
               && UvTransformKeyframes.Zip(other.UvTransformKeyframes, (left, right) => left.Is(right, tolerance)).All(me => me);
    }

    public override ModelBase Clone()
    {
        return new DccTextureSlotData
        {
            Slot = Slot,
            ImageAssetId = ImageAssetId,
            UvScaleX = UvScaleX,
            UvScaleY = UvScaleY,
            UvOffsetX = UvOffsetX,
            UvOffsetY = UvOffsetY,
            UvRotationDegrees = UvRotationDegrees,
            UvTransformKeyframes = [.. UvTransformKeyframes.Select(me => (DccTextureTransformKeyframeData)me.Clone())]
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Neutral texture slot kind.
    /// </summary>
    [MemoryPackOrder(0)]
    public DccTextureSlotKind Slot { get; set; }

    /// <summary>
    /// Bound logical image asset id.
    /// </summary>
    [MemoryPackOrder(1)]
    public string ImageAssetId { get; set; } = string.Empty;

    /// <summary>
    /// U scale.
    /// </summary>
    [MemoryPackOrder(2)]
    public double UvScaleX { get; set; } = 1d;

    /// <summary>
    /// V scale.
    /// </summary>
    [MemoryPackOrder(3)]
    public double UvScaleY { get; set; } = 1d;

    /// <summary>
    /// U offset.
    /// </summary>
    [MemoryPackOrder(4)]
    public double UvOffsetX { get; set; }

    /// <summary>
    /// V offset.
    /// </summary>
    [MemoryPackOrder(5)]
    public double UvOffsetY { get; set; }

    /// <summary>
    /// UV rotation in degrees.
    /// </summary>
    [MemoryPackOrder(6)]
    public double UvRotationDegrees { get; set; }

    /// <summary>
    /// Optional UV-transform keyframes for the first texture-transform animation slice.
    /// </summary>
    [MemoryPackOrder(7)]
    public List<DccTextureTransformKeyframeData> UvTransformKeyframes { get; set; } = [];

    #endregion
}
