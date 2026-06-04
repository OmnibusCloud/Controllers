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

    /// <summary>
    /// Persistent-batch chunk size: how many frames/tiles Render.SplitBatched groups into one chunk
    /// (each chunk renders in a single Blender process). 0 = use the per-engine default (Render.Split
    /// derives it from <see cref="Engine"/>). Declared last so the wire format stays backward
    /// compatible — callers (plugin/SDK) that don't set it deserialize to 0 and get the default.
    /// </summary>
    public int BatchSize { get; set; }

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
               && Denoise == other.Denoise
               && BatchSize == other.BatchSize;
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
            Denoise = Denoise,
            BatchSize = BatchSize
        };
    }

    #endregion
}
