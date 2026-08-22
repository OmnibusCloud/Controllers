using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// Outcome of ParaView.Validate: whether the package, state, and options are admissible, every
/// error and warning found, the subsetting fallbacks recorded, and the resolved rendering plan
/// (view, timeline, timestep indices, output size) that ParaView.Split turns into tasks.
/// </summary>
[JobDocumentContract("paraview.validationReport@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ParaViewValidationReportData : ModelBase
{
    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewValidationReportData other)
            return false;

        return IsValid.Is(other.IsValid)
               && Errors.Is(other.Errors)
               && Warnings.Is(other.Warnings)
               && Fallbacks.Is(other.Fallbacks)
               && PackageDigest.Is(other.PackageDigest)
               && ResolvedViewId.Is(other.ResolvedViewId)
               && ResolvedTimestepIndices.Is(other.ResolvedTimestepIndices)
               && TimestepValues.Count == other.TimestepValues.Count
               && TimestepValues.Zip(other.TimestepValues, (left, right) => left.Is(right, tolerance)).All(me => me)
               && AttachmentCount.Is(other.AttachmentCount)
               && TotalAttachmentBytes.Is(other.TotalAttachmentBytes)
               && ProxyTypes.Is(other.ProxyTypes)
               && RequiredPlugins.Is(other.RequiredPlugins)
               && RuntimeVersion.Is(other.RuntimeVersion)
               && Width.Is(other.Width)
               && Height.Is(other.Height)
               && Format.Is(other.Format)
               && SeriesAnchors.Is(other.SeriesAnchors)
               && OutputCount.Is(other.OutputCount);
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewValidationReportData
        {
            IsValid = IsValid,
            Errors = [.. Errors],
            Warnings = [.. Warnings],
            Fallbacks = [.. Fallbacks],
            PackageDigest = PackageDigest,
            ResolvedViewId = ResolvedViewId,
            ResolvedTimestepIndices = [.. ResolvedTimestepIndices],
            TimestepValues = [.. TimestepValues],
            AttachmentCount = AttachmentCount,
            TotalAttachmentBytes = TotalAttachmentBytes,
            ProxyTypes = [.. ProxyTypes],
            RequiredPlugins = [.. RequiredPlugins],
            RuntimeVersion = RuntimeVersion,
            Width = Width,
            Height = Height,
            Format = Format,
            SeriesAnchors = [.. SeriesAnchors],
            OutputCount = OutputCount
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// True when no error was found; Split refuses an invalid report.
    /// </summary>
    [MemoryPackOrder(0)]
    public bool IsValid { get; set; }

    /// <summary>
    /// Permanent input failures (the job must not retry).
    /// </summary>
    [MemoryPackOrder(1)]
    public List<string> Errors { get; set; } = [];

    /// <summary>
    /// Non-fatal findings.
    /// </summary>
    [MemoryPackOrder(2)]
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Series groups that fell back to full-set transfer because their format gives no per-timestep association.
    /// </summary>
    [MemoryPackOrder(3)]
    public List<string> Fallbacks { get; set; } = [];

    /// <summary>
    /// Digest of the whole package (state + attachments) — the first component of every task identity.
    /// </summary>
    [MemoryPackOrder(4)]
    public string PackageDigest { get; set; } = string.Empty;

    /// <summary>
    /// Registration name of the view that will be rendered.
    /// </summary>
    [MemoryPackOrder(5)]
    public string ResolvedViewId { get; set; } = string.Empty;

    /// <summary>
    /// Timestep indices that will be rendered, in render order.
    /// </summary>
    [MemoryPackOrder(6)]
    public List<int> ResolvedTimestepIndices { get; set; } = [];

    /// <summary>
    /// The resolved timeline (TimeKeeper values); empty for a static scene.
    /// </summary>
    [MemoryPackOrder(7)]
    public List<double> TimestepValues { get; set; } = [];

    /// <summary>
    /// Number of attachments in the package.
    /// </summary>
    [MemoryPackOrder(8)]
    public int AttachmentCount { get; set; }

    /// <summary>
    /// Sum of the declared attachment sizes plus the state size.
    /// </summary>
    [MemoryPackOrder(9)]
    public long TotalAttachmentBytes { get; set; }

    /// <summary>
    /// Distinct proxy types the state instantiates, as group/type, sorted.
    /// </summary>
    [MemoryPackOrder(10)]
    public List<string> ProxyTypes { get; set; } = [];

    /// <summary>
    /// Plugin requirements of the package, as name@version.
    /// </summary>
    [MemoryPackOrder(11)]
    public List<string> RequiredPlugins { get; set; } = [];

    /// <summary>
    /// The ParaView runtime version the controller will render with.
    /// </summary>
    [MemoryPackOrder(12)]
    public string RuntimeVersion { get; set; } = string.Empty;

    /// <summary>
    /// Resolved output width in pixels.
    /// </summary>
    [MemoryPackOrder(13)]
    public int Width { get; set; }

    /// <summary>
    /// Resolved output height in pixels.
    /// </summary>
    [MemoryPackOrder(14)]
    public int Height { get; set; }

    /// <summary>
    /// Resolved output format.
    /// </summary>
    [MemoryPackOrder(15)]
    public ParaViewImageFormat Format { get; set; }

    /// <summary>
    /// Logical paths of the series anchors — the first file of every series group, which ParaView's
    /// series readers open at load and which therefore ships with every task.
    /// </summary>
    [MemoryPackOrder(16)]
    public List<string> SeriesAnchors { get; set; } = [];

    /// <summary>
    /// Number of outputs (tasks) the job will render: the resolved timesteps, multiplied or replaced
    /// by the turntable's orbit frames. 0 when the package is invalid. Appended in Model 0.2.0.
    /// </summary>
    [MemoryPackOrder(17)]
    public int OutputCount { get; set; }

    #endregion
}
