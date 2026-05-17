using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// One scalar keyframe for the first animation-aware DCC slice.
/// </summary>
[MemoryPackable]
public partial class DccScalarKeyframeData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccScalarKeyframeData other
               && Frame.Is(other.Frame)
               && Value.Is(other.Value, tolerance)
               && InterpolationMode.Is(other.InterpolationMode);
    }

    public override ModelBase Clone()
    {
        return new DccScalarKeyframeData
        {
            Frame = Frame,
            Value = Value,
            InterpolationMode = InterpolationMode
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Target frame number.
    /// </summary>
    public int Frame { get; set; }

    /// <summary>
    /// Scalar value sampled at the frame.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Interpolation mode applied to the generated keyframe.
    /// </summary>
    public DccKeyframeInterpolationMode InterpolationMode { get; set; } = DccKeyframeInterpolationMode.Bezier;

    #endregion
}
