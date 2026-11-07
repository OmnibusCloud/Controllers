using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// One content-addressed file of a visualization package: the blob that holds it, the
/// normalized logical path the state refers to it by, its digest and size, its semantic role,
/// and the file-series metadata that drives per-task attachment subsetting.
/// </summary>
// VersionTolerant: this type rides inside the plugin↔server request payload, so version skew
// must degrade (unknown members ignored) rather than reject the submission. Explicit
// MemoryPackOrder on every member keeps the wire layout structural — append only.
[JobDocumentContract("paraview.attachmentRef@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ParaViewAttachmentRefData : ModelBase
{
    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewAttachmentRefData other)
            return false;

        return BlobId.Is(other.BlobId)
               && LogicalPath.Is(other.LogicalPath)
               && Sha256.Is(other.Sha256)
               && Size.Is(other.Size)
               && Role.Is(other.Role)
               && SeriesGroup.Is(other.SeriesGroup)
               && TimestepIndices.Is(other.TimestepIndices)
               && SeriesOrdinal.Is(other.SeriesOrdinal);
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewAttachmentRefData
        {
            BlobId = BlobId,
            LogicalPath = LogicalPath,
            Sha256 = Sha256,
            Size = Size,
            Role = Role,
            SeriesGroup = SeriesGroup,
            TimestepIndices = [.. TimestepIndices],
            SeriesOrdinal = SeriesOrdinal
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Blob identifier of the uploaded file content.
    /// </summary>
    [MemoryPackOrder(0)]
    public Guid BlobId { get; set; }

    /// <summary>
    /// Normalized logical path inside the package: relative, '/'-separated, no '..', no drive
    /// letter, no URI scheme. The state refers to the file by exactly this path, and the node
    /// materializes the file at exactly this path under the task's package root.
    /// </summary>
    [MemoryPackOrder(1)]
    public string LogicalPath { get; set; } = string.Empty;

    /// <summary>
    /// Lower-case hexadecimal SHA-256 digest of the file content.
    /// </summary>
    [MemoryPackOrder(2)]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Declared byte size of the file content.
    /// </summary>
    [MemoryPackOrder(3)]
    public long Size { get; set; }

    /// <summary>
    /// Semantic role of the file.
    /// </summary>
    [MemoryPackOrder(4)]
    public ParaViewAttachmentRole Role { get; set; } = ParaViewAttachmentRole.ReaderInput;

    /// <summary>
    /// Name of the file-series group the file belongs to (for example the reader's registration
    /// name); empty for a file that is not part of a series.
    /// </summary>
    [MemoryPackOrder(5)]
    public string SeriesGroup { get; set; } = string.Empty;

    /// <summary>
    /// Indices into the state's timeline at which this file is displayed. Empty means the file
    /// applies to every timestep (static meshes, non-series inputs, indexes). A series member
    /// with an empty list is a series whose format gives no per-timestep association: the whole
    /// group is shipped to every task and the fallback is recorded in the validation report.
    /// </summary>
    [MemoryPackOrder(6)]
    public List<int> TimestepIndices { get; set; } = [];

    /// <summary>
    /// Position of the file inside its series group (ordering metadata, informational).
    /// </summary>
    [MemoryPackOrder(7)]
    public int SeriesOrdinal { get; set; }

    #endregion
}
