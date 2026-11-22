using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Controller.Grid.Builders;
using OutWit.Controller.Grid.Model;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Utils;

/// <summary>
/// Fault-tolerant distribution for Grid.ForEach: allocate tasks to nodes, and when a node FAILS its
/// batch, reassign that node's tasks to the remaining healthy nodes and retry — instead of failing the
/// whole job because one machine died. A node that fails is excluded for the rest of the job, so the
/// loop always makes progress and terminates: every round either clears the pending work or removes at
/// least one node, and the job fails only once NO node can take the work (a genuine error — e.g. a
/// scene that crashes Blender everywhere — surfaces with the last node's message).
///
/// The actual per-group processing is injected as a delegate, so this orchestration is unit-testable
/// without the engine; <see cref="WitActivityAdapterGridForEach"/> supplies the real one over
/// <c>IWitNodesManager</c>. The happy path (no failures) is behaviourally identical to a plain
/// allocate-and-run: groups are processed once, results collected in allocator order.
///
/// Copies of one item stay on different machines through every round, using only this call's record of
/// who returned which item. A copy with no machine left is dropped; an item fails the job only when it
/// has no result at all.
///
/// A node answers only what it was asked. A task asks for one value, under the return slot Grid gave
/// its activity, and a node returns the variable of that name: that is what every node has done since
/// the first Grid.ForEach. Here a batch's values are held to it: a value is kept when it comes under a
/// slot some task of the batch asked for, as many times as tasks of the batch asked for that name; any
/// other value is left out of the results (with a warning) and the rest of the batch stands. Nothing
/// is reordered and no batch fails for it, so a call whose nodes answer as asked returns exactly what
/// it returned before. What it gives: no machine has more values in a call's results than its tasks
/// asked for, so a controller that compares the copies of an item can count each value as one
/// machine's.
///
/// A value the return collection would refuse is an answer not given. A typed collection takes a list
/// whole or not at all, so one null or one value of another type would cost the call every value of
/// every node. Such a value is left out like an unasked one (with a warning that says how many and
/// why), the batch stands, and its task has no answer. A task without an answer is not sent again,
/// here or for a node that returned fewer values than tasks: the results come back short and the
/// caller decides. For a copy the machine still counts as holding the item (it has run it), so no
/// other copy of that item is placed on it.
/// </summary>
public static class GridReassignment
{
    /// <summary>Outcome of processing one node's task group.</summary>
    /// <param name="Succeeded">True when the node completed the whole batch.</param>
    /// <param name="Status">The node's processing status (added to the job's status tree on success).</param>
    /// <param name="Variables">The batch's return variables (empty when the transformer has no return).</param>
    public readonly record struct GroupProcessResult(
        bool Succeeded,
        IWitProcessingStatus? Status,
        IReadOnlyList<IWitVariable> Variables);

    public sealed class DistributionOutcome
    {
        /// <summary>Return values of all completed tasks, in allocator (node-rate) order.</summary>
        public List<object?> Results { get; } = new();

        /// <summary>Statuses of the groups that completed — the caller adds these to the job status tree.</summary>
        public List<IWitProcessingStatus> CompletedStatuses { get; } = new();

        /// <summary>True when the work could not be completed on any available node.</summary>
        public bool Failed { get; set; }

        /// <summary>Message for the genuine failure (the last failing node's message when available).</summary>
        public string? FailureMessage { get; set; }
    }

    /// <summary>
    /// Distributes the tasks of a call whose return collection takes every value.
    /// </summary>
    /// <param name="nodes">The nodes that can take the work.</param>
    /// <param name="tasks">The tasks of the call.</param>
    /// <param name="processGroup">Runs one node's batch.</param>
    /// <param name="logWarning">Where warnings go; null when nobody listens.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The values that were kept, the statuses of the batches that completed, and whether the work failed.</returns>
    /// <exception cref="OperationCanceledException">The call was cancelled.</exception>
    public static Task<DistributionOutcome> DistributeAsync(
        IReadOnlyList<IWitEngineActivityNode> nodes,
        IReadOnlyList<WitGridTask> tasks,
        Func<WitGridTaskGroup, CancellationToken, Task<GroupProcessResult>> processGroup,
        Action<string>? logWarning,
        CancellationToken cancellationToken)
    {
        return DistributeAsync(nodes, tasks, processGroup, logWarning, null, cancellationToken);
    }

