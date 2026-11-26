using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Smt.Model;

/// <summary>
/// What one Smt.SolvePackage activity returns: a reference to a blob that holds a row of every
/// problem of the package (<see cref="SmtResultData"/>, in the controller's package format),
/// never the rows themselves. A node's answer stays a reference however many problems it solved
/// and however large their models.
/// <para>
/// When the node could not work on the package at all, <see cref="FaultCode"/> says why
/// (<see cref="SmtNodeFaults"/>: another controller version, a package that is not the one its
/// reference names, no solver module, not memory for one session) and there is no blob: the
/// package's problems have no answer from this node, and the work goes elsewhere. A problem that
/// fails on its own is never that: it is a row with its status.
/// </para>
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order. A published layout
// changes only behind a version fence, so that no reader of the old layout is ever handed the
// new one: MemoryPack throws on a payload of more members than its reader knows, and a member a
// sender may not have arrives as its type's default (null for a text or a list), never as its
// initializer; such a member starts at that default, and a reference one is declared nullable.
public sealed partial class SmtResultPackageData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SmtResultPackageData other)
            return false;

        return ResultsBlobId.Is(other.ResultsBlobId)
               && Sha256.Is(other.Sha256)
               && SizeBytes.Is(other.SizeBytes)
               && ResultCount.Is(other.ResultCount)
               && Ticket.Is(other.Ticket)
               && FaultCode.Is(other.FaultCode)
               && FaultMessage.Is(other.FaultMessage);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override SmtResultPackageData Clone()
    {
        return new SmtResultPackageData
        {
            ResultsBlobId = ResultsBlobId,
            Sha256 = Sha256,
            SizeBytes = SizeBytes,
            ResultCount = ResultCount,
            Ticket = Ticket,
            FaultCode = FaultCode,
            FaultMessage = FaultMessage
        };
    }

    #endregion

    #region Properties

    /// <summary>The blob with the package's rows (the controller's package format); empty when the node could not work on the package.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public Guid ResultsBlobId { get; set; }

    /// <summary>SHA-256 of the blob's bytes, lower-case hex; empty when the node could not work on the package. The host checks it before it reads a row.</summary>
    [MemoryPackOrder(1)]
    public string Sha256 { get; set; } = "";

    /// <summary>Size of the blob in bytes: what the node uploaded.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public long SizeBytes { get; set; }

    /// <summary>Rows in the blob: one for every problem of the package; 0 when the node could not work on it.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int ResultCount { get; set; }

    /// <summary>
    /// The ticket of the task package these rows answer, as the node was given it
    /// (<see cref="SmtTaskPackageData.Ticket"/>); empty when the package carried none in the form
    /// of a ticket. Never written to a document.
    /// </summary>
    [MemoryPackOrder(4)]
    public string Ticket { get; set; } = "";

    /// <summary>Empty when the node worked on the package; else why it could not, one code of <see cref="SmtNodeFaults"/>.</summary>
    [ToString]
    [MemoryPackOrder(5)]
    public string FaultCode { get; set; } = "";

    /// <summary>
    /// The sentence that goes with <see cref="FaultCode"/>, in fixed words: versions, sizes, hashes,
    /// never a path or a name of the machine; at most <see cref="SmtResultWords.MAX_MESSAGE_CHARACTERS"/>
    /// characters, and empty when the node worked on the package. A host takes a reference only in
    /// that form.
    /// </summary>
    [MemoryPackOrder(6)]
    public string FaultMessage { get; set; } = "";

    #endregion
}
