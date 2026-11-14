using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// The product of one shard task: the verdict, the uploaded product and signature blobs, the
/// content hash of the uncompressed product (the verification identity a re-execution must
/// reproduce) and the statistics.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusResultData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusResultData other)
            return false;

        return TaskIndex.Is(other.TaskIndex)
               && Verdict.Is(other.Verdict)
               && TextBlobId.Is(other.TextBlobId)
               && SignaturesBlobId.Is(other.SignaturesBlobId)
               && ContentSha256.Is(other.ContentSha256)
               && Error.Is(other.Error)
               && Stats.Check(other.Stats)
               && NodeId.Is(other.NodeId)
               && NodeName.Is(other.NodeName)
               && ControllerVersion.Is(other.ControllerVersion)
               && SourceUrl.Is(other.SourceUrl)
               && RuntimeTarget.Is(other.RuntimeTarget)
               && Degree.Is(other.Degree);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusResultData Clone()
    {
        return new CorpusResultData
        {
            TaskIndex = TaskIndex,
            Verdict = Verdict,
            TextBlobId = TextBlobId,
            SignaturesBlobId = SignaturesBlobId,
            ContentSha256 = ContentSha256,
            Error = Error,
            Stats = Stats?.Clone(),
            NodeId = NodeId,
            NodeName = NodeName,
            ControllerVersion = ControllerVersion,
            SourceUrl = SourceUrl,
            RuntimeTarget = RuntimeTarget,
            Degree = Degree
        };
    }

    #endregion

    #region Properties

    /// <summary>The task's index.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public int TaskIndex { get; set; }

    /// <summary>How the task ended.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public CorpusShardVerdict Verdict { get; set; }

    /// <summary>The compressed product (JSON lines of url, language, text), or Guid.Empty.</summary>
    [MemoryPackOrder(2)]
    public Guid TextBlobId { get; set; }

    /// <summary>The MinHash signatures of the product's documents, or Guid.Empty.</summary>
    [MemoryPackOrder(3)]
    public Guid SignaturesBlobId { get; set; }

    /// <summary>Lower-case hex SHA-256 of the uncompressed product: the identity a re-execution must match.</summary>
    [MemoryPackOrder(4)]
    public string ContentSha256 { get; set; } = "";

    /// <summary>Why the task did not complete, or "".</summary>
    [MemoryPackOrder(5)]
    public string Error { get; set; } = "";

    /// <summary>The shard's statistics (null when nothing was processed).</summary>
    [MemoryPackOrder(6)]
    public CorpusShardStatsData? Stats { get; set; }

    /// <summary>Not filled: a result does not say which machine ran it. Kept for the wire layout.</summary>
    [MemoryPackOrder(7)]
    public Guid NodeId { get; set; }

    /// <summary>Not filled: a result does not say which machine ran it. Kept for the wire layout.</summary>
    [MemoryPackOrder(8)]
    public string NodeName { get; set; } = "";

    /// <summary>The controller version the node ran.</summary>
    [MemoryPackOrder(9)]
    public string ControllerVersion { get; set; } = "";

    /// <summary>The shard's URL, repeated from the task so the report stands on its own.</summary>
    [MemoryPackOrder(10)]
    public string SourceUrl { get; set; } = "";

    /// <summary>The operating system and architecture the shard ran on, e.g. <c>linux-x64</c>: what tells two executions of a pair apart by platform, never by machine.</summary>
    [MemoryPackOrder(11)]
    public string RuntimeTarget { get; set; } = "";

    /// <summary>Shards the node processes at once (its cores, bounded by memory): the context of the processing time. 0 = not reported.</summary>
    [MemoryPackOrder(12)]
    public int Degree { get; set; }

    #endregion
}
