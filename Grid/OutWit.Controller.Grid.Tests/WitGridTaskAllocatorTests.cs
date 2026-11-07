using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Variables;
using OutWit.Controller.Special.Activities;

namespace OutWit.Controller.Grid.Tests
{
    [TestFixture]
    public class WitGridTaskAllocatorTests
    {
        #region Ranking Tests

        [Test]
        public void AnUnmeasuredNodeRanksAfterEveryMeasuredOneTest()
        {
            var node = WitEngineNodeSdk.Instance;
            var unmeasured = new MockActivityNode(node, 1.0, Guid.NewGuid());
            var measuredSlow = new MockActivityNode(node, 0.3, Guid.NewGuid(), iterations: 3);
            var measuredFast = new MockActivityNode(node, 2.0, Guid.NewGuid(), iterations: 3);

            var ranked = WitGridTaskAllocator.Rank(new IWitEngineActivityNode[] { unmeasured, measuredSlow, measuredFast });

            Assert.That(ranked.Select(entry => entry.Node.NodeId), Is.EqualTo(new[] { measuredFast.NodeId, measuredSlow.NodeId, unmeasured.NodeId }));
        }

        [Test]
        public void AnUnmeasuredNodeIsPlannedWithTheSlowestMeasuredRateTest()
        {
            var node = WitEngineNodeSdk.Instance;
            var unmeasured = new MockActivityNode(node, 1.0, Guid.NewGuid());
            var measured = new MockActivityNode(node, 0.25, Guid.NewGuid(), iterations: 1);

            var ranked = WitGridTaskAllocator.Rank(new IWitEngineActivityNode[] { unmeasured, measured });

            Assert.That(ranked.Single(entry => entry.Node.NodeId == unmeasured.NodeId).Rate, Is.EqualTo(0.25));
            Assert.That(ranked.Single(entry => entry.Node.NodeId == measured.NodeId).Rate, Is.EqualTo(0.25));
        }

        [Test]
        public void AnUnmeasuredNodeSlowerThanTheFloorKeepsItsOwnRateTest()
        {
            var node = WitEngineNodeSdk.Instance;
            var unmeasured = new MockActivityNode(node, 0.1, Guid.NewGuid());
            var measured = new MockActivityNode(node, 0.5, Guid.NewGuid(), iterations: 1);

            var ranked = WitGridTaskAllocator.Rank(new IWitEngineActivityNode[] { unmeasured, measured });

            Assert.That(ranked.Single(entry => entry.Node.NodeId == unmeasured.NodeId).Rate, Is.EqualTo(0.1));
        }

        [Test]
        public void WithNothingMeasuredEveryNodeKeepsItsOwnRateAndOrderTest()
        {
            var node = WitEngineNodeSdk.Instance;
            var slower = new MockActivityNode(node, 10, Guid.NewGuid());
            var faster = new MockActivityNode(node, 20, Guid.NewGuid());

            var ranked = WitGridTaskAllocator.Rank(new IWitEngineActivityNode[] { slower, faster });

            Assert.That(ranked.Select(entry => entry.Node.NodeId), Is.EqualTo(new[] { faster.NodeId, slower.NodeId }));
            Assert.That(ranked.Select(entry => entry.Rate), Is.EqualTo(new[] { 20.0, 10.0 }));
        }

        #endregion

        #region Allocation Tests

        [Test]
        public void ASingleTaskGoesToTheMeasuredNodeNotToTheUnmeasuredDefaultTest()
        {
            // Before: the default rate 1.0 outranked a measured 0.3 and the unknown machine took the only task.
            var node = WitEngineNodeSdk.Instance;
            var unmeasured = new MockActivityNode(node, 1.0, Guid.NewGuid());
            var measured = new MockActivityNode(node, 0.3, Guid.NewGuid(), iterations: 3);

            var groups = WitGridTaskAllocator.Allocate(new IWitEngineActivityNode[] { unmeasured, measured }, Tasks(1));

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Node.NodeId, Is.EqualTo(measured.NodeId));
        }

        [Test]
        public void AnUnmeasuredNodeStillTakesWorkWhenThereIsEnoughOfItTest()
        {
            var node = WitEngineNodeSdk.Instance;
            var unmeasured = new MockActivityNode(node, 1.0, Guid.NewGuid());
            var measured = new MockActivityNode(node, 0.3, Guid.NewGuid(), iterations: 3);

            var groups = WitGridTaskAllocator.Allocate(new IWitEngineActivityNode[] { unmeasured, measured }, Tasks(20));

            var unmeasuredGroup = groups.Single(group => group.Node.NodeId == unmeasured.NodeId);
            var measuredGroup = groups.Single(group => group.Node.NodeId == measured.NodeId);
            Assert.That(unmeasuredGroup.Count + measuredGroup.Count, Is.EqualTo(20));
            Assert.That(unmeasuredGroup.Count, Is.GreaterThan(0));
            Assert.That(measuredGroup.Count, Is.GreaterThanOrEqualTo(unmeasuredGroup.Count));
        }

        [Test]
        public void AnUnmeasuredNodeGetsNoMoreThanTheSlowestMeasuredOneTest()
        {
            var node = WitEngineNodeSdk.Instance;
            var unmeasured = new MockActivityNode(node, 1.0, Guid.NewGuid());
            var measuredSlow = new MockActivityNode(node, 0.5, Guid.NewGuid(), iterations: 3);
            var measuredFast = new MockActivityNode(node, 2.0, Guid.NewGuid(), iterations: 3);

            var groups = WitGridTaskAllocator.Allocate(new IWitEngineActivityNode[] { unmeasured, measuredSlow, measuredFast }, Tasks(60));

            int Count(MockActivityNode candidate) => groups.SingleOrDefault(group => group.Node.NodeId == candidate.NodeId)?.Count ?? 0;
            Assert.That(Count(measuredFast), Is.GreaterThan(Count(measuredSlow)));
            Assert.That(Count(unmeasured), Is.LessThanOrEqualTo(Count(measuredSlow) + 1));
            Assert.That(Count(unmeasured) + Count(measuredSlow) + Count(measuredFast), Is.EqualTo(60));
        }

        #endregion

        #region Tools

        private static List<WitGridTask> Tasks(int count)
        {
            return Enumerable.Range(0, count)
                .Select(_ => new WitGridTask
                {
                    Work = 1,
                    Variables = new WitVariableCollection(),
                    Activity = new WitActivitySpecialTrace { Message = (WitReference)"obj" }
                })
                .ToList();
        }

        #endregion
    }
}
