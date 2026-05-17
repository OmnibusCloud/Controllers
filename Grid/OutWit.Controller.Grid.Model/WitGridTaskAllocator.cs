using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Model;

public static class WitGridTaskAllocator
{
    public static IReadOnlyList<WitGridTaskGroup> Allocate(IReadOnlyList<IWitEngineActivityNode> nodes, IReadOnlyList<WitGridTask> tasks,
        double improvementThresholdPct = 0.02)
    {
        nodes = nodes.OrderByDescending(node => node.BenchmarkResult.Rate).ToList();
        tasks = tasks.OrderByDescending(task => task.Work).ToList();

        int maxNodesToUse = Math.Min(nodes.Count, tasks.Count);
        IReadOnlyList<WitGridTaskGroup> optimalAllocation = Allocate(maxNodesToUse, nodes, tasks);
        double bestMakespan = optimalAllocation.Max(group => group.Eta.TotalSeconds);
        
        for (int nodeCount = maxNodesToUse - 1; nodeCount >= 1; nodeCount--)
        {
            IReadOnlyList<WitGridTaskGroup> currentAllocation = Allocate(nodeCount, nodes, tasks);
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
    
    private static List<WitGridTaskGroup> Allocate(int k, IReadOnlyList<IWitEngineActivityNode> nodes, IReadOnlyList<WitGridTask> tasks)
    {
        IReadOnlyList<WitGridTaskGroup> groups 
            = nodes.Take(k).Select(node => new WitGridTaskGroup(node)).ToArray();

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
}