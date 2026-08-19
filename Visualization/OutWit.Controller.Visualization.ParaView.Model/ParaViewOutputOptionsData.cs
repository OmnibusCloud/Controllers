using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// What to render from the package: the view, the output size and format, and the timesteps.
/// Everything except <see cref="Frames"/> is per-output and participates in the task identity;
/// the frame selection determines the task set.
/// </summary>
[JobDocumentContract("paraview.outputOptions@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class ParaViewOutputOptionsData : ModelBase
{
    #region Properties

    /// <summary>
    /// Registration name of the render view to capture (the state's views collection, for example
    /// RenderView1). Empty selects the state's first render view; the validation report records the
    /// resolved name.
    /// </summary>
    [MemoryPackOrder(0)]
    public string ViewId { get; set; } = string.Empty;

    /// <summary>
    /// Output width in pixels.
    /// </summary>
    [MemoryPackOrder(1)]
    public int Width { get; set; } = 1920;

    /// <summary>
    /// Output height in pixels.
    /// </summary>
    [MemoryPackOrder(2)]
    public int Height { get; set; } = 1080;

    /// <summary>
    /// Output image format.
    /// </summary>
    [MemoryPackOrder(3)]
    public ParaViewImageFormat Format { get; set; } = ParaViewImageFormat.Png;

    /// <summary>
    /// Render with a transparent background (PNG only; ignored for JPEG).
    /// </summary>
    [MemoryPackOrder(4)]
    public bool TransparentBackground { get; set; }

    /// <summary>
    /// Timesteps to render.
    /// </summary>
    [MemoryPackOrder(5)]
    public ParaViewFrameSelectionData Frames { get; set; } = new();

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewOutputOptionsData other)
            return false;

        return ViewId.Is(other.ViewId)
               && Width.Is(other.Width)
               && Height.Is(other.Height)
               && Format == other.Format
               && TransparentBackground == other.TransparentBackground
               && Frames.Is(other.Frames, tolerance);
    }

    public override ModelBase Clone()
    {
        return new ParaViewOutputOptionsData
        {
            ViewId = ViewId,
            Width = Width,
            Height = Height,
            Format = Format,
            TransparentBackground = TransparentBackground,
            Frames = (ParaViewFrameSelectionData)Frames.Clone()
        };
    }

    #endregion
}
