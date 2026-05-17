using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using System.Linq;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Texture-slot binding contract for the first DCC scene slice.
/// </summary>
[MemoryPackable]
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
    public DccTextureSlotKind Slot { get; set; }

    /// <summary>
    /// Bound logical image asset id.
    /// </summary>
    public string ImageAssetId { get; set; } = string.Empty;

    /// <summary>
    /// U scale.
    /// </summary>
    public double UvScaleX { get; set; } = 1d;

    /// <summary>
    /// V scale.
    /// </summary>
    public double UvScaleY { get; set; } = 1d;

    /// <summary>
    /// U offset.
    /// </summary>
    public double UvOffsetX { get; set; }

    /// <summary>
    /// V offset.
    /// </summary>
    public double UvOffsetY { get; set; }

    /// <summary>
    /// UV rotation in degrees.
    /// </summary>
    public double UvRotationDegrees { get; set; }

    /// <summary>
    /// Optional UV-transform keyframes for the first texture-transform animation slice.
    /// </summary>
    public List<DccTextureTransformKeyframeData> UvTransformKeyframes { get; set; } = [];

    #endregion
}
