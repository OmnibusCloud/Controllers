using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OutWit.Controller.Grid.Builders;
using OutWit.Controller.Grid.Model;
using OutWit.Engine.Data.Activities;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Utils;

public static class ProcessingUtils
{
    public static async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessQueued(
        this IWitNodesManager nodesManager,
        IReadOnlyList<IWitEngineActivityNode> nodes,
        IReadOnlyList<WitGridTask> tasks,
        Guid jobId)
    {
        if (nodes.Count == 0)
            throw new InvalidOperationException("No compatible nodes available for queued processing.");

        var aggregateStatus = new WitProcessingStatus(Guid.Empty, jobId);
        var taskQueue = new ConcurrentQueue<(int Index, WitGridTask Task)>(
            tasks.Select((task, index) => (index, task)));

        var results = new ConcurrentDictionary<int, IReadOnlyList<IWitVariable>>();
        var failed = false;
        var failedMessage = string.Empty;

        var workers = nodes.Select(async node =>
        {
            while (!failed && taskQueue.TryDequeue(out var item))
            {
                IReadOnlyList<string> returnVariables = item.Task.Variables
                    .Where(variable => variable.IsReturnVariable())
                    .Select(variable => variable.Name)
                    .ToList();

                var (taskStatus, taskVariables) = await nodesManager.Process(
                    node.NodeId,
                    jobId,
                    item.Task.Activity,
                    item.Task.Variables,
                    returnVariables);

                lock (aggregateStatus)
                {
                    aggregateStatus.AddChild(taskStatus);
                }

                if (taskStatus.Result == WitProcessingResult.Failed)
                {
                    failed = true;
                    failedMessage = taskStatus.Message ?? "Queued task failed";
                    break;
                }

                results[item.Index] = taskVariables;
            }
        });

        await Task.WhenAll(workers);

        var aggregateVariables = results
            .OrderBy(pair => pair.Key)
            .SelectMany(pair => pair.Value)
            .ToList();

        if (failed)
            return (aggregateStatus.Failed(TimeSpan.Zero, failedMessage), aggregateVariables);

        return (aggregateStatus.Completed(TimeSpan.Zero), aggregateVariables);
    }

    public static Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(this IWitNodesManager me, WitGridTaskGroup group, Guid jobId, bool inParallel)
    {
        return me.Process(group, jobId, inParallel, exactNode: false);
    }

    /// <summary>
    /// Runs a node's batch.
    /// </summary>
    /// <param name="me">The nodes manager.</param>
    /// <param name="group">The node and its tasks.</param>
    /// <param name="jobId">The job.</param>
    /// <param name="inParallel">Whether the node may run the batch's tasks at once.</param>
    /// <param name="exactNode">True when the batch must run on exactly this node or come back failed.</param>
    /// <returns>The batch status and its return variables.</returns>
    public static async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(this IWitNodesManager me, WitGridTaskGroup group, Guid jobId, bool inParallel, bool exactNode)
    {
        _ = inParallel;

        var requests = group.Select(task => new WitNodeTaskRequest
        {
            Activity = task.Activity,
            Pool = task.Variables,
            ReturnVariables = task.Variables
            .Where(variable => variable.IsReturnVariable())
            .Select(variable => variable.Name)
            .ToList(),
            ExactNode = exactNode
        }).ToList();

        return await me.ProcessBatch(
            group.Node.NodeId,
            jobId,
            requests,
            inParallel);
    }
}