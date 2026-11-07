using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// One file of the base case tree, as the node materialises it: the blob that
/// holds its bytes and the path it takes inside the case directory. A file
/// that carries substitution tokens says so, and only those files are rewritten
/// on the node; the rest are copied byte for byte (binary fields included).
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamFileRefData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamFileRefData file)
            return false;

        return RelativePath.Is(file.RelativePath)
               && BlobId.Is(file.BlobId)
               && Sha256.Is(file.Sha256)
               && Size.Is(file.Size)
               && Templated.Is(file.Templated);
    }

    public override FoamFileRefData Clone()
    {
        return new FoamFileRefData
        {
            RelativePath = RelativePath,
            BlobId = BlobId,
            Sha256 = Sha256,
            Size = Size,
            Templated = Templated
        };
    }

    public override string ToString()
    {
        return $"{RelativePath} ({Size} B{(Templated ? ", templated" : "")})";
    }

    #endregion

    #region Properties

    /// <summary>
    /// Path inside the case directory, with forward slashes and no leading
    /// separator (<c>system/controlDict</c>, <c>0/U</c>). Never absolute,
    /// never escaping the case, never containing a space.
    /// </summary>
    [MemoryPackOrder(0)]
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Blob id of the file's bytes as uploaded by the initiator.</summary>
    [MemoryPackOrder(1)]
    public Guid BlobId { get; set; }

    /// <summary>SHA-256 of the bytes, lower-case hex; the sweep identity is built from these.</summary>
    [MemoryPackOrder(2)]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Size in bytes.</summary>
    [MemoryPackOrder(3)]
    public long Size { get; set; }

    /// <summary>True when the file carries substitution tokens and is rewritten per variant.</summary>
    [MemoryPackOrder(4)]
    public bool Templated { get; set; }

    #endregion
}
