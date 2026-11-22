using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// A reference to a task package: the item <c>Grid.ForEach</c> hands a node. The tasks themselves
/// (one shard each, with the pipeline and the origins once for all of them) are a blob; this
/// reference carries what is needed before the blob is opened and nothing that grows with the
/// manifest: where the blob is and what it must hash to, how many shards it holds and how many
/// bytes they are on the wire (the work estimate and the node's picture of its share), what a
/// node must be able to do to take it (the controller version, the PDF sandbox, room for the
/// largest shard), and the package's ticket, which the node's answer must bring back.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusTaskPackageData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusTaskPackageData other)
            return false;

        return TasksBlobId.Is(other.TasksBlobId)
               && Sha256.Is(other.Sha256)
               && SizeBytes.Is(other.SizeBytes)
               && ShardCount.Is(other.ShardCount)
               && ShardBytes.Is(other.ShardBytes)
               && LargestShardBytes.Is(other.LargestShardBytes)
               && NeedsPdfSandbox.Is(other.NeedsPdfSandbox)
               && Ticket.Is(other.Ticket)
               && RequiredControllerVersion.Is(other.RequiredControllerVersion)
               && UnsizedShards.Is(other.UnsizedShards);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusTaskPackageData Clone()
    {
        return new CorpusTaskPackageData
        {
            TasksBlobId = TasksBlobId,
            Sha256 = Sha256,
            SizeBytes = SizeBytes,
            ShardCount = ShardCount,
            ShardBytes = ShardBytes,
            LargestShardBytes = LargestShardBytes,
            NeedsPdfSandbox = NeedsPdfSandbox,
            Ticket = Ticket,
            RequiredControllerVersion = RequiredControllerVersion,
            UnsizedShards = UnsizedShards
        };
    }

    #endregion

    #region Properties

    /// <summary>The blob that holds the package's tasks.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public Guid TasksBlobId { get; set; }

    /// <summary>Lower-case hex SHA-256 of the blob's bytes: a node runs only the package its reference names.</summary>
    [MemoryPackOrder(1)]
    public string Sha256 { get; set; } = "";

    /// <summary>The blob's size in bytes.</summary>
    [ToString]
    [MemoryPackOrder(2)]
    public long SizeBytes { get; set; }

    /// <summary>The shards in the package.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int ShardCount { get; set; }

    /// <summary>The package's shards on the wire, together (a shard without a known size is not in it): the work estimate.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public long ShardBytes { get; set; }

    /// <summary>The largest shard of the package on the wire; 0 when no size is known. A node without room for it refuses its batch.</summary>
    [MemoryPackOrder(5)]
    public long LargestShardBytes { get; set; }

    /// <summary>Whether the package holds a WARC shard with the PDF stage on: a node without a PDF sandbox refuses its batch.</summary>
    [MemoryPackOrder(6)]
    public bool NeedsPdfSandbox { get; set; }

    /// <summary>
    /// The package's ticket: 16 random bytes of the server as 32 lower-case hex characters, new
    /// for every package of every round. A node's answer brings it back, and the server opens an
    /// answer only under a ticket of that round. Never written to a document a lab sees.
    /// </summary>
    [MemoryPackOrder(7)]
    public string Ticket { get; set; } = "";

    /// <summary>The controller version of the server that made the package. A node on another version refuses its batch before doing anything.</summary>
    [ToString]
    [MemoryPackOrder(8)]
    public string RequiredControllerVersion { get; set; } = "";

    /// <summary>The shards of the package whose size is not known: each is weighed as a typical shard in the work estimate.</summary>
    [MemoryPackOrder(9)]
    public int UnsizedShards { get; set; }

    #endregion
}
