using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Model;

public static class WitGridTaskAllocator
{
    #region Constants

    private const string ERROR_NO_MACHINE_FOR_COPY = "A copy of an item has no machine left that does not already hold one.";

    private const string ERROR_NO_NODES = "There are no nodes to place the tasks on.";

    private static readonly IReadOnlyDictionary<int, HashSet<Guid>> NO_HOLDERS = new Dictionary<int, HashSet<Guid>>();

    #endregion

    #region Functions

    /// <summary>
    /// Splits the tasks over the nodes: longest tasks first, each to the node that finishes it
    /// earliest by its planning rate, then the node count is trimmed while the makespan does not
    /// grow beyond the threshold. Tasks that are copies of one item land on different nodes.
    /// </summary>
    /// <remarks>
    /// A node without a measured benchmark for the activity (a default row, zero iterations)
    /// ranks after every measured node and is planned with the slowest measured rate, never with
    /// the default of exactly 1.0: a machine nobody has timed must not outrank one that was
    /// measured slow. When nothing is measured, every node keeps its own rate. The engine's
    /// dispatch selector ranks unmeasured nodes last in the same way.
    /// </remarks>
    /// <param name="nodes">Candidate nodes with their benchmark result for the activity.</param>
    /// <param name="tasks">The tasks to place.</param>
    /// <param name="improvementThresholdPct">How much makespan a smaller node count may cost (0.02 = 2%).</param>
    /// <returns>One group per node that received work.</returns>
    /// <exception cref="InvalidOperationException">An item has more copies than there are machines for them.</exception>
    public static IReadOnlyList<WitGridTaskGroup> Allocate(IReadOnlyList<IWitEngineActivityNode> nodes, IReadOnlyList<WitGridTask> tasks,
        double improvementThresholdPct = 0.02)
    {
        return Allocate(nodes, tasks, NO_HOLDERS, improvementThresholdPct);
    }

    /// <summary>
    /// Splits the tasks over the nodes like the plain overload, and also keeps each copy away from
    /// the machines that already hold a result for its item.
    /// </summary>
    /// <param name="nodes">Candidate nodes with their benchmark result for the activity.</param>
    /// <param name="tasks">The tasks to place.</param>
    /// <param name="holders">For each item, the machines that already returned a copy of it.</param>
    /// <param name="improvementThresholdPct">How much makespan a smaller node count may cost (0.02 = 2%).</param>
    /// <returns>One group per node that received work.</returns>
    /// <exception cref="InvalidOperationException">There are tasks and no nodes, or an item has more copies than there are machines for them.</exception>
    public static IReadOnlyList<WitGridTaskGroup> Allocate(IReadOnlyList<IWitEngineActivityNode> nodes, IReadOnlyList<WitGridTask> tasks,
        IReadOnlyDictionary<int, HashSet<Guid>> holders, double improvementThresholdPct = 0.02)
    {
        var candidates = Rank(nodes);

        if (candidates.Count == 0 && tasks.Count > 0)
            throw new InvalidOperationException(ERROR_NO_NODES);

        tasks = tasks.OrderByDescending(task => task.Work).ToList();

        // With copies every machine stays a candidate: a machine free of an item may be the slowest one.
        int maxNodesToUse = tasks.Any(task => task.ReplicaOf != null)
            ? candidates.Count
            : Math.Min(candidates.Count, tasks.Count);

        IReadOnlyList<WitGridTaskGroup> optimalAllocation = Allocate(maxNodesToUse, candidates, tasks, holders)
            ?? throw new InvalidOperationException(ERROR_NO_MACHINE_FOR_COPY);
        double bestMakespan = optimalAllocation.Max(group => group.Eta.TotalSeconds);

        for (int nodeCount = maxNodesToUse - 1; nodeCount >= 1; nodeCount--)
        {
            IReadOnlyList<WitGridTaskGroup>? currentAllocation = Allocate(nodeCount, candidates, tasks, holders);
            if (currentAllocation == null)
                break;

            double currentMakespan = currentAllocation.Max(group => group.Eta.TotalSeconds);

            if (currentMakespan <= bestMakespan * (1.0 + improvementThresholdPct))
            {
                optimalAllocation = currentAllocation;
                bestMakespan = currentMakespan;
            }
            else
                break;
        }

        return optimalAllocation;
    }

    /// <summary>
    /// Orders the nodes for planning and fixes the rate each one is planned with.
    /// </summary>
    /// <param name="nodes">Candidate nodes.</param>
    /// <returns>Measured nodes by rate, then unmeasured nodes by rate, each with its planning rate.</returns>
    public static IReadOnlyList<(IWitEngineActivityNode Node, double Rate)> Rank(IReadOnlyList<IWitEngineActivityNode> nodes)
    {
        var measured = nodes.Where(IsMeasured).OrderByDescending(node => node.BenchmarkResult.Rate).ToList();
        var unmeasured = nodes.Where(node => !IsMeasured(node)).OrderByDescending(node => node.BenchmarkResult.Rate).ToList();

        double? floor = measured.Count > 0 ? measured.Min(node => node.BenchmarkResult.Rate) : null;

        return measured.Select(node => (node, node.BenchmarkResult.Rate))
            .Concat(unmeasured.Select(node => (node, floor.HasValue ? Math.Min(node.BenchmarkResult.Rate, floor.Value) : node.BenchmarkResult.Rate)))
            .ToList();
    }

    private static bool IsMeasured(IWitEngineActivityNode node)
    {
        return node.BenchmarkResult.Iterations > 0;
    }

    /// <summary>Places the tasks on the first <paramref name="k"/> nodes; null when a copy has no machine there.</summary>
    private static List<WitGridTaskGroup>? Allocate(int k, IReadOnlyList<(IWitEngineActivityNode Node, double Rate)> nodes, IReadOnlyList<WitGridTask> tasks,
        IReadOnlyDictionary<int, HashSet<Guid>> holders)
    {
        IReadOnlyList<WitGridTaskGroup> groups
            = nodes.Take(k).Select(entry => new WitGridTaskGroup(entry.Node, entry.Rate)).ToArray();

        var placed = new Dictionary<int, HashSet<Guid>>();

        foreach (var task in tasks)
        {
            WitGridTaskGroup? bestGroup = null;
            double minCompletionTime = double.MaxValue;

            foreach (var group in groups)
            {
                if (HoldsItem(group, task, holders) || HoldsItem(group, task, placed))
                    continue;

                double completionTime = (group.TotalWork + task.Work) / group.Rate;

                if (bestGroup == null || completionTime < minCompletionTime)
                {
                    minCompletionTime = completionTime;
                    bestGroup = group;
                }
            }

            if (bestGroup == null)
                return null;

            bestGroup.Add(task);

            if (task.ReplicaOf is int item)
            {
                if (!placed.TryGetValue(item, out var machines))
                    placed[item] = machines = new HashSet<Guid>();

                machines.Add(bestGroup.Node.NodeId);
            }
        }

        return groups.Where(group => group.Count > 0).ToList();
    }

    private static bool HoldsItem(WitGridTaskGroup group, WitGridTask task, IReadOnlyDictionary<int, HashSet<Guid>> holders)
    {
        return task.ReplicaOf is int item
               && holders.TryGetValue(item, out var machines)
               && machines.Contains(group.Node.NodeId);
    }

    #endregion
}
