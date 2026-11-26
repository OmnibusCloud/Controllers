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
    /// A return collection that does not ask to be told sees no difference at all. Its rule is the
    /// rule it always had, the shared one for a collection of objects; a call into it returns the
    /// same objects in the same order, with or without the items of the slots at hand; and telling a
    /// collection that does ask changes nothing of what its call returns.
    /// </summary>
    [TestFixture]
    public class GridAnswerListenerAsBeforeTests
    {
        #region Constants

        private const string BALANCED = GridTestRuns.BALANCED;

        private const string QUEUED = GridTestRuns.QUEUED;

        private const string RESULTS = "results";

        #endregion

        #region Tools

        private static List<WitGridTask> MakeTasks(int count)
        {
            return Enumerable.Range(0, count).Select(index => GridTestTasks.Built(index)).ToList();
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
        /// The item of every slot: the number of its task.
        /// </summary>
        private static Dictionary<string, object?> Items(IReadOnlyList<WitGridTask> tasks)
        {
            return Enumerable.Range(0, tasks.Count).ToDictionary(Slot, index => (object?)index, StringComparer.Ordinal);
        }

        /// <summary>
        /// One answer a slot, the same object in every run: a text made for that slot alone.
        /// </summary>
        private static Dictionary<string, object?> Answers(IReadOnlyList<WitGridTask> tasks)
        {
            return Enumerable.Range(0, tasks.Count).ToDictionary(Slot, index => (object?)string.Concat("value of ", Slot(index)), StringComparer.Ordinal);
        }

        private static IWitVariable With(IWitVariable answer, object? value)
        {
            return new WitVariableObject(answer.Name, value);
        }

        #endregion

        #region Without The Method Tests

        [Test]
        public void ACollectionWithoutTheMethodHasTheRuleItAlwaysHadTest()
        {
            var items = new Dictionary<string, object?>();
            var warnings = new List<string>();

            GridValueRule objects = GridValueRule.ForCollection(new MockCollection<object?>(RESULTS), items, warnings.Add);
            GridValueRule engineObjects = GridValueRule.ForCollection(new WitVariableObjectCollection(RESULTS), items, warnings.Add);
            GridValueRule integers = GridValueRule.ForCollection(new MockCollection<int>(RESULTS), items, warnings.Add);
            GridValueRule noCollection = GridValueRule.ForCollection(null, items, warnings.Add);
            GridValueRule notACollection = GridValueRule.ForCollection(new WitVariableObject(RESULTS), items, warnings.Add);

            Assert.That(objects, Is.SameAs(GridValueRule.ANY));
            Assert.That(engineObjects, Is.SameAs(GridValueRule.ANY));
            Assert.That(noCollection, Is.SameAs(GridValueRule.ANY));
            Assert.That(notACollection, Is.SameAs(GridValueRule.ANY));
            Assert.That(integers.ElementType, Is.EqualTo(typeof(int)));
            Assert.That(integers.Tells, Is.False);
            Assert.That(GridValueRule.ANY.Tells, Is.False, "the rule every call shares tells nobody");
            Assert.That(GridValueRule.ForCollection(new MockCollection<int>(RESULTS)).Tells, Is.False);
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void TheRuleOfACollectionThatAsksIsARuleOfItsOwnTest()
        {
            var items = new Dictionary<string, object?>();

            GridValueRule first = GridValueRule.ForCollection(new MockAskingCollection<object?>(RESULTS), items, null);
            GridValueRule second = GridValueRule.ForCollection(new MockAskingCollection<object?>(RESULTS), items, null);

            Assert.That(first, Is.Not.SameAs(GridValueRule.ANY), "the shared rule is never the one that tells");
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Tells, Is.True);
            Assert.That(first.ElementType, Is.Null);
            Assert.That(first.Accepts(null), Is.True);
            Assert.That(GridValueRule.ANY.Tells, Is.False);
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public async Task ACallIntoACollectionWithoutTheMethodReturnsTheSameObjectsInTheSameOrderAsBeforeTest(string strategy)
        {
            // Every slot has one answer, the same object in every run, and one machine fails, so the
            // order is the order of a call whose work moved.
            var failing = Node(30);
            var nodes = new[] { failing, Node(20), Node(10) };
            var tasks = MakeTasks(9);
            var items = Items(tasks);
            var answers = Answers(tasks);

            MockAnsweringNodesManager Manager()
            {
                return new MockAnsweringNodesManager((nodeId, request) =>
                    strategy == BALANCED && nodeId == failing.NodeId
                        ? null
                        : MockAnsweringNodesManager.Honest(nodeId, request).Select(answer => With(answer, answers[answer.Name])).ToList());
            }

            var plain = new MockCollection<string>(RESULTS);

            // As the call ran before a collection could ask: the rule of the collection alone.
            var before = await GridTestRuns.RunAsync(strategy, Manager(), nodes, tasks, GridValueRule.ForCollection(plain));
            var now = await GridTestRuns.RunAsync(strategy, Manager(), nodes, tasks, GridValueRule.ForCollection(plain, items, null));
            var noRule = await GridTestRuns.RunAsync(strategy, Manager(), nodes, tasks, null);

            Assert.That(before, Has.Count.EqualTo(9));

            foreach (var results in new[] { now, noRule })
            {
                Assert.That(results, Has.Count.EqualTo(9));

                for (int index = 0; index < before.Count; index++)
                    Assert.That(results[index], Is.SameAs(before[index]), $"value {index}");
            }
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public async Task TellingACollectionChangesNothingOfWhatTheCallReturnsTest(string strategy)
        {
            var nodes = new[] { Node(30), Node(20), Node(10) };
            var tasks = MakeTasks(9);
            var items = Items(tasks);
            var answers = Answers(tasks);

            MockAnsweringNodesManager Manager()
            {
                return new MockAnsweringNodesManager((nodeId, request) =>
                    MockAnsweringNodesManager.Honest(nodeId, request).Select(answer => With(answer, answers[answer.Name])).ToList());
            }

            var asking = new MockAskingCollection<string>(RESULTS);

            var plain = await GridTestRuns.RunAsync(strategy, Manager(), nodes, tasks, GridValueRule.ForCollection(new MockCollection<string>(RESULTS)));
            var told = await GridTestRuns.RunAsync(strategy, Manager(), nodes, tasks, GridValueRule.ForCollection(asking, items, null));

            Assert.That(asking.Told, Has.Count.EqualTo(9));
            Assert.That(told, Has.Count.EqualTo(9));

            for (int index = 0; index < plain.Count; index++)
                Assert.That(told[index], Is.SameAs(plain[index]), $"value {index}");
        }

        #endregion
    }
}
