using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// One UV-transform keyframe for the first texture-animation-aware DCC slice.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DccTextureTransformKeyframeData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccTextureTransformKeyframeData other
               && Frame.Is(other.Frame)
               && UvScaleX.Is(other.UvScaleX, tolerance)
               && UvScaleY.Is(other.UvScaleY, tolerance)
               && UvOffsetX.Is(other.UvOffsetX, tolerance)
               && UvOffsetY.Is(other.UvOffsetY, tolerance)
               && UvRotationDegrees.Is(other.UvRotationDegrees, tolerance)
               && InterpolationMode.Is(other.InterpolationMode);
    }

    public override ModelBase Clone()
    {
        return new DccTextureTransformKeyframeData
        {
            Frame = Frame,
            UvScaleX = UvScaleX,
            UvScaleY = UvScaleY,
            UvOffsetX = UvOffsetX,
            UvOffsetY = UvOffsetY,
            UvRotationDegrees = UvRotationDegrees,
            InterpolationMode = InterpolationMode
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Target frame number.
    /// </summary>
    [MemoryPackOrder(0)]
    public int Frame { get; set; }

    /// <summary>
    /// U scale.
    /// </summary>
    [MemoryPackOrder(1)]
    public double UvScaleX { get; set; } = 1d;

    /// <summary>
    /// V scale.
    /// </summary>
    [MemoryPackOrder(2)]
    public double UvScaleY { get; set; } = 1d;

    /// <summary>
    /// U offset.
    /// </summary>
    [MemoryPackOrder(3)]
    public double UvOffsetX { get; set; }

    /// <summary>
    /// V offset.
    /// </summary>
    [MemoryPackOrder(4)]
    public double UvOffsetY { get; set; }

    /// <summary>
    /// UV rotation in degrees.
    /// </summary>
    [MemoryPackOrder(5)]
    public double UvRotationDegrees { get; set; }

    /// <summary>
    /// Interpolation mode applied to the generated keyframe.
    /// </summary>
    [MemoryPackOrder(6)]
    public DccKeyframeInterpolationMode InterpolationMode { get; set; } = DccKeyframeInterpolationMode.Bezier;

    #endregion
}
