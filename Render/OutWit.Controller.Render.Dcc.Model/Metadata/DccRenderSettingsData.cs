using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;
using OutWit.Controller.Render.Model;

namespace OutWit.Controller.Render.Dcc.Model;

/// <summary>
/// Neutral first-slice render-settings contract.
/// </summary>
[MemoryPackable]
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
               && Samples.Is(other.Samples);
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
            Samples = Samples
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Output width in pixels.
    /// </summary>
    public int ResolutionX { get; set; } = 1920;

    /// <summary>
    /// Output height in pixels.
    /// </summary>
    public int ResolutionY { get; set; } = 1080;

    /// <summary>
    /// Start frame.
    /// </summary>
    public int FrameStart { get; set; } = 1;

    /// <summary>
    /// End frame.
    /// </summary>
    public int FrameEnd { get; set; } = 1;

    /// <summary>
    /// Frames per second.
    /// </summary>
    public int Fps { get; set; } = 24;

    /// <summary>
    /// Target render engine.
    /// </summary>
    public RenderEngine TargetEngine { get; set; } = RenderEngine.Cycles;

    /// <summary>
    /// Sample count.
    /// </summary>
    public int Samples { get; set; } = 64;

    #endregion
}
