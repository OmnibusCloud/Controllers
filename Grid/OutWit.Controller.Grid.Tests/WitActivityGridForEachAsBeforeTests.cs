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
    /// What a Grid.ForEach call has always done for a return collection that asks for nothing, held
    /// down to the object: the results are the very objects the nodes returned, in the order the
    /// answers were taken, and a node is sent a task, its variables and the one name it is asked for,
    /// and nothing else. These tests state it by name, with a real engine and in-process nodes, so
    /// that a change to how answers are kept or tasks are built has to keep both. The last test holds
    /// a call whose return collection asks to be told to the same tasks, letter for letter.
    /// </summary>
    [TestFixture]
    public class WitActivityGridForEachAsBeforeTests
    {
        #region Constants

        private const string BALANCED = "Balanced";

        private const string QUEUED = "Queued";

        private const string RESULTS = "results";

        /// <summary>
        /// The calls whose tasks are held to the letter: the strategy, the call, its transformer and
        /// what every task carries besides its return slot. An item that is a plain value travels
        /// under the name of the iteration variable, an item that is a list travels as its members,
        /// and of the variables of the script a task carries the ones its transformer names.
        /// </summary>
        private static readonly TestCaseData[] CALLS =
        {
            Call(BALANCED, "item in [10, 11, 12], opts) => Int(item)", "Int(item)", "item=10", "item=11", "item=12"),
            Call(QUEUED, "item in [10, 11, 12], opts) => Int(item)", "Int(item)", "item=10", "item=11", "item=12"),
            Call(BALANCED, "item in [10, 11], opts) => Int(offset)", "Int(offset)", "item=10, offset=7", "item=11, offset=7"),
            Call(QUEUED, "item in [10, 11], opts) => Int(offset)", "Int(offset)", "item=10, offset=7", "item=11, offset=7"),
            Call(BALANCED, "pair in pairs, opts) => Int(pair.Item2)", "Int(pair.Item2)", "pair.Item1=1, pair.Item2=4", "pair.Item1=2, pair.Item2=5", "pair.Item1=3, pair.Item2=6"),
            Call(QUEUED, "pair in pairs, opts) => Int(pair.Item2)", "Int(pair.Item2)", "pair.Item1=1, pair.Item2=4", "pair.Item1=2, pair.Item2=5", "pair.Item1=3, pair.Item2=6")
        };

        #endregion

        #region Fields

        private string m_controllersPath = null!;

        private IReadOnlyList<IWitEngineActivityNode> m_fleet = null!;

        #endregion

        #region Initialization

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            m_controllersPath = TestControllers.FindPath()
                                ?? throw new DirectoryNotFoundException("@Controllers directory not found");

            m_fleet = new IWitEngineActivityNode[]
            {
                new MockActivityNode(WitEngineNodeSdk.Instance, 3, Guid.NewGuid(), iterations: 1),
                new MockActivityNode(WitEngineNodeSdk.Instance, 2, Guid.NewGuid(), iterations: 1),
                new MockActivityNode(WitEngineNodeSdk.Instance, 1, Guid.NewGuid(), iterations: 1)
            };
        }

        #endregion

        #region Tools

        private static TestCaseData Call(string strategy, string call, string transformer, params string[] variables)
        {
            return new TestCaseData(strategy, call, transformer, variables).SetArgDisplayNames(strategy, call);
        }

        /// <summary>
        /// The nodes of a call: the three machines under Balanced, one under Queued, where the tasks
        /// of the queue then reach the node one after another.
        /// </summary>
        private IReadOnlyList<IWitEngineActivityNode> Fleet(string strategy)
        {
            return strategy == QUEUED ? new[] { m_fleet[0] } : m_fleet;
        }

        private static string Script(string strategy, string collection, string call)
        {
            return $$"""
                     Job:TestJob()
                     {
                         ProcessingOptions:opts = ProcessingOptions("{{strategy}}");
                         Int:offset = Int(7);
                         Object:unused = Object("host-only");
                         Array:first = [1, 2, 3];
                         Array:second = [4, 5, 6];
                         TupleCollection:pairs = Zip(first, second);
                         {{collection}}:{{RESULTS}} = Grid.ForEach({{call}};
                     }
                     """;
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

        /// <summary>
        /// What the nodes of a call are sent, one line a task: the task's activity with its return
        /// slot, its variables by name, the one name the node is asked for, and how the task travels
        /// (in a batch any node may take under Balanced, alone under Queued).
        /// </summary>
        private static List<string> Sent(string strategy, string transformer, string[] variables)
        {
            string travels = strategy == BALANCED ? "any node" : "alone";

            return variables
                .Select((carried, slot) => $"WitActivityInteger return{slot} = {transformer}; | pool: {carried}, return{slot}=null | asks: return{slot} | {travels}")
                .ToList();
        }

        private static List<object?> Values(IWitJob job)
        {
            return job.Variables[RESULTS].Value is IEnumerable values
                ? values.Cast<object?>().ToList()
                : new List<object?>();
        }

        #endregion

        #region Same Objects Tests

        [TestCase(BALANCED, "ObjectCollection")]
        [TestCase(QUEUED, "ObjectCollection")]
        [TestCase(BALANCED, "StringCollection")]
        [TestCase(QUEUED, "StringCollection")]
        public void ACallStoresTheVeryObjectsItsNodesReturnedInTheOrderTheAnswersAreTakenTest(string strategy, string collection)
        {
            // Every value a node returns is replaced on its way back by a text made for it alone, so
            // that the same object can be told from an equal one.
            var returned = new List<(Guid NodeId, List<object?> Values)>();
            var nodes = new MockNodesManager
            {
                CompatibleNodes = Fleet(strategy),
                Answers = (nodeId, answers) =>
                {
                    var changed = answers
                        .Select(answer => (IWitVariable)new WitVariableObject(answer.Name, string.Concat("value of ", answer.Name)))
                        .ToList();

                    lock (returned)
                        returned.Add((nodeId, changed.Select(answer => answer.Value).ToList()));

                    return changed;
                }
            };

            WitEngineSdk.Instance.Reload(Guid.NewGuid(), nodes, false, null, m_controllersPath);

            var job = WitEngineSdk.Instance.Compile(Script(strategy, collection, "item in [10, 11, 12, 13, 14, 15, 16], opts) => Int(item)"));

            var status = Run(job);
            var values = Values(job);

            // Balanced takes the batches in the order they were sent, each as its node returned it.
            // Queued takes the tasks in their own order, which is the order one node is given them.
            List<object?> expected = strategy == BALANCED
                ? nodes.Batches.SelectMany(batch => returned.Single(answer => answer.NodeId == batch.NodeId).Values).ToList()
                : returned.SelectMany(answer => answer.Values).ToList();

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(expected, Has.Count.EqualTo(7));
            Assert.That(values, Has.Count.EqualTo(7));

            for (int index = 0; index < expected.Count; index++)
                Assert.That(values[index], Is.SameAs(expected[index]), $"value {index}");
        }

        #endregion

        #region Sent To A Node Tests

        [TestCaseSource(nameof(CALLS))]
        public void ANodeIsSentATaskItsVariablesAndTheOneNameItIsAskedForTest(string strategy, string call, string transformer, string[] variables)
        {
            var nodes = new MockRecordingNodesManager { CompatibleNodes = Fleet(strategy) };

            WitEngineSdk.Instance.Reload(Guid.NewGuid(), nodes, false, null, m_controllersPath);

            var job = WitEngineSdk.Instance.Compile(Script(strategy, "IntCollection", call));

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(nodes.Sent.OrderBy(line => line, StringComparer.Ordinal), Is.EqualTo(Sent(strategy, transformer, variables)));
        }

        [TestCaseSource(nameof(CALLS))]
        public void ANodeIsSentTheSameWhenTheReturnCollectionAsksToBeToldTest(string strategy, string call, string transformer, string[] variables)
        {
            // For a collection that asks, Grid writes down which item every return slot was built
            // for. That stays on the host: the tasks are, to the letter, the tasks of the test above.
            var nodes = new MockRecordingNodesManager { CompatibleNodes = Fleet(strategy) };

            WitEngineSdk.Instance.Reload(Guid.NewGuid(), nodes, false, null, m_controllersPath);

            var job = WitEngineSdk.Instance.Compile(Script(strategy, "IntCollection", call));
            var asking = new MockAskingCollection<int>(RESULTS);

            Assert.That(job.Variables.TryRemove(RESULTS), Is.True);
            job.Variables.Merge(new IWitVariable[] { asking });

            var status = Run(job);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(asking.Told, Has.Count.EqualTo(variables.Length), "the collection did ask, and was told");
            Assert.That(nodes.Sent.OrderBy(line => line, StringComparer.Ordinal), Is.EqualTo(Sent(strategy, transformer, variables)));
        }

        #endregion
    }
}
