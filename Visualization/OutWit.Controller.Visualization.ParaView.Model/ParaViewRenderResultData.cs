using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// Result of rendering one task: the task identity it answers, the output image blob, the
/// validated output dimensions and size, and the exact backend versions that produced it.
/// </summary>
[JobDocumentContract("paraview.renderResult@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class ParaViewRenderResultData : ModelBase
{
    #region Properties

    /// <summary>
    /// Identity of the task this result answers.
    /// </summary>
    [MemoryPackOrder(0)]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// Ordinal of the task (Collect restores this order).
    /// </summary>
    [MemoryPackOrder(1)]
    public int TaskIndex { get; set; }

    /// <summary>
    /// Registration name of the rendered view.
    /// </summary>
    [MemoryPackOrder(2)]
    public string ViewId { get; set; } = string.Empty;

    /// <summary>
    /// Rendered timestep index.
    /// </summary>
    [MemoryPackOrder(3)]
    public int TimestepIndex { get; set; }

    /// <summary>
    /// Rendered physical time value; null for a static scene.
    /// </summary>
    [MemoryPackOrder(4)]
    public double? TimeValue { get; set; }

    /// <summary>
    /// Blob identifier of the rendered image.
    /// </summary>
    [MemoryPackOrder(5)]
    public Guid ImageBlobId { get; set; }

    /// <summary>
    /// Validated output width in pixels.
    /// </summary>
    [MemoryPackOrder(6)]
    public int Width { get; set; }

    /// <summary>
    /// Validated output height in pixels.
    /// </summary>
    [MemoryPackOrder(7)]
    public int Height { get; set; }

    /// <summary>
    /// Output image format.
    /// </summary>
    [MemoryPackOrder(8)]
    public ParaViewImageFormat Format { get; set; }

    /// <summary>
    /// Output byte size.
    /// </summary>
    [MemoryPackOrder(9)]
    public long ByteSize { get; set; }

    /// <summary>
    /// Exact ParaView runtime version that rendered the task (for example 6.1.1).
    /// </summary>
    [MemoryPackOrder(10)]
    public string RuntimeVersion { get; set; } = string.Empty;

    /// <summary>
    /// Version of the bundled OmnibusCloud reader loaded for the task; empty when no plugin was loaded.
    /// </summary>
    [MemoryPackOrder(11)]
    public string ReaderVersion { get; set; } = string.Empty;

    /// <summary>
    /// Wall-clock seconds of the runner process.
    /// </summary>
    [MemoryPackOrder(12)]
    public double RenderSeconds { get; set; }

    /// <summary>
    /// Bounded diagnostic text from the runner (never the full process output).
    /// </summary>
    [MemoryPackOrder(13)]
    public string Diagnostics { get; set; } = string.Empty;

    #endregion

    #region ModelBase

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewRenderResultData other)
            return false;

        return TaskId.Is(other.TaskId)
               && TaskIndex.Is(other.TaskIndex)
               && ViewId.Is(other.ViewId)
               && TimestepIndex.Is(other.TimestepIndex)
               && Nullable.Equals(TimeValue, other.TimeValue)
               && ImageBlobId.Is(other.ImageBlobId)
               && Width.Is(other.Width)
               && Height.Is(other.Height)
               && Format == other.Format
               && ByteSize.Is(other.ByteSize)
               && RuntimeVersion.Is(other.RuntimeVersion)
               && ReaderVersion.Is(other.ReaderVersion)
               && RenderSeconds.Is(other.RenderSeconds, tolerance)
               && Diagnostics.Is(other.Diagnostics);
    }

    public override ModelBase Clone()
    {
        return new ParaViewRenderResultData
        {
            TaskId = TaskId,
            TaskIndex = TaskIndex,
            ViewId = ViewId,
            TimestepIndex = TimestepIndex,
            TimeValue = TimeValue,
            ImageBlobId = ImageBlobId,
            Width = Width,
            Height = Height,
            Format = Format,
            ByteSize = ByteSize,
            RuntimeVersion = RuntimeVersion,
            ReaderVersion = ReaderVersion,
            RenderSeconds = RenderSeconds,
            Diagnostics = Diagnostics
        };
    }

    #endregion
}
