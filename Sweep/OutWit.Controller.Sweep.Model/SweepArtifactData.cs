using MemoryPack;
using OutWit.Cloud.Documents;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// One downloadable artifact of a variant: what it is and which blob holds
/// it - what a document client needs to fetch it and open it the right way.
/// </summary>
[MemoryPackable]
[JobDocumentContract("sweep.artifact@1")]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepArtifactData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepArtifactData artifact)
            return false;

        return Kind.Is(artifact.Kind)
               && BlobId.Is(artifact.BlobId)
               && Bytes.Is(artifact.Bytes);
    }

    public override SweepArtifactData Clone()
    {
        return new SweepArtifactData
        {
            Kind = Kind,
            BlobId = BlobId,
            Bytes = Bytes
        };
    }

    public override string ToString()
    {
        return Bytes > 0 ? $"{Kind} {BlobId} ({Bytes} bytes)" : $"{Kind} {BlobId}";
    }

    #endregion

    #region Properties

    /// <summary>What the artifact is.</summary>
    [MemoryPackOrder(0)]
    public SweepArtifactKind Kind { get; set; }

    /// <summary>Blob id of the artifact.</summary>
    [MemoryPackOrder(1)]
    public Guid BlobId { get; set; }

    /// <summary>Size in bytes when the node reported it; 0 = unknown.</summary>
    [MemoryPackOrder(2)]
    public long Bytes { get; set; }

    #endregion
}
