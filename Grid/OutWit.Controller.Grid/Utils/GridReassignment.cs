using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public static async Task<DistributionOutcome> DistributeAsync(
        IReadOnlyList<IWitEngineActivityNode> nodes,
        IReadOnlyList<WitGridTask> tasks,
        Func<WitGridTaskGroup, CancellationToken, Task<GroupProcessResult>> processGroup,
        Action<string>? logWarning,
        CancellationToken cancellationToken)
    {
        var outcome = new DistributionOutcome();

        var pending = tasks.ToList();
        var failedNodeIds = new HashSet<Guid>();
        IWitProcessingStatus? lastFailureStatus = null;

        while (pending.Count > 0)
        {
            var healthyNodes = nodes.Where(node => !failedNodeIds.Contains(node.NodeId)).ToList();
            if (healthyNodes.Count == 0)
            {
                outcome.Failed = true;
                outcome.FailureMessage = failedNodeIds.Count == 0
                    ? "No compatible nodes are available to process the distributed tasks."
                    : lastFailureStatus?.Message
                      ?? "All compatible nodes failed while processing their assigned tasks.";
                return outcome;
            }

            IReadOnlyList<WitGridTaskGroup> groups = WitGridTaskAllocator.Allocate(healthyNodes, pending);

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

                    foreach (var variable in result.Variables)
                        outcome.Results.Add(variable.Value);
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
}
