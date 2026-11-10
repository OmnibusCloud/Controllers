using System;
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
    [TestFixture]
    public class GridReassignmentReplicaTests
    {
        private static WitGridTask MakeTask(int? replicaOf, double work = 1)
        {
            return new WitGridTask
            {
                Work = work,
                ReplicaOf = replicaOf,
                Variables = new WitVariableCollection(),
                Activity = new WitActivitySpecialTrace { Message = (WitReference)"obj" }
            };
        }

        private static IWitEngineActivityNode Node(double rate)
        {
            return new MockActivityNode(WitEngineNodeSdk.Instance, rate, Guid.NewGuid(), iterations: 1);
        }

        private static GridReassignment.GroupProcessResult Ok()
        {
            return new GridReassignment.GroupProcessResult(true, null, Array.Empty<IWitVariable>());
        }

        private static GridReassignment.GroupProcessResult Fail(string message = "node failed")
        {
            var status = new WitProcessingStatus(Guid.Empty, Guid.NewGuid()).Failed(TimeSpan.Zero, message);

            return new GridReassignment.GroupProcessResult(false, status, Array.Empty<IWitVariable>());
        }

        private static async Task<(GridReassignment.DistributionOutcome Outcome, List<(Guid Node, int? Item)> Runs)> RunAsync(
            IReadOnlyList<IWitEngineActivityNode> nodes, IReadOnlyList<WitGridTask> tasks, Func<WitGridTaskGroup, bool> succeeds)
        {
            var runs = new List<(Guid Node, int? Item)>();

            var outcome = await GridReassignment.DistributeAsync(
                nodes,
                tasks,
                (group, _) =>
                {
                    bool succeeded = succeeds(group);

                    if (succeeded)
                        runs.AddRange(group.Select(task => (group.Node.NodeId, task.ReplicaOf)));

                    return Task.FromResult(succeeded ? Ok() : Fail());
                },
                logWarning: null,
                CancellationToken.None);

            return (outcome, runs);
        }

        #region Reassignment Tests

        [Test]
        public async Task AFailedCopyIsNotSentToTheMachineHoldingTheOtherCopyTest()
        {
            var bad = Node(100);
            var holder = Node(50);
            var free = Node(1);

            var (outcome, runs) = await RunAsync(
                new[] { bad, holder, free },
                new[] { MakeTask(0), MakeTask(0) },
                group => group.Node.NodeId != bad.NodeId);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(runs.Select(run => run.Node).OrderBy(id => id), Is.EqualTo(new[] { holder.NodeId, free.NodeId }.OrderBy(id => id)));
        }

        [Test]
        public async Task ACopyWithNoFreeMachineIsDroppedAndTheJobSucceedsTest()
        {
            var bad = Node(100);
            var holder = Node(1);

            var (outcome, runs) = await RunAsync(
                new[] { bad, holder },
                new[] { MakeTask(0), MakeTask(0) },
                group => group.Node.NodeId != bad.NodeId);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(runs, Has.Count.EqualTo(1));
            Assert.That(runs[0].Node, Is.EqualTo(holder.NodeId));
        }

        [Test]
        public async Task TheLastCopyOfAnItemWithNoResultStillRunsTest()
        {
            var badA = Node(100);
            var badB = Node(50);
            var last = Node(1);

            var (outcome, runs) = await RunAsync(
                new[] { badA, badB, last },
                new[] { MakeTask(0), MakeTask(0) },
                group => group.Node.NodeId == last.NodeId);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(runs, Has.Count.EqualTo(1), "both copies failed and one machine is left: one copy runs");
            Assert.That(runs[0].Node, Is.EqualTo(last.NodeId));
        }

        [Test]
        public async Task AnItemWithNoResultFailsTheJobWhenNoMachineIsLeftTest()
        {
            var (outcome, runs) = await RunAsync(
                new[] { Node(10), Node(5) },
                new[] { MakeTask(0), MakeTask(0) },
                _ => false);

            Assert.That(outcome.Failed, Is.True);
            Assert.That(outcome.FailureMessage, Is.EqualTo("node failed"));
            Assert.That(runs, Is.Empty);
        }

        [Test]
        public async Task ItemsWithAResultDoNotFailTheJobWhenEveryMachineDiesLaterTest()
        {
            var nodes = new[] { Node(10), Node(10), Node(10) };
            var calls = new Dictionary<Guid, int>();

            // The second node fails at once; the others return their first batch and fail after that.
            bool Succeeds(WitGridTaskGroup group)
            {
                calls[group.Node.NodeId] = calls.GetValueOrDefault(group.Node.NodeId) + 1;

                return group.Node.NodeId != nodes[1].NodeId && calls[group.Node.NodeId] == 1;
            }

            var (outcome, runs) = await RunAsync(
                nodes,
                new[] { MakeTask(0), MakeTask(0), MakeTask(1), MakeTask(1) },
                Succeeds);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(runs.Select(run => run.Item).Distinct().Count(), Is.EqualTo(2));
        }

        #endregion

        #region Control Tests

        [Test]
        public async Task ACallWithoutCopiesStillReassignsToAnyHealthyNodeTest()
        {
            var bad = Node(100);
            var good = Node(1);
            var tasks = Enumerable.Range(0, 6).Select(_ => MakeTask(null)).ToArray();

            var (outcome, runs) = await RunAsync(new[] { bad, good }, tasks, group => group.Node.NodeId != bad.NodeId);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(runs, Has.Count.EqualTo(6));
            Assert.That(runs.All(run => run.Node == good.NodeId), Is.True);
        }

        #endregion

        #region Synthetic Fleet Tests

        [Test]
        public async Task CopiesStayApartThroughRandomFailuresTest()
        {
            var random = new Random(77);

            for (int fleet = 0; fleet < 300; fleet++)
            {
                int machines = random.Next(2, 9);
                var nodes = Enumerable.Range(0, machines).Select(_ => Node(0.1 + random.NextDouble() * 20)).ToArray();

                int items = random.Next(1, 30);
                var slots = WitGridReplicaPlanner.Plan(items, random.Next(2, 4), random.NextDouble(), machines, random.Next)!;
                var executions = slots.GroupBy(slot => slot).ToDictionary(group => group.Key, group => group.Count());
                var tasks = slots.Select(slot => MakeTask(executions[slot] > 1 ? slot : null, 1 + slot % 5)).ToArray();

                var doomed = nodes.Where(_ => random.NextDouble() < 0.4).Select(node => node.NodeId).ToHashSet();

                var (outcome, runs) = await RunAsync(nodes, tasks, group => !doomed.Contains(group.Node.NodeId));

                var resultsOfItems = runs.Where(run => run.Item != null).GroupBy(run => run.Item).ToList();

                foreach (var item in resultsOfItems)
                    Assert.That(item.Select(run => run.Node).Distinct().Count(), Is.EqualTo(item.Count()), "two results of one item from one machine");

                Assert.That(outcome.Failed, Is.EqualTo(doomed.Count == machines), "the job fails only when no machine can finish");

                if (outcome.Failed)
                    continue;

                Assert.That(resultsOfItems, Has.Count.EqualTo(executions.Count(pair => pair.Value > 1)), "every item with copies keeps a result");
                Assert.That(runs.Count(run => run.Item == null), Is.EqualTo(executions.Count(pair => pair.Value == 1)), "every single item ran once");
            }
        }

        #endregion
    }
}
