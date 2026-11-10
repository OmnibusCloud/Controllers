using System;
using System.IO;
using System.Linq;
using System.Threading;
using OutWit.Controller.Grid.Activities;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Engine.Data.Constants;
using OutWit.Engine.Data.Exceptions;
using OutWit.Engine.Data.Processing;
using OutWit.Engine.Data.Utils;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Grid.Tests
{
    [TestFixture]
    public class WitActivityGridReplicasTests
    {
        private MockNodesManager m_nodes = null!;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            var controllersPath = FindControllersPath()
                                  ?? throw new DirectoryNotFoundException("@Controllers directory not found");

            m_nodes = new MockNodesManager
            {
                CompatibleNodes = new IWitEngineActivityNode[]
                {
                    new MockActivityNode(WitEngineNodeSdk.Instance, 3, Guid.NewGuid(), iterations: 1),
                    new MockActivityNode(WitEngineNodeSdk.Instance, 2, Guid.NewGuid(), iterations: 1),
                    new MockActivityNode(WitEngineNodeSdk.Instance, 1, Guid.NewGuid(), iterations: 1)
                }
            };

            WitEngineSdk.Instance.Reload(Guid.NewGuid(), m_nodes, false, null, controllersPath);
        }

        [SetUp]
        public void Setup()
        {
            m_nodes.Batches.Clear();
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

        private static IWitProcessingStatus Run(string options, int items)
        {
            string values = string.Join(", ", Enumerable.Range(10, items));

            string script = $$"""
                              Job:TestJob()
                              {
                                  {{options}}
                                  ObjectCollection:results = Grid.ForEach(item in [{{values}}], opts) => Int(item);
                              }
                              """;

            return Run(WitEngineSdk.Instance.Compile(script));
        }

        private int Executions()
        {
            return m_nodes.Batches.Sum(batch => batch.Requests.Count);
        }

        #region Model Tests

        [Test]
        public void AnActivityIsEqualToItsCloneAndPrintsItsArgumentsTest()
        {
            var activity = new WitActivityGridReplicas
            {
                Copies = (WitConstantNumeric)"2",
                Share = (WitConstantNumeric)"0.05"
            };

            Assert.That(activity.Is(activity.Clone()), Is.True);
            Assert.That(activity.ToString(), Is.EqualTo("Grid.Replicas(2, 0.05);"));
        }

        [Test]
        public void ADifferentShareIsADifferentActivityTest()
        {
            var activity = new WitActivityGridReplicas { Copies = (WitConstantNumeric)"2", Share = (WitConstantNumeric)"0.05" };
            var other = new WitActivityGridReplicas { Copies = (WitConstantNumeric)"2", Share = (WitConstantNumeric)"0.5" };

            Assert.That(activity.Is(other), Is.False);
        }

        #endregion

        #region Parsing Tests

        [TestCase("Grid.Replicas()")]
        [TestCase("Grid.Replicas(1, 0.5, 3)")]
        public void AWrongArgumentCountIsRefusedTest(string call)
        {
            string script = $$"""
                              Job:TestJob()
                              {
                                  ProcessingOptions:opts = {{call}};
                              }
                              """;

            Assert.That(() => WitEngineSdk.Instance.Compile(script),
                Throws.InstanceOf<WitEngineActivityParsingException<WitActivityGridReplicas>>());
        }

        #endregion

        #region Processing Tests

        [TestCase(2, 6)]
        [TestCase(3, 9)]
        public void EveryItemRunsTheRequestedNumberOfTimesTest(int copies, int expected)
        {
            var status = Run($"ProcessingOptions:opts = Grid.Replicas({copies});", 3);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Executions(), Is.EqualTo(expected));
        }

        [Test]
        public void AShareSelectsThatPartOfTheItemsTest()
        {
            var status = Run("ProcessingOptions:opts = Grid.Replicas(2, 0.25);", 8);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Executions(), Is.EqualTo(10));
        }

        [Test]
        public void TheCopiesOfAnItemGoToDifferentNodesTest()
        {
            Run("ProcessingOptions:opts = Grid.Replicas(3);", 4);

            Assert.That(m_nodes.Batches.Select(batch => batch.NodeId).Distinct().Count(), Is.EqualTo(3));

            foreach (var batch in m_nodes.Batches)
            {
                var items = batch.Requests.Select(request => request.Pool["item"].Value).ToList();

                Assert.That(items.Distinct().Count(), Is.EqualTo(items.Count), "one node received an item twice");
            }
        }

        [Test]
        public void ABatchOfACallWithCopiesMustRunOnItsExactNodeTest()
        {
            Run("ProcessingOptions:opts = Grid.Replicas(2);", 4);

            Assert.That(m_nodes.Batches.SelectMany(batch => batch.Requests).All(request => request.ExactNode), Is.True);
        }

        [Test]
        public void ACallWithoutCopiesIsNotBoundToANodeTest()
        {
            var status = Run("ProcessingOptions:opts = ProcessingOptions(\"Balanced\");", 4);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Executions(), Is.EqualTo(4));
            Assert.That(m_nodes.Batches.SelectMany(batch => batch.Requests).Any(request => request.ExactNode), Is.False);
        }

        [Test]
        public void AShareOfZeroMakesNoCopiesTest()
        {
            var status = Run("ProcessingOptions:opts = Grid.Replicas(2, 0);", 4);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(Executions(), Is.EqualTo(4));
            Assert.That(m_nodes.Batches.SelectMany(batch => batch.Requests).Any(request => request.ExactNode), Is.False);
            Assert.That(((WitProcessingOptions)m_nodes.LastOptions!).Replicas, Is.EqualTo(1), "the server must not treat this call as one with copies");
        }

        [Test]
        public void ReturnSlotsOfACallWithCopiesAreUniqueTest()
        {
            Run("ProcessingOptions:opts = Grid.Replicas(2);", 5);

            var names = m_nodes.Batches.SelectMany(batch => batch.Requests).SelectMany(request => request.ReturnVariables).ToList();

            Assert.That(names, Has.Count.EqualTo(10));
            Assert.That(names.Distinct().Count(), Is.EqualTo(10));
        }

        [TestCase("Grid.Replicas(0)")]
        [TestCase("Grid.Replicas(4)")]
        [TestCase("Grid.Replicas(2, 1.5)")]
        [TestCase("Grid.Replicas(2, -0.1)")]
        public void AnOutOfRangeArgumentFailsTheJobTest(string call)
        {
            var status = Run($"ProcessingOptions:opts = {call};", 2);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(status.Message, Does.Contain("Grid.Replicas"));
            Assert.That(m_nodes.Batches, Is.Empty);
        }

        [Test]
        public void TheQueuedStrategyWithCopiesFailsWithAClearMessageTest()
        {
            string script = """
                            Job:TestJob()
                            {
                                ProcessingOptions:opts;
                                ObjectCollection:results = Grid.ForEach(item in [1, 2], opts) => Int(item);
                            }
                            """;

            var job = WitEngineSdk.Instance.Compile(script);
            Assert.That(job.Variables.TrySetValue("opts", new WitProcessingOptions { Strategy = ProcessingStrategy.Queued, Replicas = 2 }), Is.True);

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(status.Message, Does.Contain("Balanced"));
            Assert.That(m_nodes.Batches, Is.Empty);
        }

        #endregion

        private static string? FindControllersPath()
        {
            return TestControllers.FindPath();
        }
    }
}
