using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// One color keyframe for the first animation-aware DCC slice.
/// </summary>
[MemoryPackable]
public partial class DccColorKeyframeData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccColorKeyframeData other
               && Frame.Is(other.Frame)
               && Color.Is(other.Color, tolerance)
               && InterpolationMode.Is(other.InterpolationMode);
    }

    public override ModelBase Clone()
    {
        return new DccColorKeyframeData
        {
            Frame = Frame,
            Color = (DccColorData)Color.Clone(),
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
    /// Color sampled at the frame.
    /// </summary>
    public DccColorData Color { get; set; } = new();

    /// <summary>
    /// Interpolation mode applied to the generated keyframe.
    /// </summary>
    public DccKeyframeInterpolationMode InterpolationMode { get; set; } = DccKeyframeInterpolationMode.Bezier;

    #endregion
}
