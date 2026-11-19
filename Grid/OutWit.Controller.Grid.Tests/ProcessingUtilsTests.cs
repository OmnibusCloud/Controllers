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
    /// What a node is asked for, and the Queued strategy. A task asks for one value, under the return
    /// slot of its own activity: a variable of the script is never asked for, whatever its name. Under
    /// Queued every task is its own request, and its answer is held as a batch's is under Balanced: the
    /// value under the task's slot, once, when the return collection would take it. A queue whose
    /// nodes answer as asked returns exactly what it returned before those rules existed.
    /// </summary>
    [TestFixture]
    public class ProcessingUtilsTests
    {
        #region Constants

        private const string SURPLUS = "surplus";

        private static readonly GridValueRule TEXT = GridValueRule.ForElementType(typeof(string));

        #endregion

        #region Tools

        /// <summary>
        /// A task as Grid builds it for a transformer that returns a value.
        /// </summary>
        private static WitGridTask MakeBuiltTask(int index, params string[] scriptVariables)
        {
            return GridTestTasks.Built(index, null, scriptVariables);
        }

        /// <summary>
        /// A task built by hand around an activity that is not a function: the slot is the variable
        /// under one of Grid's own slot names.
        /// </summary>
        private static WitGridTask MakeHandTask(int index, params string[] scriptVariables)
        {
            return GridTestTasks.ByHand(index, null, scriptVariables);
        }

        private static string ReturnName(int index)
        {
            return GridTestTasks.ReturnName(index);
        }

        private static IWitEngineActivityNode Node(double rate)
        {
            return GridTestTasks.Node(rate);
        }

        private static IWitVariable With(IWitVariable answer, object? value)
        {
            return new WitVariableObject(answer.Name, value);
        }

        private static List<string> Refusals(List<string> warnings)
        {
            return warnings.Where(warning => warning.Contains("would refuse")).ToList();
        }

        #endregion

        #region Asked Names Tests

        [Test]
        public async Task ABatchAsksEachTaskForTheReturnReferenceOfItsActivityAloneTest()
        {
            // Up to 1.3.0 every variable whose name starts with "return" was asked for: the slot and
            // the three variables of the script.
            var manager = new MockAnsweringNodesManager(MockAnsweringNodesManager.Honest);
            var tasks = Enumerable.Range(0, 4).Select(index => MakeBuiltTask(index, "returnCode", "returned.Item1", "return77")).ToList();
            var group = WitGridTaskAllocator.Allocate(new[] { Node(10) }, tasks).Single();

            var (status, variables) = await manager.Process(group, Guid.NewGuid(), inParallel: false, exactNode: false);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(manager.Requests, Has.Count.EqualTo(4));

            foreach (var (_, request) in manager.Requests)
                Assert.That(request.ReturnVariables, Is.EqualTo(new[] { ((IWitFunction)request.Activity).ReturnReference }));

            Assert.That(variables.Select(variable => variable.Name).OrderBy(name => name, StringComparer.Ordinal), Is.EqualTo(Enumerable.Range(0, 4).Select(ReturnName)));
        }

        [Test]
        public async Task AQueuedTaskIsAskedForTheReturnReferenceOfItsActivityAloneTest()
        {
            var manager = new MockAnsweringNodesManager(MockAnsweringNodesManager.Honest);
            var tasks = Enumerable.Range(0, 4).Select(index => MakeBuiltTask(index, "returnCode", "returned.Item1", "return77")).ToList();

            var (status, variables) = await manager.ProcessQueued(new[] { Node(10) }, tasks, Guid.NewGuid());

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(manager.Requests, Has.Count.EqualTo(4));

            foreach (var (_, request) in manager.Requests)
                Assert.That(request.ReturnVariables, Is.EqualTo(new[] { ((IWitFunction)request.Activity).ReturnReference }));

            Assert.That(variables.Select(variable => variable.Name), Is.EqualTo(Enumerable.Range(0, 4).Select(ReturnName)));
        }

        [Test]
        public async Task ATaskBuiltByHandAsksForTheVariableUnderAGridSlotNameAloneTest()
        {
            // The activity names no slot, so the task asks for what it carries under a name Grid
            // gives a slot: the prefix and a number. A name that only starts like one is a variable
            // of the script.
            var manager = new MockAnsweringNodesManager(MockAnsweringNodesManager.Honest);
            var tasks = new[] { MakeHandTask(3, "returnCode", "returned.Item1", "return", "return3x", "return 4", "Return5") };

            await manager.ProcessQueued(new[] { Node(10) }, tasks, Guid.NewGuid());

            Assert.That(manager.Requests.Single().Request.ReturnVariables, Is.EqualTo(new[] { ReturnName(3) }));
        }

        [Test]
        public async Task ATaskWithoutASlotAsksForNothingWhateverItsVariablesAreNamedTest()
        {
            // A transformer that returns nothing has no slot. Up to 1.3.0 a variable of the script
            // named like a return variable was asked for in its place and came back as a result.
            var manager = new MockAnsweringNodesManager(MockAnsweringNodesManager.Honest);
            var tasks = Enumerable.Range(0, 3).Select(index => GridTestTasks.WithoutSlot(index, "returnCode", "returned.Item1")).ToList();
            var group = WitGridTaskAllocator.Allocate(new[] { Node(10) }, tasks).Single();

            var (_, batchVariables) = await manager.Process(group, Guid.NewGuid(), inParallel: false, exactNode: false);
            var (_, queuedVariables) = await manager.ProcessQueued(new[] { Node(10) }, tasks, Guid.NewGuid());

            Assert.That(manager.Requests, Has.Count.EqualTo(6));
            Assert.That(manager.Requests.Select(entry => entry.Request.ReturnVariables), Is.All.Empty);
            Assert.That(batchVariables, Is.Empty);
            Assert.That(queuedVariables, Is.Empty);
        }

        #endregion

        #region Queued As Before Tests

        [TestCase(true)]
        [TestCase(false)]
        public async Task QueuedTasksAnsweredAsAskedComeBackWholeInTheOrderOfTheTasksTest(bool built)
        {
            var manager = new MockAnsweringNodesManager(MockAnsweringNodesManager.Honest);
            var nodes = new[] { Node(10), Node(20) };
            var tasks = Enumerable.Range(0, 7).Select(index => built ? MakeBuiltTask(index) : MakeHandTask(index)).ToList();
            var warnings = new List<string>();

            var (status, variables) = await manager.ProcessQueued(nodes, tasks, Guid.NewGuid(), TEXT, warnings.Add);
            var (statusBefore, variablesBefore) = await manager.ProcessQueued(nodes, tasks, Guid.NewGuid());

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(statusBefore.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(variables.Select(variable => variable.Name), Is.EqualTo(Enumerable.Range(0, 7).Select(ReturnName)));
            Assert.That(variablesBefore.Select(variable => variable.Name), Is.EqualTo(Enumerable.Range(0, 7).Select(ReturnName)));
            Assert.That(variables.Select(variable => variable.Value), Is.All.InstanceOf<string>());
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task QueuedCallsWhoseNodesAnswerAsAskedReturnExactlyWhatTheyAlwaysReturnedTest()
        {
            var random = new Random(20261119);

            for (int round = 0; round < 200; round++)
            {
                int machines = random.Next(1, 6);
                var nodes = Enumerable.Range(0, machines).Select(_ => Node(0.1 + random.NextDouble() * 20)).ToArray();

                int count = random.Next(0, 30);
                bool built = random.NextDouble() < 0.5;
                bool scriptVariables = random.NextDouble() < 0.3;
                string[] carried = scriptVariables ? ["returnCode", "returned.Item1"] : [];

                var tasks = Enumerable.Range(0, count)
                    .Select(index => built ? MakeBuiltTask(index, carried) : MakeHandTask(index, carried))
                    .ToList();

                // A node may return nothing for a task. That was never an error.
                var silent = Enumerable.Range(0, count).Where(_ => random.NextDouble() < 0.2).Select(ReturnName).ToHashSet();

                // What the call returned before the rules: every value of every task, in the order of
                // the tasks.
                var before = new Dictionary<string, object?>();

                var manager = new MockAnsweringNodesManager((nodeId, request) =>
                {
                    var answers = MockAnsweringNodesManager.Honest(nodeId, request);
                    if (answers.Any(answer => silent.Contains(answer.Name)))
                        answers.Clear();

                    foreach (var answer in answers)
                        before[answer.Name] = answer.Value;

                    return answers;
                });

                var warnings = new List<string>();

                var (status, variables) = await manager.ProcessQueued(nodes, tasks, Guid.NewGuid(), TEXT, warnings.Add);

                var expected = Enumerable.Range(0, count).Select(ReturnName).Where(before.ContainsKey).Select(name => before[name]).ToList();

                Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
                Assert.That(before, Has.Count.EqualTo(count - silent.Count), "one value for each task that was answered");
                Assert.That(variables.Select(variable => variable.Value), Is.EqualTo(expected), "the same values in the same order");
                Assert.That(warnings, Is.Empty, "nothing an honest node returns is left out");
            }
        }

        [Test]
        public async Task WithoutARuleANullAndAValueOfAnyTypeAreKeptUnderQueuedAsBeforeTest()
        {
            var manager = new MockAnsweringNodesManager((nodeId, request) =>
            {
                var answers = MockAnsweringNodesManager.Honest(nodeId, request);

                if (answers[0].Name == ReturnName(1))
                    answers[0] = With(answers[0], null);

                if (answers[0].Name == ReturnName(3))
                    answers[0] = With(answers[0], 42);

                return answers;
            });

            var nodes = new[] { Node(10) };
            var tasks = Enumerable.Range(0, 5).Select(index => MakeBuiltTask(index)).ToList();
            var warnings = new List<string>();

            var (_, asBefore) = await manager.ProcessQueued(nodes, tasks, Guid.NewGuid());
            var (_, noRule) = await manager.ProcessQueued(nodes, tasks, Guid.NewGuid(), null, warnings.Add);
            var (_, anyValue) = await manager.ProcessQueued(nodes, tasks, Guid.NewGuid(), GridValueRule.ANY, warnings.Add);

            foreach (var variables in new[] { asBefore, noRule, anyValue })
            {
                Assert.That(variables, Has.Count.EqualTo(5));
                Assert.That(variables[1].Value, Is.Null);
                Assert.That(variables[3].Value, Is.EqualTo(42));
            }

            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task AQueuedTaskThatFailsEndsTheRunAsFailedAsBeforeTest()
        {
            var manager = new MockAnsweringNodesManager((nodeId, request) =>
                request.ReturnVariables.Contains(ReturnName(2)) ? null : MockAnsweringNodesManager.Honest(nodeId, request));

            var tasks = Enumerable.Range(0, 5).Select(index => MakeBuiltTask(index)).ToList();

            var (status, variables) = await manager.ProcessQueued(new[] { Node(10) }, tasks, Guid.NewGuid(), TEXT, null);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Failed));
            Assert.That(status.Message, Is.EqualTo(MockAnsweringNodesManager.FAILURE));
            Assert.That(variables.Select(variable => variable.Name), Is.EqualTo(new[] { ReturnName(0), ReturnName(1) }), "what came before the failure is returned");
            Assert.That(manager.Requests, Has.Count.EqualTo(3), "nothing is asked after a failure");
        }

        [Test]
        public void QueuedProcessingWithoutNodesIsAnErrorAsBeforeTest()
        {
            var manager = new MockAnsweringNodesManager(MockAnsweringNodesManager.Honest);
            var tasks = new[] { MakeBuiltTask(0) };

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.ProcessQueued(Array.Empty<IWitEngineActivityNode>(), tasks, Guid.NewGuid()));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await manager.ProcessQueued(Array.Empty<IWitEngineActivityNode>(), tasks, Guid.NewGuid(), TEXT, null));
        }

        #endregion

        #region Queued Left Out Tests

        [TestCase(true)]
        [TestCase(false)]
        public async Task UnderQueuedASecondValueAndAValueUnderAnotherNameAreLeftOutTest(bool built)
        {
            // Up to 1.3.0 everything a node returned for a queued task was kept: its second value, the
            // slot of another node's task, any name at all.
            var manager = new MockAnsweringNodesManager((nodeId, request) =>
            {
                var answers = MockAnsweringNodesManager.Honest(nodeId, request);
                string own = answers[0].Name;
                string foreign = own == ReturnName(0) ? ReturnName(1) : ReturnName(0);

                answers.Add(new WitVariableObject(own, SURPLUS));
                answers.Add(new WitVariableObject(foreign, SURPLUS));
                answers.Add(new WitVariableObject("anything", SURPLUS));

                return answers;
            });

            var nodes = new[] { Node(10), Node(20) };
            var tasks = Enumerable.Range(0, 4).Select(index => built ? MakeBuiltTask(index) : MakeHandTask(index)).ToList();
            var warnings = new List<string>();

            var (status, variables) = await manager.ProcessQueued(nodes, tasks, Guid.NewGuid(), TEXT, warnings.Add);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), "leaving a value out never fails a task");
            Assert.That(variables.Select(variable => variable.Name), Is.EqualTo(Enumerable.Range(0, 4).Select(ReturnName)), "one value for each task");
            Assert.That(variables.Select(variable => variable.Value), Has.None.EqualTo(SURPLUS));
            Assert.That(warnings, Has.Count.EqualTo(4), "one warning for each task's answer");
            Assert.That(warnings.Count(warning => warning.Contains("3 value(s) its 1 task(s) did not ask for")), Is.EqualTo(4));
        }

        [Test]
        public async Task UnderQueuedTheOldOverloadHoldsAnAnswerToItsNamesAsWellTest()
        {
            var manager = new MockAnsweringNodesManager((nodeId, request) =>
            {
                var answers = MockAnsweringNodesManager.Honest(nodeId, request);
                answers.Add(new WitVariableObject(answers[0].Name, SURPLUS));
                return answers;
            });

            var tasks = Enumerable.Range(0, 3).Select(index => MakeBuiltTask(index)).ToList();

            var (_, variables) = await manager.ProcessQueued(new[] { Node(10) }, tasks, Guid.NewGuid());

            Assert.That(variables, Has.Count.EqualTo(3));
            Assert.That(variables.Select(variable => variable.Value), Has.None.EqualTo(SURPLUS));
        }

        [Test]
        public async Task UnderQueuedAValueTheCollectionWouldRefuseIsLeftOutAndTheRunCompletesTest()
        {
            var manager = new MockAnsweringNodesManager((nodeId, request) =>
            {
                var answers = MockAnsweringNodesManager.Honest(nodeId, request);

                if (answers[0].Name == ReturnName(1))
                    answers[0] = With(answers[0], null);

                if (answers[0].Name == ReturnName(3))
                    answers[0] = With(answers[0], 42);

                return answers;
            });

            var nodes = new[] { Node(10), Node(20) };
            var tasks = Enumerable.Range(0, 5).Select(index => MakeBuiltTask(index)).ToList();
            var warnings = new List<string>();

            var (status, variables) = await manager.ProcessQueued(nodes, tasks, Guid.NewGuid(), TEXT, warnings.Add);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(variables.Select(variable => variable.Name), Is.EqualTo(new[] { ReturnName(0), ReturnName(2), ReturnName(4) }));
            Assert.That(variables.Select(variable => variable.Value), Is.All.InstanceOf<string>());
            Assert.That(manager.Requests, Has.Count.EqualTo(5), "a task whose value was refused is not sent again");
            Assert.That(Refusals(warnings), Has.Count.EqualTo(2));
            Assert.That(warnings, Has.Count.EqualTo(2));
            Assert.That(warnings.Count(warning => warning.Contains("1 null where String was expected")), Is.EqualTo(1));
            Assert.That(warnings.Count(warning => warning.Contains("1 of type Int32 where String was expected")), Is.EqualTo(1));
        }

        [Test]
        public async Task UnderQueuedARefusedValueUsesUpItsSlotTest()
        {
            var manager = new MockAnsweringNodesManager((nodeId, request) =>
            {
                var answers = MockAnsweringNodesManager.Honest(nodeId, request);
                answers.Insert(0, With(answers[0], null));
                return answers;
            });

            var tasks = Enumerable.Range(0, 3).Select(index => MakeBuiltTask(index)).ToList();
            var warnings = new List<string>();

            var (status, variables) = await manager.ProcessQueued(new[] { Node(10) }, tasks, Guid.NewGuid(), TEXT, warnings.Add);

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
            Assert.That(variables, Is.Empty, "each task was answered with a null first: the value that follows is one too many");
            Assert.That(Refusals(warnings), Has.Count.EqualTo(3));
            Assert.That(warnings.Count(warning => warning.Contains("did not ask for")), Is.EqualTo(3));
        }

        #endregion

        #region Queued Node Naming Tests

        [Test]
        public async Task UnderQueuedTheWarningNamesTheNodeTheReturnedStatusNamesTest()
        {
            Guid ran = Guid.NewGuid();
            var planned = Node(10);

            var manager = new MockAnsweringNodesManager((nodeId, request) => MockAnsweringNodesManager.Honest(nodeId, request).Select(answer => With(answer, null)).ToList())
            {
                StatusEngineId = ran
            };

            var warnings = new List<string>();

            await manager.ProcessQueued(new[] { planned }, new[] { MakeBuiltTask(0) }, Guid.NewGuid(), TEXT, warnings.Add);

            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.StartWith($"Node {ran}, as the returned status names it"));
            Assert.That(warnings[0], Does.Contain($"the task was planned for node {planned.NodeId}"));
        }

        [Test]
        public async Task UnderQueuedTheWarningNamesThePlannedNodeWhenTheStatusNamesItTest()
        {
            var planned = Node(10);
            var manager = new MockAnsweringNodesManager((nodeId, request) => MockAnsweringNodesManager.Honest(nodeId, request).Select(answer => With(answer, null)).ToList());
            var warnings = new List<string>();

            await manager.ProcessQueued(new[] { planned }, new[] { MakeBuiltTask(0) }, Guid.NewGuid(), TEXT, warnings.Add);

            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.StartWith($"Node {planned.NodeId} returned 1 value(s)"));
        }

        #endregion
    }
}
