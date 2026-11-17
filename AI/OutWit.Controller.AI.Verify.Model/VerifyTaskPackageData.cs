using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Verify.Model;

/// <summary>
/// What one Verify.ExecutePackage activity is given: a reference to a blob holding the
/// package's task batches, never the tasks themselves. The message that assigns work to a
/// node stays a list of references whatever the size of the taskset; the node downloads the
/// blobs of its own share over the blob channel (in chunks, resumed after a break).
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class VerifyTaskPackageData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not VerifyTaskPackageData other)
            return false;

        return TasksBlobId.Is(other.TasksBlobId)
               && Sha256.Is(other.Sha256)
               && SizeBytes.Is(other.SizeBytes)
               && TaskCount.Is(other.TaskCount)
               && BatchCount.Is(other.BatchCount)
               && Ticket.Is(other.Ticket)
               && RequiredControllerVersion.Is(other.RequiredControllerVersion)
               && IndexDigest.Is(other.IndexDigest);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override VerifyTaskPackageData Clone()
    {
        return new VerifyTaskPackageData
        {
            TasksBlobId = TasksBlobId,
            Sha256 = Sha256,
            SizeBytes = SizeBytes,
            TaskCount = TaskCount,
            BatchCount = BatchCount,
            Ticket = Ticket,
            RequiredControllerVersion = RequiredControllerVersion,
            IndexDigest = IndexDigest
        };
    }

    #endregion

    #region Properties

    /// <summary>The blob with the package's task batches (the controller's package format).</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public Guid TasksBlobId { get; set; }

    /// <summary>SHA-256 of the blob's bytes, lowercase hex; the node checks it before it reads the tasks.</summary>
    [MemoryPackOrder(1)]
    public string Sha256 { get; set; } = "";

    /// <summary>Size of the blob in bytes: what the node will download.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public long SizeBytes { get; set; }

    /// <summary>Tasks in the package: the work estimate, known without opening the blob.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int TaskCount { get; set; }

    /// <summary>Task batches in the package; each is executed as one batch on the node.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public int BatchCount { get; set; }

    /// <summary>
    /// The package's ticket: 16 random bytes as 32 lower-case hex characters, made when the
    /// package is. It travels to the node with the package and comes back in the result; the
    /// server accepts a result only under a ticket it gave out. A node knows the tickets of its
    /// own packages only. Never written to a document.
    /// </summary>
    [MemoryPackOrder(5)]
    public string Ticket { get; set; } = "";

    /// <summary>
    /// The version of the controller that made the package. A node on another version refuses
    /// the package before it does anything, and the work goes to another node.
    /// </summary>
    [ToString]
    [MemoryPackOrder(6)]
    public string RequiredControllerVersion { get; set; } = "";

    /// <summary>
    /// SHA-256 over the indices of the package's tasks in ascending order (each as four bytes,
    /// low byte first), lowercase hex: what a result must answer, no task more and none less.
    /// </summary>
    [MemoryPackOrder(7)]
    public string IndexDigest { get; set; } = "";

    #endregion
}
