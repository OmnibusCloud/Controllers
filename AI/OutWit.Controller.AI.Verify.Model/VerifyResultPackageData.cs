using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// What one Verify.ExecutePackage activity returns: a reference to a blob holding the result
/// batches of the package, never the results themselves. A node's answer stays a list of
/// references however many tasks it ran and however much they printed.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyResultPackageData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyResultPackageData other)
            return false;

        return ResultsBlobId.Is(other.ResultsBlobId)
               && Sha256.Is(other.Sha256)
               && SizeBytes.Is(other.SizeBytes)
               && TaskCount.Is(other.TaskCount)
               && BatchCount.Is(other.BatchCount);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override VerifyResultPackageData Clone()
    {
        return new VerifyResultPackageData
        {
            ResultsBlobId = ResultsBlobId,
            Sha256 = Sha256,
            SizeBytes = SizeBytes,
            TaskCount = TaskCount,
            BatchCount = BatchCount
        };
    }

    #endregion

    #region Properties

    /// <summary>The blob with the package's result batches (the controller's package format).</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public Guid ResultsBlobId { get; set; }

    /// <summary>SHA-256 of the blob's bytes, lowercase hex; the host checks it before it reads the results.</summary>
    [MemoryPackOrder(1)]
    public string Sha256 { get; set; } = "";

    /// <summary>Size of the blob in bytes: what the node uploaded.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public long SizeBytes { get; set; }

    /// <summary>Tasks the package's results answer.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int TaskCount { get; set; }

    /// <summary>Result batches in the package, one per task batch executed.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public int BatchCount { get; set; }

    #endregion
}
