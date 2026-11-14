using OutWit.Controller.AI.Corpus.Storage;

namespace OutWit.Controller.AI.Corpus.Diagnostics;

/// <summary>
/// The busiest node's share of a run's work, as the platform's allocator would split the shards over
/// the pool now. The allocator (<c>WitGridTaskAllocator</c> of the Grid controller) is not a
/// dependency of this controller, so its rule is followed here, step for step, and a test holds the
/// two together: the nodes ranked by benchmark rate (a node that has not measured itself after every
/// one that has, at the slowest measured rate); the tasks largest first, each to the node that
/// finishes it earliest; the two executions of a sampled shard never on one node; then the node count
/// trimmed while the longest share grows by no more than 2 %. A task's work is its shard's size in
/// megabytes, as <c>Corpus.ProcessShard</c> estimates it. Which shards the platform samples is random;
/// the plan takes the largest ones, as the storage estimate does. Every node counts as a machine of
/// its own here. The answer is a share and counts: no node is named.
/// </summary>
public static class CorpusSharePlanner
{
    #region Constants

    /// <summary>How much longer the longest share may get when the plan uses one node fewer (the allocator's default, 2 %).</summary>
    public const double IMPROVEMENT_THRESHOLD = 0.02;

    /// <summary>The work of a shard whose size is not known: one typical Common Crawl WET file, in megabytes (as <c>Corpus.ProcessShard</c> estimates it).</summary>
    public const double DEFAULT_SHARD_MEGABYTES = 64;

    /// <summary>The executions of a sampled shard: the original and one copy on another node.</summary>
    public const int SAMPLED_EXECUTIONS = 2;

    private const double BYTES_PER_MIB = 1024.0 * 1024.0;

    /// <summary>The slowest rate the allocator plans a node with.</summary>
    private const double MIN_RATE = 1e-7;

    #endregion

    #region Functions

    /// <summary>Plans a run.</summary>
    /// <param name="nodes">The pool: the nodes that can take Corpus shards now.</param>
    /// <param name="shardBytes">Each shard's size on the wire (0 or less: not known).</param>
    /// <param name="samplingRate">The share of the shards that run twice, 0 to 1.</param>
    /// <returns>The plan's counts and the busiest share.</returns>
    public static CorpusSharePlan Plan(IReadOnlyList<CorpusShareNode> nodes, IReadOnlyList<long> shardBytes, double samplingRate)
    {
        if (nodes.Count == 0 || shardBytes.Count == 0)
            return CorpusSharePlan.NONE with { PoolNodes = nodes.Count };

        var tasks = Tasks(shardBytes, nodes.Count > 1 ? CorpusStorageEstimator.SampledShards(shardBytes.Count, samplingRate) : 0);
        var ranked = Rank(nodes);

        // With copies every node stays a candidate: a node free of an item may be the slowest one.
        var maxNodes = tasks.Any(task => task.Item != null) ? ranked.Count : Math.Min(ranked.Count, tasks.Count);
        var best = Allocate(maxNodes, ranked, tasks);
        if (best == null)
            return CorpusSharePlan.NONE with { PoolNodes = nodes.Count };

        var bestMakespan = Makespan(best);
        for (var count = maxNodes - 1; count >= 1; count--)
        {
            var current = Allocate(count, ranked, tasks);
            if (current == null)
                break;

            var makespan = Makespan(current);
            if (makespan > bestMakespan * (1.0 + IMPROVEMENT_THRESHOLD))
                break;

            best = current;
            bestMakespan = makespan;
        }

        var planned = best.Where(group => group.Count > 0).ToList();
        var total = planned.Sum(group => group.Work);
        return new CorpusSharePlan(
            nodes.Count,
            planned.Count,
            planned.Count(group => !group.Measured),
            total > 0 ? planned.Max(group => group.Work) / total : 0);
    }

