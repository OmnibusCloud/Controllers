using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Visualization.ParaView.Model;

/// <summary>
/// Blob-backed reference to an OmnibusCloud ParaView package: the saved state (.pvsm) blob, every
/// content-addressed attachment the state refers to by logical path, the runtime requirements, and
/// the producer's view of the timeline. The package is validated server-side independently of any
/// plugin-side validation; the controller never uses a client absolute path.
/// </summary>
[JobDocumentContract("paraview.sceneRef@1")]
[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class ParaViewSceneRefData : ModelBase
{
    #region ModelBase

    /// <inheritdoc />
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not ParaViewSceneRefData other)
            return false;

        return StateBlobId.Is(other.StateBlobId)
               && StateSha256.Is(other.StateSha256)
               && StateSize.Is(other.StateSize)
               && Attachments.Count == other.Attachments.Count
               && Attachments.Zip(other.Attachments, (left, right) => left.Is(right, tolerance)).All(me => me)
               && Runtime.Is(other.Runtime, tolerance)
               && TimestepValues.Count == other.TimestepValues.Count
               && TimestepValues.Zip(other.TimestepValues, (left, right) => left.Is(right, tolerance)).All(me => me)
               && PackageManifestJson.Is(other.PackageManifestJson);
    }

    /// <inheritdoc />
    public override ModelBase Clone()
    {
        return new ParaViewSceneRefData
        {
            StateBlobId = StateBlobId,
            StateSha256 = StateSha256,
            StateSize = StateSize,
            Attachments = [.. Attachments.Select(me => (ParaViewAttachmentRefData)me.Clone())],
            Runtime = (ParaViewRuntimeRequirementData)Runtime.Clone(),
            TimestepValues = [.. TimestepValues],
            PackageManifestJson = PackageManifestJson
        };
    }

    #endregion

    #region Properties

    /// <summary>
    /// Blob identifier of the saved ParaView state (.pvsm).
    /// </summary>
    [MemoryPackOrder(0)]
    public Guid StateBlobId { get; set; }

    /// <summary>
    /// Lower-case hexadecimal SHA-256 digest of the state file; verified against the blob before validation.
    /// </summary>
    [MemoryPackOrder(1)]
    public string StateSha256 { get; set; } = string.Empty;

    /// <summary>
    /// Declared byte size of the state file.
    /// </summary>
    [MemoryPackOrder(2)]
    public long StateSize { get; set; }

    /// <summary>
    /// Every file the state refers to, by normalized logical path, with digest, size, role, and series metadata.
    /// </summary>
    [MemoryPackOrder(3)]
    public List<ParaViewAttachmentRefData> Attachments { get; set; } = [];

    /// <summary>
    /// Producing ParaView version and required plugins.
    /// </summary>
    [MemoryPackOrder(4)]
    public ParaViewRuntimeRequirementData Runtime { get; set; } = new();

    /// <summary>
    /// The timeline (TimeKeeper timestep values) as the producer saw it. The state's own TimeKeeper
    /// is authoritative when the state carries one; the validation report records the resolved timeline.
    /// Empty for a static scene.
    /// </summary>
    [MemoryPackOrder(5)]
    public List<double> TimestepValues { get; set; } = [];

    /// <summary>
    /// The producer's package manifest (com.omnibuscloud.paraview-package) as opaque provenance,
    /// bounded in size. It never drives a controller decision — the typed members above do.
    /// </summary>
    [MemoryPackOrder(6)]
    public string PackageManifestJson { get; set; } = string.Empty;

    #endregion
}
