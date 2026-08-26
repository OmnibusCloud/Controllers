using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// A chunk of render tasks one pvpython process renders together (docs 03, section 27, item 2 —
/// FrameBatch): the shared state, options and runtime are hoisted once, the attachment list is the
/// UNION of the chunk's per-timestep subsets (still only what these outputs need), and the outputs
/// themselves travel as the per-task records in render order. Generated host-side by
/// ParaView.SplitBatched, consumed node-side by ParaView.RenderFrameBatch. Not a document type:
/// batches never cross the initiator boundary. Model 0.5.0.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ParaViewRenderTaskBatchData : ModelBase
{
    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewRenderTaskBatchData other)
            return false;

        return BatchIndex.Is(other.BatchIndex)
               && StateBlobId.Is(other.StateBlobId)
               && StateSha256.Is(other.StateSha256)
               && StateSize.Is(other.StateSize)
               && Options.Is(other.Options, tolerance)
               && Attachments.Count == other.Attachments.Count
               && Attachments.Zip(other.Attachments, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Runtime.Is(other.Runtime, tolerance)
               && PackageDigest.Is(other.PackageDigest)
               && DatasetId.Is(other.DatasetId)
               && SubsetBytes.Is(other.SubsetBytes)
               && Tasks.Count == other.Tasks.Count
               && Tasks.Zip(other.Tasks, (left, right) => left.Is(right, tolerance)).All(me => me);
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewRenderTaskBatchData
        {
            BatchIndex = BatchIndex,
            StateBlobId = StateBlobId,
            StateSha256 = StateSha256,
            StateSize = StateSize,
            Options = (ParaViewOutputOptionsData)Options.Clone(),
            Attachments = [.. Attachments.Select(me => (ParaViewAttachmentRefData)me.Clone())],
            Runtime = (ParaViewRuntimeRequirementData)Runtime.Clone(),
            PackageDigest = PackageDigest,
            DatasetId = DatasetId,
            SubsetBytes = SubsetBytes,
            Tasks = [.. Tasks.Select(me => (ParaViewRenderTaskData)me.Clone())]
        };
    }

    #endregion

    #region Properties

    /// <summary>Ordinal of the chunk in the split (diagnostics and workspace naming).</summary>
    [MemoryPackOrder(0)]
    public int BatchIndex { get; set; }

    /// <summary>Blob of the rewritten state every output of the chunk loads.</summary>
    [MemoryPackOrder(1)]
    public Guid StateBlobId { get; set; }

    /// <summary>SHA-256 of the state blob, verified at materialization.</summary>
    [MemoryPackOrder(2)]
    public string StateSha256 { get; set; } = string.Empty;

    /// <summary>Byte size of the state blob, verified at materialization.</summary>
    [MemoryPackOrder(3)]
    public long StateSize { get; set; }

    /// <summary>Output options shared by every output of the chunk (the resolved view stamped).</summary>
    [MemoryPackOrder(4)]
    public ParaViewOutputOptionsData Options { get; set; } = new();

    /// <summary>
    /// The union of the chunk's per-timestep attachment subsets, in package order: every static
    /// input, series index and series anchor once, plus the pieces of the chunk's timesteps.
    /// </summary>
    [MemoryPackOrder(5)]
    public List<ParaViewAttachmentRefData> Attachments { get; set; } = [];

    /// <summary>Runtime requirement of the package (series, plugins).</summary>
    [MemoryPackOrder(6)]
    public ParaViewRuntimeRequirementData Runtime { get; set; } = new();

    /// <summary>Package digest the task identities are derived from.</summary>
    [MemoryPackOrder(7)]
    public string PackageDigest { get; set; } = string.Empty;

    /// <summary>Dataset identity component (reserved, empty in version 1).</summary>
    [MemoryPackOrder(8)]
    public string DatasetId { get; set; } = string.Empty;

    /// <summary>Bytes the node materializes for the chunk: the state plus the attachment union.</summary>
    [MemoryPackOrder(9)]
    public long SubsetBytes { get; set; }

    /// <summary>
    /// The outputs of the chunk in render order — one record per output with its identity, task
    /// index, timestep and camera move. Their own attachment lists are EMPTY: the chunk's union
    /// above is what the node materializes.
    /// </summary>
    [MemoryPackOrder(10)]
    public List<ParaViewRenderTaskData> Tasks { get; set; } = [];

    #endregion
}
