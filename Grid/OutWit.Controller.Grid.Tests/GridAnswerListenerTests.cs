using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Controller.Grid.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests
{
    /// <summary>
    /// A return collection that asks to be told which item each value answers. Grid knows which item
    /// every return slot was built for, and a value says nothing of it; a collection that declares
    /// <c>public void OnGridAnswer(object? item, object? value)</c> is called once for every value
    /// that is kept, with the item of the slot the value came under. What is left out (a name nobody
    /// asked for, a value once too often, a value the collection would refuse, a copy that was
    /// dropped, the answer of a batch that failed) is not told. The tasks here are built as Grid
    /// builds them, and an honest value says who made it and under which slot.
    /// </summary>
    [TestFixture]
    public class GridAnswerListenerTests
    {
        #region Constants

        private const string BALANCED = GridTestRuns.BALANCED;

        private const string QUEUED = GridTestRuns.QUEUED;

        private const string RESULTS = "results";

        private const string SURPLUS = "surplus";

        #endregion

        #region Tools

        private static List<WitGridTask> MakeTasks(int count, bool copies = false)
        {
            return Enumerable.Range(0, count).Select(index => GridTestTasks.Built(index, copies ? index / 2 : null)).ToList();
        }

        private static string Slot(int index)
        {
            return GridTestTasks.ReturnName(index);
        }

        private static IWitEngineActivityNode Node(double rate)
        {
            return GridTestTasks.Node(rate);
        }

        /// <summary>
        /// The item of every slot of a call, as the Grid.ForEach adapter keeps them beside the tasks:
        /// an object of its own for every task, and one object for the copies of an item. The items
        /// are plain values, texts and lists in turn: Grid hands on whatever the call's collection held.
        /// </summary>
        private static Dictionary<string, object?> Items(IReadOnlyList<WitGridTask> tasks)
        {
            var made = new Dictionary<int, object>();
            var items = new Dictionary<string, object?>(StringComparer.Ordinal);

            for (int index = 0; index < tasks.Count; index++)
            {
                int key = tasks[index].ReplicaOf ?? index;

                if (!made.TryGetValue(key, out object? item))
                    made[key] = item = (key % 3) switch { 0 => key, 1 => $"item {key}", _ => new List<int> { key } };

                items[Slot(index)] = item;
            }

            return items;
        }

        /// <summary>
        /// The slot an honest value came under: it says who made it and under which name.
        /// </summary>
        private static string SlotOf(object? value)
        {
            return ((string)value!).Split(':')[1];
        }

        private static string MakerOf(object? value)
        {
            return ((string)value!).Split(':')[0];
        }

        private static IWitVariable With(IWitVariable answer, object? value)
        {
            return new WitVariableObject(answer.Name, value);
        }

        private static MockAnsweringNodesManager Honest()
        {
            return new MockAnsweringNodesManager(MockAnsweringNodesManager.Honest);
        }

        #endregion

        #region Told Tests

        [TestCase(BALANCED, true)]
        [TestCase(QUEUED, true)]
        [TestCase(BALANCED, false)]
        [TestCase(QUEUED, false)]
        public async Task ACollectionThatAsksIsToldEveryKeptValueWithTheItemOfItsSlotTest(string strategy, bool typed)
        {
            var nodes = new[] { Node(30), Node(20), Node(10) };
            var tasks = MakeTasks(7);
            var items = Items(tasks);
            var warnings = new List<string>();

            var texts = new MockAskingCollection<string>(RESULTS);
            var objects = new MockAskingCollection<object?>(RESULTS);

            GridValueRule rule = GridValueRule.ForCollection(typed ? texts : objects, items, warnings.Add);

            var results = await GridTestRuns.RunAsync(strategy, Honest(), nodes, tasks, rule, warnings);
            var told = typed ? texts.Told : objects.Told;

            Assert.That(rule.Tells, Is.True);
            Assert.That(rule.ElementType, Is.EqualTo(typed ? typeof(string) : null), "the rule takes what the collection's rule always took");
            Assert.That(results, Has.Count.EqualTo(7));
            Assert.That(told, Has.Count.EqualTo(7), "one call for every value that is kept");
            Assert.That(told.Select(pair => SlotOf(pair.Value)), Is.Unique);

            foreach (var (item, value) in told)
            {
                Assert.That(item, Is.SameAs(items[SlotOf(value)]), "the item of the slot the value came under, the same object");
                Assert.That(results.Count(result => ReferenceEquals(result, value)), Is.EqualTo(1), "the value is the object that joins the results");
            }

            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task EveryCopyOfAnItemIsToldTheSameItemUnderItsOwnSlotTest()
        {
            var nodes = new[] { Node(30), Node(20), Node(10) };
            var tasks = MakeTasks(6, copies: true);
            var items = Items(tasks);
            var collection = new MockAskingCollection<string>(RESULTS);

            var results = await GridTestRuns.RunAsync(BALANCED, Honest(), nodes, tasks, GridValueRule.ForCollection(collection, items, null));

            Assert.That(results, Has.Count.EqualTo(6));
            Assert.That(collection.Told, Has.Count.EqualTo(6));

            var byItem = collection.Told.GroupBy(pair => pair.Item, ReferenceEqualityComparer.Instance).ToList();

            Assert.That(byItem, Has.Count.EqualTo(3), "three items");

            foreach (var copies in byItem)
            {
                Assert.That(copies.Count(), Is.EqualTo(2), "each copy is told with the item of both");
                Assert.That(copies.Select(pair => SlotOf(pair.Value)).Distinct().Count(), Is.EqualTo(2), "each copy came under its own slot");
                Assert.That(copies.Select(pair => MakerOf(pair.Value)).Distinct().Count(), Is.EqualTo(2), "and the copies of an item ran on different machines");
            }
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public async Task ANodeThatAnswersOneItemUnderTwoSlotsIsToldWithTheItemOfEachSlotTest(string strategy)
        {
            // The node was given the items A, B and C. It returns a result of A under A's slot and a
            // second result of A under B's slot. Both came under a slot the node was asked for, so
            // Grid keeps both; the collection is told each with the item of its slot, and a controller
            // that looks into its values sees that the second does not answer B.
            var tasks = MakeTasks(3);
            var items = new Dictionary<string, object?>(StringComparer.Ordinal) { [Slot(0)] = "A", [Slot(1)] = "B", [Slot(2)] = "C" };
            var answers = new Dictionary<string, object?>(StringComparer.Ordinal) { [Slot(0)] = "result of A", [Slot(1)] = "another result of A", [Slot(2)] = "result of C" };
            var collection = new MockAskingCollection<string>(RESULTS);

            var manager = new MockAnsweringNodesManager((nodeId, request) =>
                MockAnsweringNodesManager.Honest(nodeId, request).Select(answer => With(answer, answers[answer.Name])).ToList());

            var results = await GridTestRuns.RunAsync(strategy, manager, new[] { Node(10) }, tasks, GridValueRule.ForCollection(collection, items, null));

            Assert.That(results, Is.EqualTo(new[] { "result of A", "another result of A", "result of C" }), "Grid keeps the three values");
            Assert.That(collection.Told, Is.EqualTo(new (object?, object?)[]
            {
                ("A", "result of A"),
                ("B", "another result of A"),
                ("C", "result of C")
            }));
            Assert.That(collection.Told.Where(pair => !((string)pair.Value!).EndsWith($"of {pair.Item}")).Select(pair => pair.Item), Is.EqualTo(new[] { "B" }));
        }

        [Test]
        public async Task AValueThatComesAfterItsNodeFailedIsToldOnceWithItsItemTest()
        {
            // The faster node answers its first task and fails the next: its batch fails, nothing of
            // it is kept or told, and its tasks go to the other node.
            var failing = Node(20);
            var healthy = Node(10);
            var tasks = MakeTasks(6);
            var items = Items(tasks);
            var warnings = new List<string>();
            var collection = new MockAskingCollection<string>(RESULTS);
            var answered = new HashSet<Guid>();

            var manager = new MockAnsweringNodesManager((nodeId, request) =>
                nodeId == failing.NodeId && !answered.Add(nodeId)
                    ? null
                    : MockAnsweringNodesManager.Honest(nodeId, request));

            var results = await GridTestRuns.RunAsync(BALANCED, manager, new[] { failing, healthy }, tasks, GridValueRule.ForCollection(collection, items, null), warnings);

            Assert.That(warnings.Count(warning => warning.Contains("reassigning")), Is.EqualTo(1));
            Assert.That(manager.Requests.Count(entry => entry.NodeId == failing.NodeId), Is.GreaterThanOrEqualTo(2), "the node answered a task before it failed");
            Assert.That(results, Has.Count.EqualTo(6));
            Assert.That(results.Select(MakerOf).Distinct(), Is.EqualTo(new[] { healthy.NodeId.ToString("N") }));
            Assert.That(collection.Told, Has.Count.EqualTo(6), "every value is told once, when it is kept");
            Assert.That(collection.Told.Select(pair => SlotOf(pair.Value)), Is.Unique);
            Assert.That(collection.Told.Select(pair => MakerOf(pair.Value)).Distinct(), Is.EqualTo(new[] { healthy.NodeId.ToString("N") }), "what the failed node returned was never told");

            foreach (var (item, value) in collection.Told)
                Assert.That(item, Is.SameAs(items[SlotOf(value)]), "a task keeps its slot, and its item, when it moves");
        }

        #endregion

        #region Not Told Tests

        [Test]
        public async Task ACopyThatWasDroppedTellsNothingTest()
        {
            // Two machines, two copies of every item. One machine fails; the other already holds a
            // copy of every item, so the failed copies have nowhere to go and are dropped.
            var failing = Node(20);
            var healthy = Node(10);
            var tasks = MakeTasks(4, copies: true);
            var items = Items(tasks);
            var warnings = new List<string>();
            var collection = new MockAskingCollection<string>(RESULTS);

            var manager = new MockAnsweringNodesManager((nodeId, request) =>
                nodeId == failing.NodeId ? null : MockAnsweringNodesManager.Honest(nodeId, request));

            var results = await GridTestRuns.RunAsync(BALANCED, manager, new[] { failing, healthy }, tasks, GridValueRule.ForCollection(collection, items, null), warnings);

            Assert.That(warnings.Count(warning => warning.Contains("was dropped")), Is.EqualTo(2));
            Assert.That(results, Has.Count.EqualTo(2), "one result an item");
            Assert.That(collection.Told, Has.Count.EqualTo(2));
            Assert.That(collection.Told.Select(pair => pair.Item).Distinct(ReferenceEqualityComparer.Instance).Count(), Is.EqualTo(2), "each item once");
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public async Task AValueUnderANameNobodyAskedForIsNotToldTest(string strategy)
        {
            var tasks = MakeTasks(4);
            var warnings = new List<string>();
            var collection = new MockAskingCollection<object?>(RESULTS);

            var manager = new MockAnsweringNodesManager((nodeId, request) =>
            {
                var answer = MockAnsweringNodesManager.Honest(nodeId, request);
                answer.Add(new WitVariableObject("returnCode", SURPLUS));
                return answer;
            });

            var results = await GridTestRuns.RunAsync(strategy, manager, new[] { Node(10) }, tasks, GridValueRule.ForCollection(collection, Items(tasks), null), warnings);

            Assert.That(results, Has.Count.EqualTo(4));
            Assert.That(results, Has.None.EqualTo(SURPLUS));
            Assert.That(collection.Told, Has.Count.EqualTo(4));
            Assert.That(collection.Told.Select(pair => pair.Value), Has.None.EqualTo(SURPLUS));
            Assert.That(warnings.Where(warning => warning.Contains("did not ask for")), Is.Not.Empty);
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public async Task AValueThatComesOnceTooOftenIsNotToldTest(string strategy)
        {
            // Two values under every slot: the first is the task's answer, the second is one too many.
            var tasks = MakeTasks(3);
            var items = Items(tasks);
            var collection = new MockAskingCollection<object?>(RESULTS);

            var manager = new MockAnsweringNodesManager((nodeId, request) =>
            {
                var answer = MockAnsweringNodesManager.Honest(nodeId, request);
                answer.AddRange(answer.Select(first => With(first, SURPLUS)).ToList());
                return answer;
            });

            var results = await GridTestRuns.RunAsync(strategy, manager, new[] { Node(10) }, tasks, GridValueRule.ForCollection(collection, items, null));

            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(collection.Told, Has.Count.EqualTo(3));
            Assert.That(collection.Told.Select(pair => pair.Value), Has.None.EqualTo(SURPLUS));
            Assert.That(collection.Told.Select(pair => pair.Item), Is.EqualTo(new[] { items[Slot(0)], items[Slot(1)], items[Slot(2)] }));
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public async Task AValueTheCollectionWouldRefuseIsNotToldTest(string strategy)
        {
            // A collection of texts would refuse the null under the second slot and the number under
            // the fourth: they never join the results, so the collection is not told of them.
            var tasks = MakeTasks(5);
            var items = Items(tasks);
            var warnings = new List<string>();
            var collection = new MockAskingCollection<string>(RESULTS);

            var manager = new MockAnsweringNodesManager((nodeId, request) =>
                MockAnsweringNodesManager.Honest(nodeId, request)
                    .Select(answer => answer.Name == Slot(1) ? With(answer, null) : answer.Name == Slot(3) ? With(answer, 42) : answer)
                    .ToList());

            var results = await GridTestRuns.RunAsync(strategy, manager, new[] { Node(10) }, tasks, GridValueRule.ForCollection(collection, items, null), warnings);

            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(collection.Told.Select(pair => pair.Item), Is.EqualTo(new[] { items[Slot(0)], items[Slot(2)], items[Slot(4)] }));
            Assert.That(collection.Told.Select(pair => pair.Value), Is.EqualTo(results));
            Assert.That(warnings.Where(warning => warning.Contains("would refuse")), Is.Not.Empty);
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public async Task AValueUnderASlotTheCallHasNoItemForIsKeptAndNotToldTest(string strategy)
        {
            // Grid.ForEach has an item for every slot it builds. A task built by other means has none:
            // its value is kept as ever, and there is nothing to tell.
            var tasks = MakeTasks(3);
            var items = Items(tasks);
            var collection = new MockAskingCollection<object?>(RESULTS);

            items.Remove(Slot(1));

            var results = await GridTestRuns.RunAsync(strategy, Honest(), new[] { Node(10) }, tasks, GridValueRule.ForCollection(collection, items, null));

            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(collection.Told.Select(pair => SlotOf(pair.Value)), Is.EqualTo(new[] { Slot(0), Slot(2) }));
        }

        #endregion
    }
}
