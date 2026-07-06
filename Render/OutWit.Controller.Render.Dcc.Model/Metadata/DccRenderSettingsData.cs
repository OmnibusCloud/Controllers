using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice render-settings contract.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class DccRenderSettingsData : ModelBase
{
    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        return modelBase is DccRenderSettingsData other
               && ResolutionX.Is(other.ResolutionX)
               && ResolutionY.Is(other.ResolutionY)
               && FrameStart.Is(other.FrameStart)
               && FrameEnd.Is(other.FrameEnd)
               && Fps.Is(other.Fps)
               && TargetEngine.Is(other.TargetEngine)
               && Samples.Is(other.Samples)
               && ViewTransform.Is(other.ViewTransform)
               && Exposure.Is(other.Exposure, tolerance)
               && MotionBlur == other.MotionBlur
               && MotionBlurShutter.Is(other.MotionBlurShutter, tolerance);
    }

    public override ModelBase Clone()
    {
        return new DccRenderSettingsData
        {
            ResolutionX = ResolutionX,
            ResolutionY = ResolutionY,
            FrameStart = FrameStart,
            FrameEnd = FrameEnd,
            Fps = Fps,
            TargetEngine = TargetEngine,
            Samples = Samples,
            ViewTransform = ViewTransform,
            Exposure = Exposure,
            MotionBlur = MotionBlur,
            MotionBlurShutter = MotionBlurShutter
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Output width in pixels.
    /// </summary>
    [MemoryPackOrder(0)]
    public int ResolutionX { get; set; } = 1920;

    /// <summary>
    /// Output height in pixels.
    /// </summary>
    [MemoryPackOrder(1)]
    public int ResolutionY { get; set; } = 1080;

    /// <summary>
    /// Start frame.
    /// </summary>
    [MemoryPackOrder(2)]
    public int FrameStart { get; set; } = 1;

    /// <summary>
    /// End frame.
    /// </summary>
    [MemoryPackOrder(3)]
    public int FrameEnd { get; set; } = 1;

    /// <summary>
    /// Frames per second.
    /// </summary>
    [MemoryPackOrder(4)]
    public int Fps { get; set; } = 24;

    /// <summary>
    /// Target render engine.
    /// </summary>
    [MemoryPackOrder(5)]
    public RenderEngine TargetEngine { get; set; } = RenderEngine.Cycles;

    /// <summary>
    /// Sample count.
    /// </summary>
    [MemoryPackOrder(6)]
    public int Samples { get; set; } = 64;

    /// <summary>
    /// Color-management view transform (e.g. "Standard", "Filmic", "AgX"). Empty leaves the
    /// renderer default unchanged.
    /// </summary>
    [MemoryPackOrder(7)]
    public string ViewTransform { get; set; } = string.Empty;

    /// <summary>
    /// Color-management exposure (stops). 0 leaves the default.
    /// </summary>
    [MemoryPackOrder(8)]
    public double Exposure { get; set; }

    /// <summary>
    /// Whether motion blur is enabled for the render.
    /// </summary>
    [MemoryPackOrder(9)]
    public bool MotionBlur { get; set; }

    /// <summary>
    /// Motion-blur shutter (fraction of a frame the shutter is open). Blender default is 0.5.
    /// Only applied when <see cref="MotionBlur"/> is true.
    /// </summary>
    [MemoryPackOrder(10)]
    public double MotionBlurShutter { get; set; } = 0.5d;

    #endregion
}
