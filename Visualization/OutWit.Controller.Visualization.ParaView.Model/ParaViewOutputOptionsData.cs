using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// What to render from the package: the view, the output size and format, and the timesteps.
/// Everything except <see cref="Frames"/> and <see cref="Turntable"/> is per-output and
/// participates in the task identity; the frame selection and the turntable determine the task
/// set (a turntable task additionally carries its orbit position in its identity).
/// </summary>
[JobDocumentContract("paraview.outputOptions@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ParaViewOutputOptionsData : ModelBase
{
    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewOutputOptionsData other)
            return false;

        return ViewId.Is(other.ViewId)
               && Width.Is(other.Width)
               && Height.Is(other.Height)
               && Format.Is(other.Format)
               && TransparentBackground.Is(other.TransparentBackground)
               && Frames.Is(other.Frames, tolerance)
               && (Turntable == null ? other.Turntable == null : other.Turntable != null && Turntable.Is(other.Turntable, tolerance));
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewOutputOptionsData
        {
            ViewId = ViewId,
            Width = Width,
            Height = Height,
            Format = Format,
            TransparentBackground = TransparentBackground,
            Frames = (ParaViewFrameSelectionData)Frames.Clone(),
            Turntable = (ParaViewTurntableData?)Turntable?.Clone()
        };
    }

    #endregion

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

    /// <summary>
    /// Optional camera orbit (turntable): null renders the captured camera as is. Appended in
    /// Model 0.2.0; an older reader ignores it, an older writer leaves it null.
    /// </summary>
    [MemoryPackOrder(6)]
    public ParaViewTurntableData? Turntable { get; set; }

    #endregion
}
