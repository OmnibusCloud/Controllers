using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Controller.Grid.Utils;
using OutWit.Controller.Special.Activities;
using OutWit.Engine.Data.References;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;
using OutWit.Engine.Sdk;

namespace OutWit.Controller.Grid.Tests
{
    /// <summary>
    /// A node answers only what it was asked. A task asks for one value, under its return slot; a value
    /// that comes under a slot the batch's tasks asked for is kept, as many times as they asked for that
    /// name; any other value is left out and the batch stands. A call whose nodes answer as asked
    /// returns exactly what it returned before the rule existed: that half is the larger part of these
    /// tests. The tasks here are built by hand around an activity that is not a function, so a task's
    /// slot is the variable it carries under one of Grid's slot names; what a slot is, and that a
    /// variable of the script is none, has its own tests.
    /// </summary>
    [TestFixture]
    public class GridReassignmentAnswersTests
    {
        #region Constants

        private const string SURPLUS = "surplus";

        #endregion

        #region Tools

        private static WitGridTask MakeTask(int index, int? replicaOf = null, params string[] scriptVariables)
        {
            var variables = new WitVariableCollection();
            variables.Add(new WitVariableObject(ReturnName(index)));
            variables.Add(new WitVariableObject("item", index));

            foreach (string name in scriptVariables)
                variables.Add(new WitVariableObject(name, $"script:{name}"));

            return new WitGridTask
            {
                Work = 1,
                ReplicaOf = replicaOf,
                Variables = variables,
                Activity = new WitActivitySpecialTrace { Message = (WitReference)"obj" }
            };
        }

        private static string ReturnName(int index)
        {
            return $"return{index}";
        }

        private static IWitEngineActivityNode Node(double rate)
        {
            return new MockActivityNode(WitEngineNodeSdk.Instance, rate, Guid.NewGuid(), iterations: 1);
        }

        /// <summary>
        /// What a node returns, as the engine's node does it: for each task, the variables of its pool
        /// whose names the task asked for, in the pool's order. A task asks for its slot and for nothing
        /// else it carries.
        /// </summary>
        private static List<IWitVariable> Honest(WitGridTaskGroup group)
        {
            var answers = new List<IWitVariable>();

            foreach (var task in group)
            {
                foreach (var variable in task.Variables.Where(variable => GridTestTasks.IsSlot(task, variable.Name)))
                    answers.Add(new WitVariableObject(variable.Name, variable.Value ?? Answer(group, variable.Name)));
            }

            return answers;
        }

        private static string Answer(WitGridTaskGroup group, string name)
        {
            return $"{group.Node.NodeId:N}:{name}";
        }

        private static IWitVariable Surplus(string name)
        {
            return new WitVariableObject(name, SURPLUS);
        }

        private static GridReassignment.GroupProcessResult Completed(IReadOnlyList<IWitVariable> variables)
        {
            return new GridReassignment.GroupProcessResult(true, null, variables);
        }

        private static GridReassignment.GroupProcessResult Failed()
        {
            var status = new WitProcessingStatus(Guid.Empty, Guid.NewGuid()).Failed(TimeSpan.Zero, "node failed");

            return new GridReassignment.GroupProcessResult(false, status, Array.Empty<IWitVariable>());
        }

        private static Task<GridReassignment.DistributionOutcome> RunAsync(
            IReadOnlyList<IWitEngineActivityNode> nodes, IReadOnlyList<WitGridTask> tasks, Func<WitGridTaskGroup, IReadOnlyList<IWitVariable>> answers, List<string>? warnings = null)
        {
            return GridReassignment.DistributeAsync(
                nodes,
                tasks,
                (group, _) => Task.FromResult(Completed(answers(group))),
                warnings == null ? null : warnings.Add,
                CancellationToken.None);
        }

        private static List<string> SlotAnswers(GridReassignment.DistributionOutcome outcome)
        {
            return outcome.Results.OfType<string>().Where(value => value.Contains(":return")).ToList();
        }

        private static List<string> Slots(GridReassignment.DistributionOutcome outcome)
        {
            return SlotAnswers(outcome).Select(value => value.Split(':')[1]).ToList();
        }

