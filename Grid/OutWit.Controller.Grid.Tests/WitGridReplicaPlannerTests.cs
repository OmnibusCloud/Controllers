using System;
using System.Collections.Generic;
using System.Linq;
using OutWit.Controller.Grid.Model;

namespace OutWit.Controller.Grid.Tests
{
    [TestFixture]
    public class WitGridReplicaPlannerTests
    {
        private static IReadOnlyList<int>? Plan(int items, int replicas, double share, int machines, int seed = 1)
        {
            return WitGridReplicaPlanner.Plan(items, replicas, share, machines, new Random(seed).Next);
        }

        #region No Copies Tests

        [TestCase(1, 1.0, 5)]
        [TestCase(0, 1.0, 5)]
        [TestCase(2, 0.0, 5)]
        [TestCase(2, 1.0, 1)]
        [TestCase(2, 1.0, 0)]
        public void NothingIsPlannedWhenNoItemCanRunTwiceTest(int replicas, double share, int machines)
        {
            Assert.That(Plan(10, replicas, share, machines), Is.Null);
        }

        [Test]
        public void AnEmptyCallPlansNothingTest()
        {
            Assert.That(Plan(0, 2, 1.0, 5), Is.Null);
        }

        [Test]
        public void ANotANumberShareSelectsNothingTest()
        {
            Assert.That(Plan(10, 2, double.NaN, 5), Is.Null);
        }

        #endregion

        #region Selection Tests

        [TestCase(20, 2, 0.05, 1)]
        [TestCase(10, 2, 0.05, 1)]
        [TestCase(100, 2, 0.07, 7)]
        [TestCase(10, 3, 0.25, 3)]
        [TestCase(7, 2, 1.0, 7)]
        [TestCase(3, 2, 5.0, 3)]
        public void TheShareIsRoundedUpToWholeItemsTest(int items, int replicas, double share, int expectedSelected)
        {
            var slots = Plan(items, replicas, share, 5)!;

            int selected = slots.GroupBy(slot => slot).Count(group => group.Count() > 1);

            Assert.That(selected, Is.EqualTo(expectedSelected));
            Assert.That(slots, Has.Count.EqualTo(items + expectedSelected * (replicas - 1)));
        }

        [Test]
        public void EveryItemRunsAtLeastOnceTest()
        {
            var slots = Plan(50, 3, 0.2, 5)!;

            Assert.That(slots.Distinct().OrderBy(slot => slot), Is.EqualTo(Enumerable.Range(0, 50)));
        }

        [Test]
        public void ASelectedItemRunsExactlyTheRequestedNumberOfTimesTest()
        {
            var counts = Plan(50, 3, 0.2, 5)!.GroupBy(slot => slot).Select(group => group.Count()).Distinct().OrderBy(count => count);

            Assert.That(counts, Is.EqualTo(new[] { 1, 3 }));
        }

        [TestCase(10, 2, 2)]
        [TestCase(3, 2, 2)]
        [TestCase(3, 3, 3)]
        [TestCase(10, 5, 3)]
        public void CopiesAreCappedByTheLimitAndByTheMachinesTest(int replicas, int machines, int expected)
        {
            int copies = Plan(10, replicas, 1.0, machines)!.GroupBy(slot => slot).Max(group => group.Count());

            Assert.That(copies, Is.EqualTo(expected));
        }

        #endregion

        #region Randomness Tests

        [Test]
        public void TheSelectionIsSpreadOverAllItemsTest()
        {
            const int ROUNDS = 4000;
            var random = new Random(11);
            var picked = new int[20];

            for (int round = 0; round < ROUNDS; round++)
            {
                foreach (var group in WitGridReplicaPlanner.Plan(20, 2, 0.1, 5, random.Next)!.GroupBy(slot => slot).Where(group => group.Count() > 1))
                    picked[group.Key]++;
            }

            // Each of 20 items is picked with p = 0.1: mean 400, sigma 19.
            Assert.That(picked.Min(), Is.GreaterThan(300));
            Assert.That(picked.Max(), Is.LessThan(500));
        }

        [Test]
        public void ASlotPositionSaysNothingAboutCopiesTest()
        {
            const int ROUNDS = 2000;
            var random = new Random(12);
            int inLastSlots = 0;
            int copies = 0;

            for (int round = 0; round < ROUNDS; round++)
            {
                var slots = WitGridReplicaPlanner.Plan(20, 2, 0.5, 5, random.Next)!;
                var repeated = slots.GroupBy(slot => slot).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();

                copies += slots.Count(repeated.Contains);
                inLastSlots += slots.Skip(20).Count(repeated.Contains);
            }

            // 20 of the 30 positions hold an item that runs twice; a third of those fall in the last 10.
            double share = (double)inLastSlots / copies;
            Assert.That(share, Is.EqualTo(1.0 / 3.0).Within(0.02));
        }

        [Test]
        public void ThePlatformSourceProducesAValidPlanTest()
        {
            var slots = WitGridReplicaPlanner.Plan(30, 2, 0.5, 4)!;

            Assert.That(slots, Has.Count.EqualTo(45));
            Assert.That(slots.Distinct().Count(), Is.EqualTo(30));
        }

        #endregion
    }
}
