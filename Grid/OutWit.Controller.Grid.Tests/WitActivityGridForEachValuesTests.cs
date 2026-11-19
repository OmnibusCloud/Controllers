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
    /// Grid.ForEach with a real engine, a real typed collection and nodes whose answers a test may
    /// change on their way back. One null or one value of another type under a slot a node was asked
    /// for costs the call that value and nothing else, under both strategies; a collection of objects
    /// takes everything, as before; a call with nowhere to put its results stores nothing and completes,
    /// as before; and a collection that does not hold the values after they were stored fails the job,
    /// whatever the engine answered.
    /// </summary>
    [TestFixture]
    public class WitActivityGridForEachValuesTests
    {
        #region Constants

        private const string BALANCED = "Balanced";

        private const string QUEUED = "Queued";

        private const string RESULTS = "results";

        private const string REFUSED = "would refuse";

        private const string NOT_ASKED = "did not ask for";

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
        /// Compiles a job of one Grid.ForEach call over the numbers from ten on.
        /// </summary>
        /// <param name="assignment">What stands before the call: "IntCollection:results = ", or nothing for a call that is not assigned.</param>
        /// <param name="items">How many items the call has.</param>
        /// <param name="strategy">The strategy of the call.</param>
        /// <param name="transformer">The transformer; it returns its item by default.</param>
        /// <param name="options">The options of the call, when they are not a plain strategy.</param>
        private IWitJob Compile(string assignment, int items, string strategy, string transformer = "Int(item)", string? options = null)
        {
            // One worker under Queued: the tasks of the queue then reach the test node one after another.
            if (strategy == QUEUED)
                m_nodes.CompatibleNodes = new[] { m_fleet[0] };

            string values = string.Join(", ", Enumerable.Range(10, items));
            string optionsLine = options ?? $"ProcessingOptions:opts = ProcessingOptions(\"{strategy}\");";

            string script = $$"""
                              Job:TestJob()
                              {
                                  {{optionsLine}}
                                  {{assignment}}Grid.ForEach(item in [{{values}}], opts) => {{transformer}};
                              }
                              """;

            return WitEngineSdk.Instance.Compile(script);
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

        private static IReadOnlyList<IWitVariable> With(IReadOnlyList<IWitVariable> answers, int index, object? value)
        {
            var changed = answers.ToList();
            changed[index] = new WitVariableObject(changed[index].Name, value);

            return changed;
        }

        /// <summary>
        /// One node changes one value on its way back: the first value of the first answer that has one.
        /// The name stays, so the value comes under a slot the node was asked for.
        /// </summary>
        private void OneValueComesBackAs(object? value)
        {
            bool changed = false;

            m_nodes.Answers = (_, answers) =>
            {
                if (changed || answers.Count == 0)
                    return answers;

                changed = true;
                return With(answers, 0, value);
            };
        }

        private void EveryValueComesBackAs(object? value)
        {
            m_nodes.Answers = (_, answers) => answers.Select(answer => (IWitVariable)new WitVariableObject(answer.Name, value)).ToList();
        }

        private List<IReadOnlyList<string>> AskedNames()
        {
            return m_nodes.Batches.SelectMany(batch => batch.Requests).Select(request => request.ReturnVariables)
                .Concat(m_nodes.Singles.Select(single => single.ReturnVariables))
                .ToList();
        }

        #endregion

        #region As Before Tests

        [TestCase(BALANCED, "IntCollection")]
        [TestCase(QUEUED, "IntCollection")]
        [TestCase(BALANCED, "ObjectCollection")]
        [TestCase(QUEUED, "ObjectCollection")]
        public void ACallWhoseNodesAnswerAsAskedReturnsEveryValueAsBeforeTest(string strategy, string collection)
        {
            var job = Compile($"{collection}:{RESULTS} = ", 6, strategy);

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.EquivalentTo(Enumerable.Range(10, 6)));
            Assert.That(AskedNames(), Has.Count.EqualTo(6));
            Assert.That(AskedNames().Select(names => names.Count), Is.All.EqualTo(1), "a task asks for one value");
            Assert.That(AskedNames().Select(names => names[0]).Distinct().Count(), Is.EqualTo(6));
            Assert.That(m_logger.WarningsWith(REFUSED), Is.Empty);
            Assert.That(m_logger.WarningsWith(NOT_ASKED), Is.Empty);
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void ACollectionOfObjectsTakesANullAsBeforeTest(string strategy)
        {
            OneValueComesBackAs(null);

            var job = Compile($"ObjectCollection:{RESULTS} = ", 6, strategy);

            var status = Run(job);
            var values = Values(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(values, Has.Count.EqualTo(6));
            Assert.That(values.Count(value => value == null), Is.EqualTo(1));
            Assert.That(values.Where(value => value != null), Is.SubsetOf(Enumerable.Range(10, 6)));
            Assert.That(m_logger.WarningsWith(REFUSED), Is.Empty);
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void ACollectionOfObjectsTakesAValueOfAnyTypeAsBeforeTest(string strategy)
        {
            OneValueComesBackAs("text");

            var job = Compile($"ObjectCollection:{RESULTS} = ", 6, strategy);

            var status = Run(job);
            var values = Values(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(values, Has.Count.EqualTo(6));
            Assert.That(values.Count(value => Equals(value, "text")), Is.EqualTo(1));
        }

        #endregion

        #region Refused Value Tests

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void ANullUnderAnAskedSlotCostsATypedCollectionThatValueAloneTest(string strategy)
        {
            // Up to 1.3.0 the collection refused the whole list and stayed empty, the job completed
            // and the script went on.
            OneValueComesBackAs(null);

            var job = Compile($"IntCollection:{RESULTS} = ", 6, strategy);

            var status = Run(job);
            var values = Values(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(values, Has.Count.EqualTo(5), "the five values of the honest answers");
            Assert.That(values, Is.All.InstanceOf<int>());
            Assert.That(values, Is.Unique);
            Assert.That(values, Is.SubsetOf(Enumerable.Range(10, 6)));
            Assert.That(m_logger.WarningsWith(REFUSED), Has.Count.EqualTo(1));
            Assert.That(m_logger.WarningsWith(REFUSED)[0], Does.Contain("1 value(s) the return collection would refuse (1 null where Int32 was expected)"));
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void AValueOfAnotherTypeUnderAnAskedSlotCostsATypedCollectionThatValueAloneTest(string strategy)
        {
            OneValueComesBackAs("text");

            var job = Compile($"IntCollection:{RESULTS} = ", 6, strategy);

            var status = Run(job);
            var values = Values(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(values, Has.Count.EqualTo(5));
            Assert.That(values, Is.All.InstanceOf<int>());
            Assert.That(values, Is.Unique);
            Assert.That(values, Is.SubsetOf(Enumerable.Range(10, 6)));
            Assert.That(m_logger.WarningsWith(REFUSED), Has.Count.EqualTo(1));
            Assert.That(m_logger.WarningsWith(REFUSED)[0], Does.Contain("1 value(s) the return collection would refuse (1 of type String where Int32 was expected)"));
        }

        [Test]
        public void ANullAmongTheCopiesOfACallCostsThatValueAloneTest()
        {
            OneValueComesBackAs(null);

            var job = Compile($"IntCollection:{RESULTS} = ", 3, BALANCED, options: "ProcessingOptions:opts = Grid.Replicas(2);");

            var status = Run(job);
            var values = Values(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(m_nodes.Batches.Sum(batch => batch.Requests.Count), Is.EqualTo(6), "three items, two executions each, none of them run again");
            Assert.That(values, Has.Count.EqualTo(5), "six executions, one value left out");
            Assert.That(values, Is.All.InstanceOf<int>());
            Assert.That(values.Distinct(), Is.EquivalentTo(Enumerable.Range(10, 3)), "every item still has a result");
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void WhenEveryValueIsRefusedTheCollectionIsEmptyAndTheJobCompletesTest(string strategy)
        {
            EveryValueComesBackAs(null);

            var job = Compile($"IntCollection:{RESULTS} = ", 4, strategy);

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.Empty);
        }

        #endregion

        #region Nothing To Store Tests

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void ACallThatIsNotAssignedStoresNothingAndCompletesTest(string strategy)
        {
            EveryValueComesBackAs(null);

            var job = Compile(string.Empty, 3, strategy);

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(AskedNames(), Has.Count.EqualTo(3), "the transformer returns a value, so every task still has its slot");
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void ACallAssignedToAVariableThatIsNotACollectionStoresNothingAndCompletesAsBeforeTest(string strategy)
        {
            var job = Compile($"Object:{RESULTS} = ", 3, strategy);

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(job.Variables[RESULTS].Value, Is.Null);
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void ATransformerThatReturnsNothingLeavesAnEmptyCollectionAndCompletesTest(string strategy)
        {
            var job = Compile($"IntCollection:{RESULTS} = ", 3, strategy, "Trace(item)");

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.Empty);
            Assert.That(AskedNames().Select(names => names.Count), Is.All.EqualTo(0), "a transformer that returns nothing has no slot");
        }

        #endregion

        #region Loud Failure Tests

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void ACollectionThatDoesNotHoldTheValuesAfterTheyWereStoredFailsTheJobTest(string strategy)
        {
            // The collection refuses the list and keeps the two values of an earlier call. An engine
            // before Data 1.1.6 answers that the list was taken; a newer one answers that it was not.
            // Either way the script must not go on.
            var job = Compile($"IntCollection:{RESULTS} = ", 3, strategy);
            var refusing = new MockRefusingCollection(RESULTS, new[] { 1, 2 });

            Assert.That(job.Variables.TryRemove(RESULTS), Is.True);
            job.Variables.Merge(new IWitVariable[] { refusing });

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(status.Message, Does.Contain("Grid.ForEach => Int"));
            Assert.That(status.Message, Does.Contain($"the return collection '{RESULTS}' did not take the 3 value(s) of the call"));
            Assert.That(status.Message, Does.Contain("it holds 2 value(s)"));
            Assert.That(status.Message, Does.Contain("Its elements are of type Int32"));
            Assert.That(refusing.Refusals, Is.EqualTo(1));
            Assert.That(refusing.Value, Is.EqualTo(new[] { 1, 2 }));
        }

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void AnEmptyResultIntoAnEmptyCollectionStaysSilentWhateverTheCollectionAnswersTest(string strategy)
        {
            var job = Compile($"IntCollection:{RESULTS} = ", 3, strategy, "Trace(item)");
            var refusing = new MockRefusingCollection(RESULTS);

            Assert.That(job.Variables.TryRemove(RESULTS), Is.True);
            job.Variables.Merge(new IWitVariable[] { refusing });

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(refusing.Refusals, Is.EqualTo(1), "the empty list was offered");
            Assert.That(refusing.Value, Is.Empty);
        }

        #endregion

        #region Slot Tests

        [TestCase(BALANCED)]
        [TestCase(QUEUED)]
        public void AScriptVariableWhoseNameStartsWithReturnIsNotAskedOfTheNodesTest(string strategy)
        {
            // Up to 1.3.0 the variable was asked for with every task's slot and came back as a second
            // value of every item: six values for three items.
            if (strategy == QUEUED)
                m_nodes.CompatibleNodes = new[] { m_fleet[0] };

            string script = $$"""
                              Job:TestJob()
                              {
                                  ProcessingOptions:opts = ProcessingOptions("{{strategy}}");
                                  Int:returnCode = Int(7);
                                  IntCollection:{{RESULTS}} = Grid.ForEach(item in [1, 2, 3], opts) => Int(returnCode);
                              }
                              """;

            var job = WitEngineSdk.Instance.Compile(script);

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Values(job), Is.EqualTo(new[] { 7, 7, 7 }));
            Assert.That(AskedNames(), Has.Count.EqualTo(3));
            Assert.That(AskedNames().Select(names => names.Count), Is.All.EqualTo(1));
            Assert.That(AskedNames().SelectMany(names => names), Has.None.EqualTo("returnCode"));
        }

        [Test]
        public void ADelegatedActivityIsAskedForItsOwnSlotAloneTest()
        {
            string script = """
                            Job:TestJob()
                            {
                                Int:returnCode = Int(7);
                                Int:result = Grid.Delegate => Int(returnCode);
                            }
                            """;

            var job = WitEngineSdk.Instance.Compile(script);

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(job.Variables["result"].Value, Is.EqualTo(7));
            Assert.That(AskedNames(), Has.Count.EqualTo(1));
            Assert.That(AskedNames()[0], Is.EqualTo(new[] { "return0" }));
        }

        #endregion
    }
}
