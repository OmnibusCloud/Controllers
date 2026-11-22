namespace OutWit.Controller.AI.Corpus.Shards;

/// <summary>What the probe learned.</summary>
/// <param name="EstimatedBytes">Bytes of the sources the manifest gave no size for: the origin's answers plus the mean of them for the rest.</param>
/// <param name="Probed">Unsized sources whose size the origin gave.</param>
/// <param name="Failed">Unsized sources asked without an answer.</param>
/// <param name="Unprobed">Unsized sources not asked: beyond the probe budget, or on a host this machine does not fetch from.</param>
public sealed record ShardSizeProbeResult(long EstimatedBytes, int Probed, int Failed, int Unprobed)
{
    /// <summary>Every source's size as the work estimate counts it, aligned with the sources: the manifest's, the origin's answer, or the mean of the answers for a source with neither (0 when there is no answer at all).</summary>
    public IReadOnlyList<long> Sizes { get; init; } = [];

    /// <summary>The size every source is held to by the nodes, aligned with the sources: the manifest's or the origin's answer; 0 for a source with neither, which is held to the cap only.</summary>
    public IReadOnlyList<long> Declared { get; init; } = [];

    /// <summary>Sources with no size of their own after the probe.</summary>
    public int Unsized { get; init; }

    /// <summary>Sized sources the origin was asked about and answered for.</summary>
    public int Verified { get; init; }

    /// <summary>Sized sources of the sample whose size at the origin is past what their declaration allows: a node would refuse them.</summary>
    public IReadOnlyList<ShardSizeMismatch> Overdeclared { get; init; } = [];

    /// <summary>The mean of the origin's answers for unsized sources; 0 when there is none.</summary>
    public long MeanBytes { get; init; }
}
