using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Controller.Grid.Utils;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests
{
    /// <summary>
    /// What can go wrong around a return collection that asks to be told, and what Grid does then.
    /// The method is the controller's code: when it throws, the value is kept all the same, the other
    /// values are told, and one warning a call says how often it threw. A public method of the name
    /// with another signature is a mistake: one warning a call, and the method is never called. And
    /// under the Queued strategy the answers are held in the nodes' own loops, so the method is called
    /// from several threads at once, with no lock of Grid's around it.
    /// </summary>
    [TestFixture]
    public class GridAnswerListenerFaultsTests
    {
        #region Constants

        private const string BALANCED = GridTestRuns.BALANCED;

        private const string QUEUED = GridTestRuns.QUEUED;

        private const string RESULTS = "results";

        private const string METHOD = "OnGridAnswer";

        /// <summary>
        /// Collections whose public method of the name is not the one Grid calls, each with the way
        /// to read how often the method was called.
        /// </summary>
        private static readonly TestCaseData[] OTHER_SIGNATURES =
        {
            Mistake("one argument", new MockOneArgumentCollection(RESULTS), collection => ((MockOneArgumentCollection)collection).Calls),
            Mistake("typed arguments", new MockTypedArgumentsCollection(RESULTS), collection => ((MockTypedArgumentsCollection)collection).Calls),
            Mistake("a return value", new MockAnsweringBackCollection(RESULTS), collection => ((MockAnsweringBackCollection)collection).Calls),
            Mistake("a static method", new MockStaticMethodCollection(RESULTS), _ => MockStaticMethodCollection.Calls)
        };

        #endregion

        #region Tools

        private static TestCaseData Mistake(string name, IWitVariable collection, Func<IWitVariable, int> calls)
        {
            return new TestCaseData(collection, calls).SetArgDisplayNames(name);
        }

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
        private static Dictionary<string, object?> Items(int count)
        {
            return Enumerable.Range(0, count).ToDictionary(Slot, index => (object?)index, StringComparer.Ordinal);
        }

        private static MockAnsweringNodesManager Honest()
        {
            return new MockAnsweringNodesManager(MockAnsweringNodesManager.Honest);
        }

        private static List<string> About(List<string> warnings)
        {
            return warnings.Where(warning => warning.Contains(METHOD)).ToList();
        }

        #endregion

        #region Throwing Tests

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public async Task AMethodThatThrowsCostsNoValueAndTheOtherValuesAreToldTest(string strategy)
        {
            var warnings = new List<string>();
            var collection = new MockAskingCollection<string>(RESULTS)
            {
                Throws = (item, _) => Equals(item, 2) ? new InvalidOperationException("the controller's own trouble") : null
            };

            GridValueRule rule = GridValueRule.ForCollection(collection, Items(5), warnings.Add);

            var results = await GridTestRuns.RunAsync(strategy, Honest(), new[] { Node(20), Node(10) }, MakeTasks(5), rule, warnings);

            Assert.That(results, Has.Count.EqualTo(5), "the value it threw for is kept");
            Assert.That(collection.Told.Select(pair => pair.Item), Is.EquivalentTo(new[] { 0, 1, 3, 4 }), "the others are told");
            Assert.That(warnings, Is.Empty, "nothing is said while the call runs");

            rule.WarnOfUntold(warnings.Add);

            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain($"{METHOD} of the return collection '{RESULTS}' threw for 1 value(s) of the call"));
            Assert.That(warnings[0], Does.Contain(nameof(InvalidOperationException)));
            Assert.That(warnings[0], Does.Contain("kept"));
            Assert.That(warnings[0], Does.Not.Contain("the controller's own trouble"), "the warning names the type, not what the controller wrote");

            rule.WarnOfUntold(warnings.Add);

            Assert.That(warnings, Has.Count.EqualTo(1), "what was said is not said again");
        }

        [Test]
        public async Task EveryThrowOfACallIsCountedInOneWarningThatNamesTheFirstTest()
        {
            // One node, so the values are kept in the order of the tasks: the first throw is the one
            // for the first task.
            var warnings = new List<string>();
            var collection = new MockAskingCollection<object?>(RESULTS)
            {
                Throws = (item, _) => Equals(item, 0) ? new FormatException() : new ArgumentException()
            };

            GridValueRule rule = GridValueRule.ForCollection(collection, Items(6), warnings.Add);

            var results = await GridTestRuns.RunAsync(BALANCED, Honest(), new[] { Node(10) }, MakeTasks(6), rule, warnings);

            rule.WarnOfUntold(warnings.Add);

            Assert.That(results, Has.Count.EqualTo(6));
            Assert.That(collection.Told, Is.Empty);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("threw for 6 value(s)"));
            Assert.That(warnings[0], Does.Contain(nameof(FormatException)));
            Assert.That(warnings[0], Does.Not.Contain(nameof(ArgumentException)));
        }

        [Test]
        public void ACollectionThatNeverThrewIsNotWarnedOfTest()
        {
            var warnings = new List<string>();

            GridValueRule.ForCollection(new MockAskingCollection<object?>(RESULTS), Items(1), warnings.Add).WarnOfUntold(warnings.Add);
            GridValueRule.ForCollection(new MockCollection<int>(RESULTS), Items(1), warnings.Add).WarnOfUntold(warnings.Add);
            GridValueRule.ANY.WarnOfUntold(warnings.Add);

            Assert.That(warnings, Is.Empty);
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public async Task ACancellationTheMethodThrowsDoesNotEndTheCallTest(string strategy)
        {
            // The call's own cancellation comes through its token. An OperationCanceledException out
            // of the controller's method is the method's trouble, as any other exception is.
            var warnings = new List<string>();
            var collection = new MockAskingCollection<object?>(RESULTS)
            {
                Throws = (item, _) => Equals(item, 1) ? new OperationCanceledException() : null
            };

            GridValueRule rule = GridValueRule.ForCollection(collection, Items(4), warnings.Add);

            var results = await GridTestRuns.RunAsync(strategy, Honest(), new[] { Node(10) }, MakeTasks(4), rule, warnings);

            rule.WarnOfUntold(warnings.Add);

            Assert.That(results, Has.Count.EqualTo(4));
            Assert.That(collection.Told.Select(pair => pair.Item), Is.EqualTo(new[] { 0, 2, 3 }));
            Assert.That(About(warnings), Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain(nameof(OperationCanceledException)));
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void AnOutOfMemoryExceptionIsNotHeldBackTest(string strategy)
        {
            var collection = new MockAskingCollection<object?>(RESULTS)
            {
                Throws = (_, _) => new OutOfMemoryException()
            };

            GridValueRule rule = GridValueRule.ForCollection(collection, Items(3), null);

            Assert.That(async () => await GridTestRuns.RunAsync(strategy, Honest(), new[] { Node(10) }, MakeTasks(3), rule),
                Throws.InstanceOf<OutOfMemoryException>());
        }

        #endregion

        #region Signature Tests

        [TestCaseSource(nameof(OTHER_SIGNATURES))]
        public async Task AMethodOfTheNameWithAnotherSignatureIsWarnedOfOnceAndNeverCalledTest(IWitVariable collection, Func<IWitVariable, int> calls)
        {
            var warnings = new List<string>();

            GridValueRule rule = GridValueRule.ForCollection(collection, Items(4), warnings.Add);

            Assert.That(rule, Is.SameAs(GridValueRule.ANY), "the rule the collection always had");
            Assert.That(rule.Tells, Is.False);
            Assert.That(About(warnings), Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain($"the return collection '{RESULTS}' ({collection.GetType().Name})"));
            Assert.That(warnings[0], Does.Contain("public void OnGridAnswer(object item, object value)"));
            Assert.That(warnings[0], Does.Contain("is not told"));

            foreach (string strategy in new[] { BALANCED, QUEUED })
            {
                var results = await GridTestRuns.RunAsync(strategy, Honest(), new[] { Node(20), Node(10) }, MakeTasks(4), rule, warnings);

                Assert.That(results, Has.Count.EqualTo(4));
            }

            rule.WarnOfUntold(warnings.Add);

            Assert.That(calls(collection), Is.EqualTo(0));
            Assert.That(warnings, Has.Count.EqualTo(1), "one warning, when the method is looked for");
        }

        [Test]
        public async Task AMethodThatIsNotPublicIsNotLookedAtTest()
        {
            var warnings = new List<string>();
            var collection = new MockHiddenMethodCollection(RESULTS);

            GridValueRule rule = GridValueRule.ForCollection(collection, Items(3), warnings.Add);

            var results = await GridTestRuns.RunAsync(BALANCED, Honest(), new[] { Node(10) }, MakeTasks(3), rule, warnings);

            Assert.That(rule, Is.SameAs(GridValueRule.ANY));
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(collection.Calls, Is.EqualTo(0));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task AMethodOfTheClassBelowIsFoundBesideAnotherOfItsNameTest()
        {
            var warnings = new List<string>();
            var collection = new MockAskingDerivedCollection(RESULTS);

            GridValueRule rule = GridValueRule.ForCollection(collection, Items(3), warnings.Add);

            var results = await GridTestRuns.RunAsync(BALANCED, Honest(), new[] { Node(10) }, MakeTasks(3), rule, warnings);

            Assert.That(rule.Tells, Is.True);
            Assert.That(results, Has.Count.EqualTo(3));
            Assert.That(collection.Told.Select(pair => pair.Item), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(collection.OtherCalls, Is.EqualTo(0));
            Assert.That(warnings, Is.Empty);
        }

        #endregion

        #region Threads Tests

        [Test]
        public async Task UnderQueuedTheMethodIsCalledFromTheNodesOwnLoopsWithNoLockAroundItTest()
        {
            // Two nodes take one task each and answer at the same moment. Each call of the method
            // waits for the other to be inside it as well: with a lock of Grid's around the method
            // the second call could not enter while the first waits, and the wait would run out.
            using var together = new Barrier(2);
            int met = 0;

            var collection = new MockAskingCollection<string>(RESULTS)
            {
                Throws = (_, _) =>
                {
                    if (together.SignalAndWait(TimeSpan.FromSeconds(30)))
                        Interlocked.Increment(ref met);

                    return null;
                }
            };

            GridValueRule rule = GridValueRule.ForCollection(collection, Items(2), null);

            var results = await GridTestRuns.RunAsync(QUEUED, new MockGatedNodesManager(2), new[] { Node(20), Node(10) }, MakeTasks(2), rule);

            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(met, Is.EqualTo(2), "both calls were inside the method at once");
            Assert.That(collection.Told.Select(pair => pair.Item), Is.EquivalentTo(new[] { 0, 1 }));
        }

        #endregion
    }
}
