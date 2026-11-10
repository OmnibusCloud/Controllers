using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Grid.Tests
{
    [TestFixture]
    public class WitGridTaskAllocatorReplicaTests
    {
        private static WitGridTask MakeTask(double work, int? replicaOf = null)
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

        private static IReadOnlyDictionary<int, HashSet<Guid>> Holding(int item, params IWitEngineActivityNode[] nodes)
        {
            return new Dictionary<int, HashSet<Guid>> { [item] = nodes.Select(node => node.NodeId).ToHashSet() };
        }

        private static void AssertCopiesApart(IReadOnlyList<WitGridTaskGroup> groups)
        {
            foreach (var group in groups)
            {
                var items = group.Where(task => task.ReplicaOf != null).Select(task => task.ReplicaOf!.Value).ToList();

                Assert.That(items.Distinct().Count(), Is.EqualTo(items.Count), "two copies of one item share a machine");
            }
        }

        #region Keeping Copies Apart Tests

        [Test]
        public void TwoCopiesLandOnDifferentNodesEvenWhenOneNodeIsFarFasterTest()
        {
            var fast = Node(1000);
            var slow = Node(1);
            var tasks = new[] { MakeTask(1, 0), MakeTask(1, 0) };

            var groups = WitGridTaskAllocator.Allocate(new[] { fast, slow }, tasks);

            Assert.That(groups, Has.Count.EqualTo(2));
            AssertCopiesApart(groups);
        }

        [Test]
        public void TrimmingNeverLeavesFewerNodesThanCopiesTest()
        {
            var nodes = new[] { Node(100), Node(99), Node(98), Node(1) };
            var tasks = new[] { MakeTask(1, 0), MakeTask(1, 0), MakeTask(1, 0) };

            var groups = WitGridTaskAllocator.Allocate(nodes, tasks);

            Assert.That(groups, Has.Count.EqualTo(3));
            AssertCopiesApart(groups);
        }

        [Test]
        public void TwoNodesWithOneIdAreOneMachineTest()
        {
            var id = Guid.NewGuid();
            var nodes = new IWitEngineActivityNode[]
            {
                new MockActivityNode(WitEngineNodeSdk.Instance, 2, id, iterations: 1),
                new MockActivityNode(WitEngineNodeSdk.Instance, 2, id, iterations: 1),
                Node(1)
            };

            var groups = WitGridTaskAllocator.Allocate(nodes, new[] { MakeTask(1, 0), MakeTask(1, 0) });

            Assert.That(groups.Select(group => group.Node.NodeId).Distinct().Count(), Is.EqualTo(2));
        }

        [Test]
        public void MoreCopiesThanMachinesIsAnErrorTest()
        {
            var nodes = new[] { Node(1), Node(2) };
            var tasks = new[] { MakeTask(1, 0), MakeTask(1, 0), MakeTask(1, 0) };

            Assert.That(() => WitGridTaskAllocator.Allocate(nodes, tasks), Throws.InvalidOperationException);
        }

        #endregion

        #region Holders Tests

        [Test]
        public void ACopyAvoidsAMachineThatAlreadyHoldsItsItemTest()
        {
            var fast = Node(1000);
            var slow = Node(1);

            var groups = WitGridTaskAllocator.Allocate(new[] { fast, slow }, new[] { MakeTask(1, 7) }, Holding(7, fast));

            Assert.That(groups.Single().Node.NodeId, Is.EqualTo(slow.NodeId));
        }

        [Test]
        public void TheOnlyFreeMachineMayBeTheLastOneRankedTest()
        {
            var nodes = new[] { Node(100), Node(50), Node(1) };

            var groups = WitGridTaskAllocator.Allocate(nodes, new[] { MakeTask(1, 3) }, Holding(3, nodes[0], nodes[1]));

            Assert.That(groups.Single().Node.NodeId, Is.EqualTo(nodes[2].NodeId));
        }

        [Test]
        public void AHolderStillTakesOtherItemsTest()
        {
            var a = Node(100);
            var b = Node(100);

            var groups = WitGridTaskAllocator.Allocate(new[] { a, b }, new[] { MakeTask(1, 1), MakeTask(1) }, Holding(1, a));

            Assert.That(groups.Single(group => group.Node.NodeId == b.NodeId).Single().ReplicaOf, Is.EqualTo(1));
            Assert.That(groups.Single(group => group.Node.NodeId == a.NodeId).Single().ReplicaOf, Is.Null);
        }

        #endregion

        #region Control Tests

        [Test]
        public void TasksWithoutNodesAreRefusedByNameTest()
        {
            Assert.That(() => WitGridTaskAllocator.Allocate(Array.Empty<IWitEngineActivityNode>(), new[] { MakeTask(1) }),
                Throws.InvalidOperationException.With.Message.Contains("no nodes"));
        }

        [Test]
        public void ACallWithoutCopiesIsPlannedAsBeforeTest()
        {
            var nodes = new[] { Node(100), Node(10), Node(1) };
            var tasks = new[] { MakeTask(1), MakeTask(1) };

            var groups = WitGridTaskAllocator.Allocate(nodes, tasks);

            // Two equal tasks on a node ten times faster than the next one: the trim keeps one node.
            Assert.That(groups.Single().Node.NodeId, Is.EqualTo(nodes[0].NodeId));
            Assert.That(groups.Single().Count, Is.EqualTo(2));
        }

        [Test]
        public void ATaskCopyIsEqualToItsCloneAndDiffersFromAnotherItemTest()
        {
            var task = MakeTask(1, 2);

            Assert.That(task.Is(task.Clone()), Is.True);
            Assert.That(task.Is(MakeTask(1, 3)), Is.False);
            Assert.That(task.Is(MakeTask(1)), Is.False);
        }

        #endregion

        #region Synthetic Fleet Tests

        [Test]
        public void CopiesStayApartOnRandomFleetsTest()
        {
            var random = new Random(4242);

            for (int round = 0; round < 500; round++)
            {
                int machines = random.Next(2, 12);
                var nodes = Enumerable.Range(0, machines).Select(_ => Node(0.1 + random.NextDouble() * 50)).ToArray();

                int items = random.Next(1, 60);
                var slots = WitGridReplicaPlanner.Plan(items, random.Next(2, 4), random.NextDouble(), machines, random.Next);
                Assert.That(slots, Is.Not.Null);

                var executions = slots!.GroupBy(slot => slot).ToDictionary(group => group.Key, group => group.Count());
                var tasks = slots.Select(slot => MakeTask(1 + slot % 7, executions[slot] > 1 ? slot : null)).ToArray();

                var groups = WitGridTaskAllocator.Allocate(nodes, tasks);

                AssertCopiesApart(groups);
                Assert.That(groups.Sum(group => group.Count), Is.EqualTo(tasks.Length), "every execution is placed");
            }
        }

        #endregion
    }
}
