using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// One runtime a node reports as present and hash-verified.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyRuntimeInfoData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyRuntimeInfoData other)
            return false;

        return RuntimeId.Is(other.RuntimeId)
               && Sha256.Is(other.Sha256)
               && Available.Is(other.Available);
    }

    public override VerifyRuntimeInfoData Clone()
    {
        return new VerifyRuntimeInfoData
        {
            RuntimeId = RuntimeId,
            Sha256 = Sha256,
            Available = Available
        };
    }

    #endregion

    #region Properties

    [ToString]
    [MemoryPackOrder(0)]
    public string RuntimeId { get; set; } = "";

    /// <summary>Pinned SHA-256 the node verified the module against.</summary>
    [MemoryPackOrder(1)]
    public string Sha256 { get; set; } = "";

    /// <summary>True when the module is present on the node and passed its hash pin.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public bool Available { get; set; }

    #endregion
}
