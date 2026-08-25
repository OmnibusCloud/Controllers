using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// Self-contained description of one render task: the state, the view, the timestep, the output
/// options, and — the viability requirement of the distribution model — only the attachments this
/// task's timestep needs. Generated host-side by ParaView.Split, consumed node-side by
/// ParaView.RenderFrame. Not a document type: tasks never cross the initiator boundary.
/// </summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ParaViewRenderTaskData : ModelBase
{
    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewRenderTaskData other)
            return false;

        return TaskId.Is(other.TaskId)
               && TaskIndex.Is(other.TaskIndex)
               && StateBlobId.Is(other.StateBlobId)
               && StateSha256.Is(other.StateSha256)
               && StateSize.Is(other.StateSize)
               && ViewId.Is(other.ViewId)
               && TimestepIndex.Is(other.TimestepIndex)
               && TimeValue.Is(other.TimeValue, tolerance)
               && Options.Is(other.Options, tolerance)
               && Attachments.Count == other.Attachments.Count
               && Attachments.Zip(other.Attachments, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Runtime.Is(other.Runtime, tolerance)
               && PackageDigest.Is(other.PackageDigest)
               && DatasetId.Is(other.DatasetId)
               && SubsetBytes.Is(other.SubsetBytes)
               && OrbitIndex.Is(other.OrbitIndex)
               && AzimuthDegrees.Is(other.AzimuthDegrees, tolerance)
               && ElevationDegrees.Is(other.ElevationDegrees, tolerance)
               && DollyFactor.Is(other.DollyFactor, tolerance);
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewRenderTaskData
        {
            TaskId = TaskId,
            TaskIndex = TaskIndex,
            StateBlobId = StateBlobId,
            StateSha256 = StateSha256,
            StateSize = StateSize,
            ViewId = ViewId,
            TimestepIndex = TimestepIndex,
            TimeValue = TimeValue,
            Options = (ParaViewOutputOptionsData)Options.Clone(),
            Attachments = [.. Attachments.Select(me => (ParaViewAttachmentRefData)me.Clone())],
            Runtime = (ParaViewRuntimeRequirementData)Runtime.Clone(),
            PackageDigest = PackageDigest,
            DatasetId = DatasetId,
            SubsetBytes = SubsetBytes,
            OrbitIndex = OrbitIndex,
            AzimuthDegrees = AzimuthDegrees,
            ElevationDegrees = ElevationDegrees,
            DollyFactor = DollyFactor
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Deterministic task identity: SHA-256 over package digest, dataset identity (reserved, empty
    /// in version 1), view id, timestep index, and the output options digest.
    /// </summary>
    [MemoryPackOrder(0)]
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// Ordinal of the task in the collection (requested view order, then timestep order). Results
    /// arrive in completion order; Collect restores this order.
    /// </summary>
    [MemoryPackOrder(1)]
    public int TaskIndex { get; set; }

    /// <summary>
    /// Blob identifier of the state (.pvsm).
    /// </summary>
    [MemoryPackOrder(2)]
    public Guid StateBlobId { get; set; }

    /// <summary>
    /// Expected SHA-256 of the state; verified when materialized.
    /// </summary>
    [MemoryPackOrder(3)]
    public string StateSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Declared state size in bytes.
    /// </summary>
    [MemoryPackOrder(4)]
    public long StateSize { get; set; }

    /// <summary>
    /// Resolved registration name of the view to render.
    /// </summary>
    [MemoryPackOrder(5)]
    public string ViewId { get; set; } = string.Empty;

    /// <summary>
    /// Index into the resolved timeline.
    /// </summary>
    [MemoryPackOrder(6)]
    public int TimestepIndex { get; set; }

    /// <summary>
    /// Physical time value of the timestep; null for a static scene.
    /// </summary>
    [MemoryPackOrder(7)]
    public double? TimeValue { get; set; }

    /// <summary>
    /// Output options (view, size, format) — identical across the tasks of one job.
    /// </summary>
    [MemoryPackOrder(8)]
    public ParaViewOutputOptionsData Options { get; set; } = new();

    /// <summary>
    /// This task's minimal attachment subset: the files associated with its timestep plus every
    /// static attachment and series index. The node materializes exactly these and nothing else.
    /// </summary>
    [MemoryPackOrder(9)]
    public List<ParaViewAttachmentRefData> Attachments { get; set; } = [];

    /// <summary>
    /// Runtime requirements of the package (compatibility is re-checked on the node).
    /// </summary>
    [MemoryPackOrder(10)]
    public ParaViewRuntimeRequirementData Runtime { get; set; } = new();

    /// <summary>
    /// Digest of the whole package (state + every attachment), part of the task identity.
    /// </summary>
    [MemoryPackOrder(11)]
    public string PackageDigest { get; set; } = string.Empty;

    /// <summary>
    /// Dataset identity component of the task identity; reserved for the dataset-set scenario, empty in version 1.
    /// </summary>
    [MemoryPackOrder(12)]
    public string DatasetId { get; set; } = string.Empty;

    /// <summary>
    /// Total declared bytes of <see cref="Attachments"/> plus the state — what the node will download.
    /// </summary>
    [MemoryPackOrder(13)]
    public long SubsetBytes { get; set; }

    /// <summary>
    /// Position of this task in its camera orbit (0 when the job renders no turntable). Appended in
    /// Model 0.2.0.
    /// </summary>
    [MemoryPackOrder(14)]
    public int OrbitIndex { get; set; }

    /// <summary>
    /// Camera azimuth in degrees the node applies to the state's camera about the orbit axis before
    /// rendering (0 renders the captured camera as is). Appended in Model 0.2.0.
    /// </summary>
    [MemoryPackOrder(15)]
    public double AzimuthDegrees { get; set; }

    /// <summary>
    /// Camera elevation in degrees the node applies about the camera's right axis before
    /// rendering (0 keeps the captured height). Appended in Model 0.4.0.
    /// </summary>
    [MemoryPackOrder(16)]
    public double ElevationDegrees { get; set; }

    /// <summary>
    /// Factor the node applies to the camera's distance from the focal point before rendering
    /// (1 keeps the captured distance). Appended in Model 0.4.0.
    /// </summary>
    [MemoryPackOrder(17)]
    public double DollyFactor { get; set; } = 1.0;

    #endregion
}