    /// <summary>A shard's work as <c>Corpus.ProcessShard</c> estimates it: its megabytes on the wire, at least 1, or a typical shard when its size is not known.</summary>
    /// <param name="sizeBytes">The shard's size (0 or less: not known).</param>
    /// <returns>The work units.</returns>
    public static double Work(long sizeBytes)
    {
        return sizeBytes > 0 ? Math.Max(1, sizeBytes / BYTES_PER_MIB) : DEFAULT_SHARD_MEGABYTES;
    }

    /// <summary>The executions, largest first: every shard once, and the largest <paramref name="sampled"/> a second time.</summary>
    private static List<PlannedTask> Tasks(IReadOnlyList<long> shardBytes, int sampled)
    {
        var copied = Enumerable.Range(0, shardBytes.Count)
            .OrderByDescending(index => Work(shardBytes[index]))
            .Take(sampled)
            .ToHashSet();

        var tasks = new List<PlannedTask>(shardBytes.Count + sampled);
        for (var index = 0; index < shardBytes.Count; index++)
            tasks.Add(new PlannedTask(Work(shardBytes[index]), copied.Contains(index) ? index : null));

        foreach (var index in copied.Order())
        {
            for (var copy = 1; copy < SAMPLED_EXECUTIONS; copy++)
                tasks.Add(new PlannedTask(Work(shardBytes[index]), index));
        }

        return tasks.OrderByDescending(task => task.Work).ToList();
    }

    /// <summary>Measured nodes by rate, then the others by rate at the slowest measured rate (their own when none has measured itself).</summary>
    private static List<(double Rate, bool Measured)> Rank(IReadOnlyList<CorpusShareNode> nodes)
    {
        var measured = nodes.Where(node => node.Measured).OrderByDescending(node => node.Rate).ToList();
        var unmeasured = nodes.Where(node => !node.Measured).OrderByDescending(node => node.Rate).ToList();
        double? floor = measured.Count > 0 ? measured.Min(node => node.Rate) : null;

        return measured.Select(node => (node.Rate, true))
            .Concat(unmeasured.Select(node => (floor.HasValue ? Math.Min(node.Rate, floor.Value) : node.Rate, false)))
            .ToList();
    }

    /// <summary>Places the tasks on the first <paramref name="count"/> nodes; null when a copy has no node left without its item.</summary>
    private static List<PlannedGroup>? Allocate(int count, IReadOnlyList<(double Rate, bool Measured)> nodes, IReadOnlyList<PlannedTask> tasks)
    {
        var groups = nodes.Take(count).Select(node => new PlannedGroup(Math.Max(node.Rate, MIN_RATE), node.Measured)).ToList();

        foreach (var task in tasks)
        {
            PlannedGroup? bestGroup = null;
            var bestCompletion = double.MaxValue;
            foreach (var group in groups)
            {
                if (task.Item is { } item && group.Items.Contains(item))
                    continue;

                var completion = (group.Work + task.Work) / group.Rate;
                if (bestGroup == null || completion < bestCompletion)
                {
                    bestCompletion = completion;
                    bestGroup = group;
                }
            }

            if (bestGroup == null)
                return null;

            bestGroup.Add(task);
        }

        return groups;
    }

    private static double Makespan(IReadOnlyList<PlannedGroup> groups)
    {
        return groups.Where(group => group.Count > 0).Max(group => group.Work / group.Rate);
    }

    #endregion

    #region Types

    /// <summary>One execution: its work and, for a sampled shard, the shard it is an execution of.</summary>
    private sealed record PlannedTask(double Work, int? Item);

    /// <summary>One node's share in a plan.</summary>
    private sealed class PlannedGroup(double rate, bool measured)
    {
        public void Add(PlannedTask task)
        {
            Work += task.Work;
            Count++;
            if (task.Item is { } item)
                Items.Add(item);
        }

        public double Rate { get; } = rate;

        public bool Measured { get; } = measured;

        public double Work { get; private set; }

        public int Count { get; private set; }

        public HashSet<int> Items { get; } = [];
    }

    #endregion
}