    /// <summary>
    /// Distributes the tasks of a call and holds every answer to what its tasks asked for and to what
    /// the return collection takes.
    /// </summary>
    /// <param name="nodes">The nodes that can take the work.</param>
    /// <param name="tasks">The tasks of the call.</param>
    /// <param name="processGroup">Runs one node's batch.</param>
    /// <param name="logWarning">Where warnings go; null when nobody listens.</param>
    /// <param name="values">What the return collection takes; null when it takes every value.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The values that were kept, the statuses of the batches that completed, and whether the work failed.</returns>
    /// <exception cref="OperationCanceledException">The call was cancelled.</exception>
    public static async Task<DistributionOutcome> DistributeAsync(
        IReadOnlyList<IWitEngineActivityNode> nodes,
        IReadOnlyList<WitGridTask> tasks,
        Func<WitGridTaskGroup, CancellationToken, Task<GroupProcessResult>> processGroup,
        Action<string>? logWarning,
        GridValueRule? values,
        CancellationToken cancellationToken)
    {
        var outcome = new DistributionOutcome();

        var pending = tasks.ToList();
        var failedNodeIds = new HashSet<Guid>();
        var holders = new Dictionary<int, HashSet<Guid>>();
        IWitProcessingStatus? lastFailureStatus = null;

        while (pending.Count > 0)
        {
            var healthyNodes = nodes.Where(node => !failedNodeIds.Contains(node.NodeId)).ToList();

            pending = KeepPlaceableCopies(pending, healthyNodes, holders, logWarning);
            if (pending.Count == 0)
                break;

            if (healthyNodes.Count == 0)
            {
                outcome.Failed = true;
                outcome.FailureMessage = failedNodeIds.Count == 0
                    ? "No compatible nodes are available to process the distributed tasks."
                    : lastFailureStatus?.Message
                      ?? "All compatible nodes failed while processing their assigned tasks.";
                return outcome;
            }

            IReadOnlyList<WitGridTaskGroup> groups = WitGridTaskAllocator.Allocate(healthyNodes, pending, holders);

            var running = groups
                .Select(group => (Group: group, Task: processGroup(group, cancellationToken)))
                .ToList();

            // Honour cancellation while a node renders its whole batch; observe orphaned groups so the
            // cancelled run doesn't leak an unobserved exception.
            var allGroups = Task.WhenAll(running.Select(item => item.Task));
            try
            {
                await allGroups.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _ = allGroups.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
                throw;
            }

            var stillPending = new List<WitGridTask>();
            foreach (var (group, task) in running)
            {
                var result = task.Result;
                if (result.Succeeded)
                {
                    if (result.Status != null)
                        outcome.CompletedStatuses.Add(result.Status);

                    IReadOnlyList<IWitVariable> answers = GridAnswers.Keep(group, result.Variables, values, out int unasked, out var refused);

                    GridAnswers.Warn(logWarning, GridAnswers.Who(group.Node.NodeId, result.Status, "batch"), group.Count, unasked, refused, values);

                    foreach (var variable in answers)
                        outcome.Results.Add(variable.Value);

                    // A copy whose value was left out is recorded all the same: the machine has run the
                    // item, and another copy of it must not land there.
                    RecordHolder(holders, group);
                }
                else
                {
                    failedNodeIds.Add(group.Node.NodeId);
                    lastFailureStatus = result.Status;
                    stillPending.AddRange(group);

                    logWarning?.Invoke(
                        $"Node {group.Node.NodeId} failed {group.Count} task(s); reassigning them to other nodes. "
                        + (result.Status?.Message ?? string.Empty));
                }
            }

            pending = stillPending;
        }

        return outcome;
    }

    /// <summary>
    /// Keeps as many copies of each item as there are machines free of it. The first copy of an item
    /// with no result yet always stays: losing it would lose the item, which only the caller may decide.
    /// </summary>
    private static List<WitGridTask> KeepPlaceableCopies(
        List<WitGridTask> pending,
        IReadOnlyList<IWitEngineActivityNode> healthyNodes,
        Dictionary<int, HashSet<Guid>> holders,
        Action<string>? logWarning)
    {
        if (pending.All(task => task.ReplicaOf == null))
            return pending;

        var machines = healthyNodes.Select(node => node.NodeId).Distinct().ToList();
        var kept = new List<WitGridTask>(pending.Count);
        var keptCopies = new Dictionary<int, int>();

        foreach (var task in pending)
        {
            if (task.ReplicaOf is not int item)
            {
                kept.Add(task);
                continue;
            }

            holders.TryGetValue(item, out var holding);
            int freeMachines = machines.Count(machine => holding == null || !holding.Contains(machine));
            int keptSoFar = keptCopies.GetValueOrDefault(item);

            if (keptSoFar < freeMachines || (keptSoFar == 0 && holding == null))
            {
                kept.Add(task);
                keptCopies[item] = keptSoFar + 1;
            }
            else
                logWarning?.Invoke($"A copy of item {item} was dropped: no machine is left that does not already hold one.");
        }

        return kept;
    }

    private static void RecordHolder(Dictionary<int, HashSet<Guid>> holders, WitGridTaskGroup group)
    {
        foreach (var task in group)
        {
            if (task.ReplicaOf is not int item)
                continue;

            if (!holders.TryGetValue(item, out var machines))
                holders[item] = machines = new HashSet<Guid>();

            machines.Add(group.Node.NodeId);
        }
    }
}
