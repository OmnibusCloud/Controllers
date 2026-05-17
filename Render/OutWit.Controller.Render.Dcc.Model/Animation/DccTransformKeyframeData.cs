using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// One transform keyframe for the first animation-aware DCC slice.
/// </summary>
[MemoryPackable]
public partial class DccTransformKeyframeData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccTransformKeyframeData other
               && Frame.Is(other.Frame)
               && InterpolationMode.Is(other.InterpolationMode)
               && Transform.Is(other.Transform, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccTransformKeyframeData
        {
            Frame = Frame,
            InterpolationMode = InterpolationMode,
            Transform = (DccTransformData)Transform.Clone()
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Target frame number.
    /// </summary>
    public int Frame { get; set; }

    /// <summary>
    /// Transform sampled at the frame.
    /// </summary>
    public DccTransformData Transform { get; set; } = new();

    /// <summary>
    /// Interpolation mode applied to the generated keyframe.
    /// </summary>
    public DccKeyframeInterpolationMode InterpolationMode { get; set; } = DccKeyframeInterpolationMode.Bezier;

    #endregion
}
