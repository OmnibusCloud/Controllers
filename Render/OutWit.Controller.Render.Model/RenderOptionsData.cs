using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Controller.Render.Model;

/// <summary>
/// Rendering parameters: format, engine, and quality settings.
/// Shared between SDK, controller, and plugins.
/// </summary>
[MemoryPackable]
public partial class RenderOptionsData : ModelBase
{
    #region Properties

    /// <summary>
    /// Output image format.
    /// </summary>
    public RenderFormat Format { get; set; } = RenderFormat.PNG;

    /// <summary>
    /// Render engine.
    /// </summary>
    public RenderEngine Engine { get; set; } = RenderEngine.Cycles;

    /// <summary>
    /// Sample count. 0 = use scene default.
    /// </summary>
    public int Samples { get; set; }

    /// <summary>
    /// Output width. 0 = use scene default.
    /// </summary>
    public int ResolutionX { get; set; }

    /// <summary>
    /// Output height. 0 = use scene default.
    /// </summary>
    public int ResolutionY { get; set; }

    /// <summary>
    /// Apply denoising to the rendered image.
    /// </summary>
    public bool Denoise { get; set; }

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not RenderOptionsData other)
            return false;

        return Format == other.Format
               && Engine == other.Engine
               && Samples == other.Samples
               && ResolutionX == other.ResolutionX
               && ResolutionY == other.ResolutionY
               && Denoise == other.Denoise;
    }

    public override ModelBase Clone()
    {
        return new RenderOptionsData
        {
            Format = Format,
            Engine = Engine,
            Samples = Samples,
            ResolutionX = ResolutionX,
            ResolutionY = ResolutionY,
            Denoise = Denoise
        };
    }

    #endregion
}
