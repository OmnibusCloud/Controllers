using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Model;

public static class WitGridTaskAllocator
{
    #region Functions

    /// <summary>
    /// Splits the tasks over the nodes: longest tasks first, each to the node that finishes it
    /// earliest by its planning rate, then the node count is trimmed while the makespan does not
    /// grow beyond the threshold.
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
    public static IReadOnlyList<WitGridTaskGroup> Allocate(IReadOnlyList<IWitEngineActivityNode> nodes, IReadOnlyList<WitGridTask> tasks,
        double improvementThresholdPct = 0.02)
    {
        var candidates = Rank(nodes);
        tasks = tasks.OrderByDescending(task => task.Work).ToList();

        int maxNodesToUse = Math.Min(candidates.Count, tasks.Count);
        IReadOnlyList<WitGridTaskGroup> optimalAllocation = Allocate(maxNodesToUse, candidates, tasks);
        double bestMakespan = optimalAllocation.Max(group => group.Eta.TotalSeconds);

        for (int nodeCount = maxNodesToUse - 1; nodeCount >= 1; nodeCount--)
        {
            IReadOnlyList<WitGridTaskGroup> currentAllocation = Allocate(nodeCount, candidates, tasks);
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

    private static List<WitGridTaskGroup> Allocate(int k, IReadOnlyList<(IWitEngineActivityNode Node, double Rate)> nodes, IReadOnlyList<WitGridTask> tasks)
    {
        IReadOnlyList<WitGridTaskGroup> groups
            = nodes.Take(k).Select(entry => new WitGridTaskGroup(entry.Node, entry.Rate)).ToArray();

        foreach (var task in tasks)
        {
            WitGridTaskGroup bestGroup = groups[0];
            double minCompletionTime = (bestGroup.TotalWork + task.Work) / bestGroup.Rate;

            for (int i = 1; i < groups.Count; i++)
            {
                var currentGroup = groups[i];
                double currentCompletionTime = (currentGroup.TotalWork + task.Work) / currentGroup.Rate;

                if (currentCompletionTime < minCompletionTime)
                {
                    minCompletionTime = currentCompletionTime;
                    bestGroup = currentGroup;
                }
            }
            bestGroup.Add(task);
        }

        return groups.Where(group => group.Count > 0).ToList();
    }

    #endregion
}