        private static List<string> Makers(GridReassignment.DistributionOutcome outcome)
        {
            return SlotAnswers(outcome).Select(value => value.Split(':')[0]).Distinct().ToList();
        }

        #endregion

        #region As Before Tests

        [Test]
        public async Task ABatchAnsweredAsAskedIsReturnedWholeTest()
        {
            var nodes = new[] { Node(10), Node(20) };
            var tasks = Enumerable.Range(0, 6).Select(index => MakeTask(index)).ToList();
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, tasks, Honest, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(6));
            Assert.That(Slots(outcome).OrderBy(name => name, StringComparer.Ordinal), Is.EqualTo(Enumerable.Range(0, 6).Select(ReturnName)));
            Assert.That(Makers(outcome), Has.Count.EqualTo(2));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task ABatchReturningFewerValuesThanTasksIsReturnedAsItIsTest()
        {
            var nodes = new[] { Node(10) };
            var tasks = Enumerable.Range(0, 4).Select(index => MakeTask(index)).ToList();
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, tasks, group => Honest(group).Take(3).ToList(), warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(3));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task ABatchWithNoReturnVariablesAndNoValuesIsReturnedEmptyTest()
        {
            var nodes = new[] { Node(10) };
            var tasks = Enumerable.Range(0, 3).Select(_ => new WitGridTask
            {
                Work = 1,
                Variables = new WitVariableCollection(),
                Activity = new WitActivitySpecialTrace { Message = (WitReference)"obj" }
            }).ToList();

            var outcome = await RunAsync(nodes, tasks, _ => Array.Empty<IWitVariable>());

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Is.Empty);
        }

        [Test]
        public async Task ATaskThatCarriesAScriptVariableNamedLikeAReturnVariableIsAnsweredWithItsSlotAloneTest()
        {
            // A script's own variable named "returnCode" is carried by every task that refers to it.
            // Up to 1.3.0 it was asked of the node with the task's slot and came back once per task,
            // as one more value of every item: ten values for five tasks. A task asks for its slot.
            var nodes = new[] { Node(10) };
            var tasks = Enumerable.Range(0, 5).Select(index => MakeTask(index, null, "returnCode")).ToList();
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, tasks, Honest, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(5), "five tasks, five values");
            Assert.That(outcome.Results, Has.None.EqualTo("script:returnCode"));
            Assert.That(Slots(outcome).OrderBy(name => name, StringComparer.Ordinal), Is.EqualTo(Enumerable.Range(0, 5).Select(ReturnName)));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task TheValuesKeepTheOrderTheNodeReturnedThemInTest()
        {
            var nodes = new[] { Node(10) };
            var tasks = Enumerable.Range(0, 8).Select(index => MakeTask(index)).ToList();
            List<object?> sent = [];

            var outcome = await RunAsync(nodes, tasks, group =>
            {
                var answers = Honest(group);
                answers.Reverse();
                sent = answers.Select(answer => answer.Value).ToList();
                return answers;
            });

            Assert.That(outcome.Results, Is.EqualTo(sent));
        }

        [Test]
        public async Task CallsWhoseNodesAnswerAsAskedReturnExactlyWhatTheyAlwaysReturnedTest()
        {
            var random = new Random(20261117);

            for (int round = 0; round < 300; round++)
            {
                int machines = random.Next(1, 7);
                var nodes = Enumerable.Range(0, machines).Select(_ => Node(0.1 + random.NextDouble() * 20)).ToArray();
                var failing = nodes.Where(_ => random.NextDouble() < 0.25).Select(node => node.NodeId).ToHashSet();
                bool scriptVariable = random.NextDouble() < 0.3;
                bool copies = machines > 1 && random.NextDouble() < 0.4;

                int count = random.Next(1, 40);
                var tasks = Enumerable.Range(0, count)
                    .Select(index => MakeTask(index, copies ? index / 2 : null, scriptVariable ? ["returnCode", "returned.Item1"] : []))
                    .ToList();

                // What the call returned before the rule: every value of every batch that completed,
                // in the order the batches were taken. A task that carries script variables named like
                // return variables is asked for its slot alone, so an honest node returns one value.
                var before = new List<object?>();
                var warnings = new List<string>();

                var outcome = await GridReassignment.DistributeAsync(
                    nodes,
                    tasks,
                    (group, _) =>
                    {
                        if (failing.Contains(group.Node.NodeId))
                            return Task.FromResult(Failed());

                        var answers = Honest(group);
                        if (random.NextDouble() < 0.2)
                            answers.RemoveAt(random.Next(answers.Count));

                        before.AddRange(answers.Select(answer => answer.Value));
                        return Task.FromResult(Completed(answers));
                    },
                    warnings.Add,
                    CancellationToken.None);

                Assert.That(outcome.Results, Is.EqualTo(before), "the same values in the same order");
                Assert.That(warnings.Where(warning => warning.Contains("did not ask for")), Is.Empty, "nothing an honest node returns is left out");
            }
        }

