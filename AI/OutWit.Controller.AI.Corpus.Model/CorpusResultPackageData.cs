using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// A node's answer to a task package: a reference to the blob that holds one result for each
/// shard of the package, under the package's ticket. When the node could not answer for the
/// package as a whole (its task blob did not arrive, its results could not be uploaded), the
/// reference carries the fault's fixed code and no blob: every shard of the package then goes
/// back unanswered, as a fault of that node.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusResultPackageData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusResultPackageData other)
            return false;

        return Ticket.Is(other.Ticket)
               && ResultsBlobId.Is(other.ResultsBlobId)
               && Fault.Is(other.Fault);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusResultPackageData Clone()
    {
        return new CorpusResultPackageData { Ticket = Ticket, ResultsBlobId = ResultsBlobId, Fault = Fault };
    }

    #endregion

    #region Properties

    /// <summary>The ticket of the task package this answers, repeated from its reference.</summary>
    [MemoryPackOrder(0)]
    public string Ticket { get; set; } = "";

    /// <summary>The blob that holds the package's results, or Guid.Empty with a <see cref="Fault"/>.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public Guid ResultsBlobId { get; set; }

    /// <summary>"" for an answered package; otherwise the fixed code of the node's fault (a code of <see cref="CorpusShardVerdict.NodeFault"/>, never a text) that kept it from answering the package at all.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public string Fault { get; set; } = "";

    #endregion
}
