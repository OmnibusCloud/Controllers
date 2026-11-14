using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Attributes;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.AI.Corpus.Model;

/// <summary>
/// What a lab learns before anything runs: how many shards, whether every origin is allowed,
/// which sources exceed the cap, how much traffic the contributors will carry, how much the
/// job will leave on the server and how much room the deployment gives one job, and every
/// problem with the manifest by line.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order — append new members at the END only.
public sealed partial class CorpusPreflightData : ModelBase
{
    #region Model Base

    /// <summary>Value comparison with another model: the same type and equal fields (the OutWit model paradigm).</summary>
    /// <param name="modelBase">The model to compare with.</param>
    /// <param name="tolerance">Tolerance for floating-point fields.</param>
    /// <returns>True when the models are equal by value.</returns>
    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not CorpusPreflightData other)
            return false;

        return WellFormed.Is(other.WellFormed)
               && TaskCount.Is(other.TaskCount)
               && RefusedOrigins.Is(other.RefusedOrigins)
               && OversizedSources.Is(other.OversizedSources)
               && EstimatedTrafficBytes.Is(other.EstimatedTrafficBytes)
               && Messages.Is(other.Messages)
               && EstimatedStorageBytes.Is(other.EstimatedStorageBytes)
               && StorageHeadroomBytes.Is(other.StorageHeadroomBytes)
               && StorageRetentionDays.Is(other.StorageRetentionDays)
               && BusiestNodeShare.Is(other.BusiestNodeShare, tolerance)
               && PlannedNodes.Is(other.PlannedNodes);
    }

    /// <summary>Deep copy.</summary>
    /// <returns>The copy.</returns>
    public override CorpusPreflightData Clone()
    {
        return new CorpusPreflightData
        {
            WellFormed = WellFormed,
            TaskCount = TaskCount,
            RefusedOrigins = [.. RefusedOrigins],
            OversizedSources = OversizedSources,
            EstimatedTrafficBytes = EstimatedTrafficBytes,
            Messages = [.. Messages],
            EstimatedStorageBytes = EstimatedStorageBytes,
            StorageHeadroomBytes = StorageHeadroomBytes,
            StorageRetentionDays = StorageRetentionDays,
            BusiestNodeShare = BusiestNodeShare,
            PlannedNodes = PlannedNodes
        };
    }

    #endregion

    #region Properties

    /// <summary>Whether every manifest line parsed and every source is allowed and within the cap.</summary>
    [ToString]
    [MemoryPackOrder(0)]
    public bool WellFormed { get; set; }

    /// <summary>Shards that parsed.</summary>
    [ToString]
    [MemoryPackOrder(1)]
    public int TaskCount { get; set; }

    /// <summary>Hosts the manifest names that are not on the allowlist.</summary>
    [MemoryPackOrder(2)]
    public List<string> RefusedOrigins { get; set; } = [];

    /// <summary>Sources whose estimated size exceeds the cap.</summary>
    [ToString]
    [MemoryPackOrder(3)]
    public int OversizedSources { get; set; }

    /// <summary>Sum of the sources' estimated sizes: what the contributors will fetch.</summary>
    [ToString]
    [MemoryPackOrder(4)]
    public long EstimatedTrafficBytes { get; set; }

    /// <summary>Every problem, by manifest line.</summary>
    [MemoryPackOrder(5)]
    public List<string> Messages { get; set; } = [];

    /// <summary>
    /// What the job will leave on the server for the retention window: the products of every
    /// execution (a sampled shard runs twice), the job's documents and its inputs. An estimate
    /// from measured ratios of product to shard bytes, on the side of too much.
    /// </summary>
    [ToString]
    [MemoryPackOrder(6)]
    public long EstimatedStorageBytes { get; set; }

    /// <summary>The most one job may leave on the server, as the deployment states it; 0 = not stated (no refusal on storage).</summary>
    [ToString]
    [MemoryPackOrder(7)]
    public long StorageHeadroomBytes { get; set; }

    /// <summary>Days the server keeps a job's products after they are made, as the deployment states it; 0 = not stated.</summary>
    [MemoryPackOrder(8)]
    public int StorageRetentionDays { get; set; }

    /// <summary>
    /// The busiest node's share of the work, 0 to 1, as the platform's allocator would split the
    /// shards over the pool now (by the nodes' benchmark rates, the sampled shards' second copies
    /// on other nodes): what the contributor with the most work fetches, as a share of the run's
    /// traffic. Anonymous: a share, never a node. 0 = not known (no pool the host can ask).
    /// </summary>
    [ToString]
    [MemoryPackOrder(9)]
    public double BusiestNodeShare { get; set; }

    /// <summary>The nodes that plan would give shards to (a count, never a node); 0 = not known.</summary>
    [ToString]
    [MemoryPackOrder(10)]
    public int PlannedNodes { get; set; }

    #endregion
}