        #endregion

        #region Left Out Tests

        [Test]
        public async Task ASecondValueForOneTaskIsLeftOutAndTheBatchStandsTest()
        {
            var liar = Node(20);
            var honest = Node(10);
            var tasks = Enumerable.Range(0, 6).Select(index => MakeTask(index)).ToList();
            var warnings = new List<string>();

            var outcome = await RunAsync(new[] { liar, honest }, tasks, group =>
            {
                var answers = Honest(group);
                if (group.Node.NodeId == liar.NodeId)
                    answers.Add(Surplus(answers[0].Name));

                return answers;
            }, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(6), "one value for each task, however many the node sent");
            Assert.That(outcome.Results, Has.None.EqualTo(SURPLUS));
            Assert.That(Makers(outcome), Has.Count.EqualTo(2), "the node's batch stands: no task was run again");
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("1 value(s)"));
        }

        [Test]
        public async Task AValueThatComesFirstUnderANameWinsItsPlaceTest()
        {
            var nodes = new[] { Node(10) };
            var tasks = Enumerable.Range(0, 3).Select(index => MakeTask(index)).ToList();

            var outcome = await RunAsync(nodes, tasks, group =>
            {
                var answers = Honest(group);
                answers.Insert(0, Surplus(answers[0].Name));
                return answers;
            });

            Assert.That(outcome.Results, Has.Count.EqualTo(3), "three tasks, three values");
            Assert.That(outcome.Results.Count(value => Equals(value, SURPLUS)), Is.EqualTo(1), "which of two values of one name is the node's own business; their number is not");
        }

        [Test]
        public async Task ATaskAnsweredTwiceInPlaceOfAnotherKeepsOneValueTest()
        {
            var nodes = new[] { Node(10) };
            var tasks = Enumerable.Range(0, 4).Select(index => MakeTask(index)).ToList();
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, tasks, group =>
            {
                var answers = Honest(group);
                answers[1] = Surplus(answers[0].Name);
                return answers;
            }, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(3), "the task left unanswered has no value, and the other has one");
            Assert.That(outcome.Results, Has.None.EqualTo(SURPLUS));
            Assert.That(warnings, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task AValueUnderANameTheBatchWasNotGivenIsLeftOutTest()
        {
            var liar = Node(20);
            var honest = Node(10);
            var tasks = Enumerable.Range(0, 6).Select(index => MakeTask(index)).ToList();

            var outcome = await RunAsync(new[] { liar, honest }, tasks, group =>
            {
                var answers = Honest(group);
                if (group.Node.NodeId == liar.NodeId)
                {
                    // The slot of a task the other node holds.
                    string foreign = Enumerable.Range(0, 6).Select(ReturnName).First(name => answers.All(answer => answer.Name != name));
                    answers.Add(Surplus(foreign));
                    answers.Add(Surplus("anything"));
                }

                return answers;
            });

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(6));
            Assert.That(outcome.Results, Has.None.EqualTo(SURPLUS));
        }

        [Test]
        public async Task AValueWithNoVariableIsLeftOutTest()
        {
            var nodes = new[] { Node(10) };
            var tasks = Enumerable.Range(0, 4).Select(index => MakeTask(index)).ToList();

            var outcome = await RunAsync(nodes, tasks, group =>
            {
                var answers = Honest(group);
                answers.Insert(2, null!);
                return answers;
            });

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(4));
        }

        [Test]
        public async Task AValueUnderAScriptVariableIsLeftOutHoweverManyTasksCarryItTest()
        {
            // Up to 1.3.0 the variable was good for as many values as tasks carried it: three slots and
            // the variable three times. It is not a slot, so no value under it is kept.
            var nodes = new[] { Node(10) };
            var tasks = Enumerable.Range(0, 3).Select(index => MakeTask(index, null, "returnCode")).ToList();
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, tasks, group =>
            {
                var answers = Honest(group);
                answers.Add(Surplus("returnCode"));
                answers.Add(Surplus("returnCode"));
                return answers;
            }, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(3), "three tasks, three values");
            Assert.That(outcome.Results, Has.None.EqualTo(SURPLUS));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("2 value(s)"));
        }

        [Test]
        public async Task WhenEveryNodeReturnsMoreThanItWasAskedTheJobStillCompletesTest()
        {
            var nodes = new[] { Node(10), Node(20) };
            var tasks = Enumerable.Range(0, 4).Select(index => MakeTask(index)).ToList();

            var outcome = await RunAsync(nodes, tasks, group =>
            {
                var answers = Honest(group);
                answers.Add(Surplus(answers[0].Name));
                return answers;
            });

            Assert.That(outcome.Failed, Is.False, "the rule leaves values out; it never fails a batch");
            Assert.That(outcome.Results, Has.Count.EqualTo(4));
        }

        #endregion

        #region Copies Tests

        [Test]
        public async Task ACopyAnsweredTwiceCountsOnceTest()
        {
            var liar = Node(100);
            var other = Node(50);

            var outcome = await RunAsync(
                new[] { liar, other },
                new[] { MakeTask(0, replicaOf: 0), MakeTask(1, replicaOf: 0) },
                group =>
                {
                    var answers = Honest(group);
                    if (group.Node.NodeId == liar.NodeId)
                        answers.Add(Surplus(answers[0].Name));

                    return answers;
                });

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(2), "two copies, two values: the node's second answer made no third");
            Assert.That(Makers(outcome), Has.Count.EqualTo(2), "one value from each machine");
        }

        [Test]
        public async Task NoMachineHasMoreValuesInACallThanItsTasksAskedForTest()
        {
            var random = new Random(1117);

            for (int round = 0; round < 300; round++)
            {
                int machines = random.Next(2, 7);
                var nodes = Enumerable.Range(0, machines).Select(_ => Node(0.1 + random.NextDouble() * 20)).ToArray();
                var liars = nodes.Where(_ => random.NextDouble() < 0.5).Select(node => node.NodeId).ToHashSet();

                int count = random.Next(1, 30);
                var tasks = Enumerable.Range(0, count).Select(index => MakeTask(index, index / 3)).ToList();

                var held = new Dictionary<string, int>();

                var outcome = await RunAsync(nodes, tasks, group =>
                {
                    string maker = group.Node.NodeId.ToString("N");
                    held[maker] = held.GetValueOrDefault(maker) + group.Count;

                    var answers = Honest(group);
                    if (!liars.Contains(group.Node.NodeId))
                        return answers;

                    // Every answer is this node's own and says so, whatever name it comes under.
                    int extra = random.Next(1, 6);
                    for (int index = 0; index < extra; index++)
                    {
                        string name = random.NextDouble() < 0.7 ? answers[random.Next(answers.Count)].Name : ReturnName(random.Next(count + 3));
                        answers.Insert(random.Next(answers.Count + 1), new WitVariableObject(name, Answer(group, name)));
                    }

                    return answers;
                });

                // With fewer machines than copies the platform runs fewer copies: count the tasks that ran.
                int ran = held.Values.Sum();

                Assert.That(outcome.Failed, Is.False);
                Assert.That(outcome.Results, Has.Count.EqualTo(ran), "one value for each task that ran");

                foreach (var machine in SlotAnswers(outcome).GroupBy(value => value.Split(':')[0]))
                    Assert.That(machine.Count(), Is.LessThanOrEqualTo(held[machine.Key]), "a machine with more values than tasks");

                Assert.That(Slots(outcome).Distinct().Count(), Is.EqualTo(ran), "no slot answered twice");
            }
        }

        #endregion
    }
}
