using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Grid.Tests
{
    /// <summary>
    /// A return collection that asks to be told which item each value answers, with a real engine and
    /// in-process nodes. The collection declares <c>public void OnGridAnswer(object? item, object?
    /// value)</c>; Grid calls it once for every value it keeps, with the element of the call's
    /// collection the task was built from. The transformers here return something of their item, so
    /// a pair that does not belong together shows.
    /// </summary>
    [TestFixture]
    public class WitActivityGridForEachItemsTests
    {
        #region Constants

        private const string BALANCED = "Balanced";

        private const string QUEUED = "Queued";

        private const string RESULTS = "results";

        private const string METHOD = "OnGridAnswer";

        #endregion

        #region Fields

        private MockNodesManager m_nodes = null!;

        private MockLogger m_logger = null!;

        private IReadOnlyList<IWitEngineActivityNode> m_fleet = null!;

        #endregion

        #region Initialization

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            var controllersPath = TestControllers.FindPath()
                                  ?? throw new DirectoryNotFoundException("@Controllers directory not found");

            m_fleet = new IWitEngineActivityNode[]
            {
                new MockActivityNode(WitEngineNodeSdk.Instance, 3, Guid.NewGuid(), iterations: 1),
                new MockActivityNode(WitEngineNodeSdk.Instance, 2, Guid.NewGuid(), iterations: 1),
                new MockActivityNode(WitEngineNodeSdk.Instance, 1, Guid.NewGuid(), iterations: 1)
            };

            m_nodes = new MockNodesManager { CompatibleNodes = m_fleet };
            m_logger = new MockLogger();

            WitEngineSdk.Instance.Reload(Guid.NewGuid(), m_nodes, false, m_logger, controllersPath);
        }

        [SetUp]
        public void Setup()
        {
            m_nodes.Batches.Clear();
            m_nodes.Singles.Clear();
            m_nodes.Answers = null;
            m_nodes.CompatibleNodes = m_fleet;
            m_logger.Clear();
        }

        #endregion

        #region Tools

        /// <summary>
        /// Compiles a job of one Grid.ForEach call into the variable the tests then replace.
        /// </summary>
        /// <param name="strategy">The strategy of the call.</param>
        /// <param name="call">The call from its opening bracket on, without the last semicolon.</param>
        /// <param name="options">The options of the call, when they are not a plain strategy.</param>
        private IWitJob Compile(string strategy, string call, string? options = null)
        {
            // One worker under Queued: the tasks of the queue then reach the test node one after another.
            if (strategy == QUEUED)
                m_nodes.CompatibleNodes = new[] { m_fleet[0] };

            string optionsLine = options ?? $"ProcessingOptions:opts = ProcessingOptions(\"{strategy}\");";

            string script = $$"""
                              Job:TestJob()
                              {
                                  {{optionsLine}}
                                  Array:first = [1, 2, 3];
                                  Array:second = [4, 5, 6];
                                  TupleCollection:pairs = Zip(first, second);
                                  ObjectCollection:{{RESULTS}} = Grid.ForEach({{call}};
                              }
                              """;

            return WitEngineSdk.Instance.Compile(script);
        }

        /// <summary>
        /// Puts a collection of the test's own in the place of the variable the call returns into.
        /// </summary>
        private static TCollection Returning<TCollection>(IWitJob job, TCollection collection)
            where TCollection : IWitVariable
        {
            Assert.That(job.Variables.TryRemove(RESULTS), Is.True);
            job.Variables.Merge(new IWitVariable[] { collection });

            return collection;
        }

        private static IWitProcessingStatus Run(IWitJob job)
        {
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);
            var finished = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { finished.Set(); };
            task.Run();

            Assert.That(finished.WaitOne(TimeSpan.FromSeconds(60)), Is.True, "the job did not finish");

            return task.Status!;
        }

        private static List<object?> Values(IWitJob job)
        {
            return job.Variables[RESULTS].Value is IEnumerable values
                ? values.Cast<object?>().ToList()
                : new List<object?>();
        }

        private static List<object?> Items(IWitJob job, string name)
        {
            return ((IEnumerable)job.Variables[name].Value!).Cast<object?>().ToList();
        }

        #endregion

        #region Told Tests

        [TestCase(BALANCED, true)]
        [TestCase(QUEUED, true)]
        [TestCase(BALANCED, false)]
        [TestCase(QUEUED, false)]
        public void ACollectionThatAsksIsToldEveryValueWithTheItemItAnswersTest(string strategy, bool typed)
        {
            var job = Compile(strategy, "item in [10, 11, 12, 13, 14, 15], opts) => Int(item)");

            var integers = new MockAskingCollection<int>(RESULTS);
            var objects = new MockAskingCollection<object?>(RESULTS);

            Returning<IWitVariable>(job, typed ? integers : objects);

            var status = Run(job);
            var told = typed ? integers.Told : objects.Told;

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.EquivalentTo(Enumerable.Range(10, 6)));
            Assert.That(told, Has.Count.EqualTo(6), "one call for every value that is kept");
            Assert.That(told.Select(pair => pair.Item), Is.EquivalentTo(Enumerable.Range(10, 6)));
            Assert.That(told.Where(pair => !Equals(pair.Item, pair.Value)), Is.Empty, "the transformer returns its item, so a value answers the item it is told with");
            Assert.That(m_logger.WarningsWith(METHOD), Is.Empty);
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void AnItemThatIsATextIsToldAsItIsTest(string strategy)
        {
            var job = Compile(strategy, "name in [\"north\", \"south\", \"east\"], opts) => String(name)");
            var collection = Returning(job, new MockAskingCollection<string>(RESULTS));

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.EquivalentTo(new[] { "north", "south", "east" }));
            Assert.That(collection.Told, Has.Count.EqualTo(3));
            Assert.That(collection.Told.Select(pair => pair.Item), Is.EquivalentTo(new[] { "north", "south", "east" }));
            Assert.That(collection.Told.Where(pair => !Equals(pair.Item, pair.Value)), Is.Empty);
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void AnItemThatIsAListIsToldAsTheListItselfTest(string strategy)
        {
            // A task receives a list as its members (pair.Item1, pair.Item2); the collection is told
            // the list, the very element of the call's collection.
            var job = Compile(strategy, "pair in pairs, opts) => Int(pair.Item2)");
            var collection = Returning(job, new MockAskingCollection<object?>(RESULTS));

            var status = Run(job);
            var pairs = Items(job, "pairs");

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.EquivalentTo(new[] { 4, 5, 6 }));
            Assert.That(collection.Told, Has.Count.EqualTo(3));

            foreach (var (item, value) in collection.Told)
            {
                Assert.That(pairs.Count(pair => ReferenceEquals(pair, item)), Is.EqualTo(1), "the item is an element of the call's collection, the same object");
                Assert.That(((IEnumerable)item!).Cast<object?>().Last(), Is.EqualTo(value), "the value is the second member of the list it is told with");
            }
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void TheValuesToldAreTheObjectsThatJoinTheResultsTest(string strategy)
        {
            // Every value a node returns is replaced on its way back by a text made for it alone.
            m_nodes.Answers = (_, answers) => answers
                .Select(answer => (IWitVariable)new WitVariableObject(answer.Name, string.Concat("value of ", answer.Name)))
                .ToList();

            var job = Compile(strategy, "item in [10, 11, 12, 13, 14], opts) => Int(item)");
            var collection = Returning(job, new MockAskingCollection<object?>(RESULTS));

            var status = Run(job);
            var values = Values(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(values, Has.Count.EqualTo(5));
            Assert.That(collection.Told, Has.Count.EqualTo(5));

            foreach (object? value in values)
                Assert.That(collection.Told.Count(pair => ReferenceEquals(pair.Value, value)), Is.EqualTo(1), $"{value}");
        }

        #endregion

        #region Copies Tests

        [Test]
        public void EveryCopyOfAnItemIsToldWithTheSameItemTest()
        {
            var job = Compile(BALANCED, "item in [10, 11, 12], opts) => Int(item)", "ProcessingOptions:opts = Grid.Replicas(2);");
            var collection = Returning(job, new MockAskingCollection<object?>(RESULTS));

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(m_nodes.Batches.Sum(batch => batch.Requests.Count), Is.EqualTo(6), "three items, two executions each");
            Assert.That(Values(job), Has.Count.EqualTo(6));
            Assert.That(collection.Told, Has.Count.EqualTo(6), "every copy has its own slot and its own call");
            Assert.That(collection.Told.Where(pair => !Equals(pair.Item, pair.Value)), Is.Empty);

            foreach (var copies in collection.Told.GroupBy(pair => pair.Value))
            {
                Assert.That(copies.Count(), Is.EqualTo(2), $"item {copies.Key}");
                Assert.That(copies.First().Item, Is.SameAs(copies.Last().Item), "the two copies are told the same object");
            }
        }

        #endregion

        #region Another Item Tests

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void AValueOfAnotherItemUnderASlotIsToldWithTheItemOfThatSlotTest(string strategy)
        {
            // A node that was given several items returns the result of its first one under every
            // slot it was asked for. Grid cannot look into a value and keeps them all; the collection
            // is told each with the item of the slot it came under, and can see which do not answer it.
            int first = 0;

            m_nodes.Answers = (_, answers) =>
            {
                if (answers.Count == 0)
                    return answers;

                if (first == 0)
                    first = (int)answers[0].Value!;

                return answers.Select(answer => (IWitVariable)new WitVariableObject(answer.Name, first)).ToList();
            };

            m_nodes.CompatibleNodes = new[] { m_fleet[0] };

            var job = Compile(strategy, "item in [10, 11, 12], opts) => Int(item)");
            var collection = Returning(job, new MockAskingCollection<int>(RESULTS));

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.EqualTo(new[] { first, first, first }), "Grid keeps the three values: each came under a slot the node was asked for");
            Assert.That(collection.Told.Select(pair => pair.Item), Is.EquivalentTo(new[] { 10, 11, 12 }), "each slot is told with its own item");
            Assert.That(collection.Told.Select(pair => pair.Value), Is.All.EqualTo(first));
            Assert.That(collection.Told.Count(pair => !Equals(pair.Item, pair.Value)), Is.EqualTo(2), "two of the values do not answer the item they came for");
        }

        #endregion

        #region Not Told Tests

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void AValueThatIsLeftOutIsNotToldTest(string strategy)
        {
            // One node returns a null under its first slot (the collection of integers would refuse
            // it) and a value nobody asked for.
            bool changed = false;

            m_nodes.Answers = (_, answers) =>
            {
                if (changed || answers.Count == 0)
                    return answers;

                changed = true;

                var answer = answers.ToList();
                answer[0] = new WitVariableObject(answer[0].Name, null);
                answer.Add(new WitVariableObject("surplus", 99));

                return answer;
            };

            var job = Compile(strategy, "item in [10, 11, 12, 13, 14, 15], opts) => Int(item)");
            var collection = Returning(job, new MockAskingCollection<int>(RESULTS));

            var status = Run(job);
            var values = Values(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(values, Has.Count.EqualTo(5));
            Assert.That(collection.Told.Select(pair => pair.Value), Is.EquivalentTo(values), "what is told is what is kept");
            Assert.That(collection.Told.Where(pair => !Equals(pair.Item, pair.Value)), Is.Empty);
            Assert.That(m_logger.WarningsWith("would refuse"), Has.Count.EqualTo(1));
            Assert.That(m_logger.WarningsWith("did not ask for"), Has.Count.EqualTo(1));
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void ACollectionWithoutTheMethodIsNotAskedAndNothingIsSaidTest(string strategy)
        {
            var job = Compile(strategy, "item in [10, 11, 12], opts) => Int(item)");
            var collection = Returning(job, new MockHiddenMethodCollection(RESULTS));

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.EquivalentTo(new[] { 10, 11, 12 }));
            Assert.That(collection.Calls, Is.EqualTo(0), "a method that is not public is not the method");
            Assert.That(m_logger.WarningsWith(METHOD), Is.Empty);
        }

        #endregion

        #region Warning Tests

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void AMethodThatThrowsCostsNoValueAndIsReportedOnceACallTest(string strategy)
        {
            var job = Compile(strategy, "item in [10, 11, 12, 13, 14, 15], opts) => Int(item)");
            var collection = Returning(job, new MockAskingCollection<int>(RESULTS));

            collection.Throws = (item, _) => Equals(item, 11) || Equals(item, 14)
                ? new InvalidOperationException("the controller's own trouble")
                : null;

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.EquivalentTo(Enumerable.Range(10, 6)), "the values it threw for are kept");
            Assert.That(collection.Told.Select(pair => pair.Item), Is.EquivalentTo(new[] { 10, 12, 13, 15 }), "the others are told");
            Assert.That(m_logger.WarningsWith(METHOD), Has.Count.EqualTo(1), "one warning a call");
            Assert.That(m_logger.WarningsWith(METHOD)[0], Does.Contain($"'{RESULTS}'"));
            Assert.That(m_logger.WarningsWith(METHOD)[0], Does.Contain("threw for 2 value(s)"));
            Assert.That(m_logger.WarningsWith(METHOD)[0], Does.Contain(nameof(InvalidOperationException)));
            Assert.That(m_logger.WarningsWith(METHOD)[0], Does.Not.Contain("the controller's own trouble"), "the warning names the type, not what the controller wrote");
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void AMethodOfThatNameWithAnotherSignatureIsReportedOnceACallAndNotCalledTest(string strategy)
        {
            var job = Compile(strategy, "item in [10, 11, 12], opts) => Int(item)");
            var collection = Returning(job, new MockOneArgumentCollection(RESULTS));

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.EquivalentTo(new[] { 10, 11, 12 }));
            Assert.That(collection.Calls, Is.EqualTo(0));
            Assert.That(m_logger.WarningsWith(METHOD), Has.Count.EqualTo(1), "one warning a call");
            Assert.That(m_logger.WarningsWith(METHOD)[0], Does.Contain($"'{RESULTS}'"));
            Assert.That(m_logger.WarningsWith(METHOD)[0], Does.Contain("is not told"));
        }

        #endregion
    }
}
