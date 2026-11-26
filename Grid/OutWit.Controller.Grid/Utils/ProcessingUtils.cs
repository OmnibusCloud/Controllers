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
    /// <summary>
    /// Runs the tasks under the Queued strategy for a call whose return collection takes every value:
    /// the nodes take one task after another from a common queue.
    /// </summary>
    /// <param name="nodesManager">The nodes manager.</param>
    /// <param name="nodes">The nodes that take from the queue.</param>
    /// <param name="tasks">The tasks of the call.</param>
    /// <param name="jobId">The job.</param>
    /// <returns>The status of the whole run and the values that were kept, in the order of the tasks.</returns>
    /// <exception cref="InvalidOperationException">There is no node to run the tasks.</exception>
    public static Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessQueued(
        this IWitNodesManager nodesManager,
        IReadOnlyList<IWitEngineActivityNode> nodes,
        IReadOnlyList<WitGridTask> tasks,
        Guid jobId)
    {
        return nodesManager.ProcessQueued(nodes, tasks, jobId, null, null);
    }

    /// <summary>
    /// Runs the tasks under the Queued strategy: the nodes take one task after another from a common
    /// queue. Every answer is held as a batch's answer is under the Balanced strategy: of what a node
    /// returns for a task, the value under the name that task asked for is kept, once, when the return
    /// collection would take it; anything else is left out with a warning, and the task then has no
    /// answer. A task without an answer is not sent again. A return collection that asks to be told
    /// which item each value answers is told of a value as it is kept: in the loop of the node that
    /// answered, so from several threads at once when several nodes answer.
    /// </summary>
    /// <param name="nodesManager">The nodes manager.</param>
    /// <param name="nodes">The nodes that take from the queue.</param>
    /// <param name="tasks">The tasks of the call.</param>
    /// <param name="jobId">The job.</param>
    /// <param name="values">What the return collection takes; null when it takes every value. The rule of a collection that asks to be told tells it from the nodes' own loops.</param>
    /// <param name="logWarning">Where warnings go; null when nobody listens. It is called from the nodes' own loops.</param>
    /// <returns>The status of the whole run and the values that were kept, in the order of the tasks.</returns>
    /// <exception cref="InvalidOperationException">There is no node to run the tasks.</exception>
    public static async Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> ProcessQueued(
        this IWitNodesManager nodesManager,
        IReadOnlyList<IWitEngineActivityNode> nodes,
        IReadOnlyList<WitGridTask> tasks,
        Guid jobId,
        GridValueRule? values,
        Action<string>? logWarning)
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
                IReadOnlyList<string> returnVariables = item.Task.AskedNames();

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

                IReadOnlyList<IWitVariable> answers = GridAnswers.Keep(new[] { item.Task }, taskVariables, values, out int unasked, out var refused);

                GridAnswers.Warn(logWarning, GridAnswers.Who(node.NodeId, taskStatus, "task"), 1, unasked, refused, values);

                results[item.Index] = answers;
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

    /// <summary>
    /// Runs a node's batch on that node or, when it cannot take it, on a node the server chooses.
    /// </summary>
    /// <param name="me">The nodes manager.</param>
    /// <param name="group">The node and its tasks.</param>
    /// <param name="jobId">The job.</param>
    /// <param name="inParallel">Whether the node may run the batch's tasks at once.</param>
    /// <returns>The batch status and its return variables.</returns>
    public static Task<(IWitProcessingStatus, IReadOnlyList<IWitVariable>)> Process(this IWitNodesManager me, WitGridTaskGroup group, Guid jobId, bool inParallel)
    {
        return me.Process(group, jobId, inParallel, exactNode: false);
    }

    /// <summary>
    /// Runs a node's batch. Each task asks the node for one value: the one under its own return slot.
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
            ReturnVariables = task.AskedNames(),
            ExactNode = exactNode
        }).ToList();

        return await me.ProcessBatch(
            group.Node.NodeId,
            jobId,
            requests,
            inParallel);
    }
}