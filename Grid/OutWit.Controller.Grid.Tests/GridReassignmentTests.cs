using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Controller.Grid.Utils;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Grid.Tests
{
    /// <summary>
    /// Unit tests for the fault-tolerant Grid.ForEach distribution: a node that fails its batch has its
    /// tasks reassigned to healthy nodes (rather than failing the whole job), and the job fails only when
    /// no node can complete the work. The per-group processing is injected, so no engine round-trip.
    /// </summary>
    [TestFixture]
    public class GridReassignmentTests
    {
        private static WitGridTask MakeTask(double work = 1) => new()
        {
            Work = work,
            Variables = new WitVariableCollection(),
            Activity = new WitActivitySpecialTrace { Message = (WitReference)"obj" }
        };

        private static IWitEngineActivityNode Node(double rate) =>
            new MockActivityNode(WitEngineNodeSdk.Instance, rate, Guid.NewGuid());

        private static IWitProcessingStatus FailedStatus(string message) =>
            new WitProcessingStatus(Guid.Empty, Guid.NewGuid()).Failed(TimeSpan.Zero, message);

        private static GridReassignment.GroupProcessResult Ok() =>
            new(true, null, Array.Empty<IWitVariable>());

        private static GridReassignment.GroupProcessResult Fail(string message) =>
            new(false, FailedStatus(message), Array.Empty<IWitVariable>());

        [Test]
        public async Task AllNodesSucceed_EveryTaskProcessedOnce_NotFailed()
        {
            var nodes = new[] { Node(10), Node(20) };
            var tasks = Enumerable.Range(0, 10).Select(_ => MakeTask()).ToList();
            var processed = new ConcurrentBag<WitGridTask>();

            var outcome = await GridReassignment.DistributeAsync(
                nodes, tasks,
                (group, _) =>
                {
                    foreach (var task in group) processed.Add(task);
                    return Task.FromResult(Ok());
                },
                logWarning: null,
                CancellationToken.None);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(processed.Count, Is.EqualTo(10));
            Assert.That(processed.Distinct().Count(), Is.EqualTo(10), "each task processed exactly once");
        }

        [Test]
        public async Task FailedNode_TasksReassignedToHealthyNode_JobSucceeds()
        {
            var badNode = Node(20);   // faster → gets the larger share, but fails it
            var goodNode = Node(10);
            var nodes = new[] { badNode, goodNode };
            var tasks = Enumerable.Range(0, 10).Select(_ => MakeTask()).ToList();

            var completedByGood = new HashSet<WitGridTask>(ReferenceEqualityComparer.Instance);
            var gate = new object();

            var outcome = await GridReassignment.DistributeAsync(
                nodes, tasks,
                (group, _) =>
                {
                    if (group.Node.NodeId == badNode.NodeId)
                        return Task.FromResult(Fail("simulated node crash"));

                    lock (gate)
                        foreach (var task in group) completedByGood.Add(task);

                    return Task.FromResult(Ok());
                },
                logWarning: null,
                CancellationToken.None);

            Assert.That(outcome.Failed, Is.False, "the healthy node must absorb the failed node's tasks");
            Assert.That(completedByGood.Count, Is.EqualTo(10), "all tasks completed on the healthy node");
        }

        [Test]
        public async Task AllNodesFail_OutcomeFailed_WithLastNodeMessage()
        {
            var nodes = new[] { Node(10), Node(20) };
            var tasks = Enumerable.Range(0, 5).Select(_ => MakeTask()).ToList();

            var outcome = await GridReassignment.DistributeAsync(
                nodes, tasks,
                (_, _) => Task.FromResult(Fail("Blender render failed with exit code 134")),
                logWarning: null,
                CancellationToken.None);

            Assert.That(outcome.Failed, Is.True);
            Assert.That(outcome.FailureMessage, Does.Contain("exit code 134"));
        }

        [Test]
        public async Task NoNodes_OutcomeFailed_WithNoNodesMessage()
        {
            var outcome = await GridReassignment.DistributeAsync(
                Array.Empty<IWitEngineActivityNode>(),
                new[] { MakeTask() },
                (_, _) => Task.FromResult(Ok()),
                logWarning: null,
                CancellationToken.None);

            Assert.That(outcome.Failed, Is.True);
            Assert.That(outcome.FailureMessage, Does.Contain("No compatible nodes"));
        }

        [Test]
        public async Task NoTasks_CompletesImmediately()
        {
            var outcome = await GridReassignment.DistributeAsync(
                new[] { Node(10) },
                Array.Empty<WitGridTask>(),
                (_, _) => Task.FromResult(Ok()),
                logWarning: null,
                CancellationToken.None);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Is.Empty);
        }
    }
}
