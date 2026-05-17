using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// One visibility/renderability keyframe for the first animation-aware DCC slice.
/// </summary>
[MemoryPackable]
public partial class DccVisibilityKeyframeData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccVisibilityKeyframeData other
               && Frame.Is(other.Frame)
               && Visible.Is(other.Visible)
               && Renderable.Is(other.Renderable)
               && InterpolationMode.Is(other.InterpolationMode);
    }

    public override ModelBase Clone()
    {
        return new DccVisibilityKeyframeData
        {
            Frame = Frame,
            Visible = Visible,
            Renderable = Renderable,
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
    /// Visibility state at the frame.
    /// </summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Renderability state at the frame.
    /// </summary>
    public bool Renderable { get; set; } = true;

    /// <summary>
    /// Interpolation mode applied to the generated keyframe.
    /// </summary>
    public DccKeyframeInterpolationMode InterpolationMode { get; set; } = DccKeyframeInterpolationMode.Bezier;

    #endregion
}
