using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Controller.Grid.Utils;
using OutWit.Engine.Data.Status;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests
{
    /// <summary>
    /// A value the return collection would refuse is an answer not given. A typed collection takes a
    /// list whole or not at all, so one null or one value of another type under a slot a node was asked
    /// for would cost the call every value of every node. Such a value is left out with a warning, the
    /// batch stands, and its task is not sent again. A call whose nodes return values the collection
    /// takes returns exactly what it returned before, and so does every call without a typed collection.
    /// The tasks here are built as Grid builds them: the slot is the return reference of the activity.
    /// </summary>
    [TestFixture]
    public class GridReassignmentValuesTests
    {
        #region Constants

        private static readonly GridValueRule TEXT = GridValueRule.ForElementType(typeof(string));

        #endregion

        #region Tools

        private static WitGridTask MakeTask(int index, int? replicaOf = null)
        {
            return GridTestTasks.Built(index, replicaOf);
        }

        private static List<WitGridTask> MakeTasks(int count)
        {
            return Enumerable.Range(0, count).Select(index => MakeTask(index)).ToList();
        }

        private static string Slot(WitGridTask task)
        {
            return ((IWitFunction)task.Activity).ReturnReference ?? string.Empty;
        }

        private static IWitEngineActivityNode Node(double rate)
        {
            return GridTestTasks.Node(rate);
        }

        /// <summary>
        /// What an honest node returns: for each task one text under the task's slot, which says who
        /// made it.
        /// </summary>
        private static List<IWitVariable> Honest(WitGridTaskGroup group)
        {
            return group
                .Select(task => (IWitVariable)new WitVariableObject(Slot(task), $"{group.Node.NodeId:N}:{Slot(task)}"))
                .ToList();
        }

        /// <summary>
        /// The same answer with another value: the name stays, so the value comes under a slot the
        /// node was asked for.
        /// </summary>
        private static IWitVariable With(IWitVariable answer, object? value)
        {
            return new WitVariableObject(answer.Name, value);
        }

        private static GridReassignment.GroupProcessResult Completed(IReadOnlyList<IWitVariable> variables, IWitProcessingStatus? status = null)
        {
            return new GridReassignment.GroupProcessResult(true, status, variables);
        }

        private static GridReassignment.GroupProcessResult Failed()
        {
            var status = new WitProcessingStatus(Guid.Empty, Guid.NewGuid()).Failed(TimeSpan.Zero, "node failed");

            return new GridReassignment.GroupProcessResult(false, status, Array.Empty<IWitVariable>());
        }

        private static Task<GridReassignment.DistributionOutcome> RunAsync(
            IReadOnlyList<IWitEngineActivityNode> nodes, IReadOnlyList<WitGridTask> tasks,
            Func<WitGridTaskGroup, IReadOnlyList<IWitVariable>> answers, GridValueRule? values, List<string>? warnings = null)
        {
            return GridReassignment.DistributeAsync(
                nodes,
                tasks,
                (group, _) => Task.FromResult(Completed(answers(group))),
                warnings == null ? null : warnings.Add,
                values,
                CancellationToken.None);
        }

        private static List<string> Makers(GridReassignment.DistributionOutcome outcome)
        {
            return outcome.Results.OfType<string>().Select(value => value.Split(':')[0]).Distinct().ToList();
        }

        private static List<string> Refusals(List<string> warnings)
        {
            return warnings.Where(warning => warning.Contains("would refuse")).ToList();
        }

        #endregion

        #region As Before Tests

        [Test]
        public async Task ACallWhoseNodesReturnValuesTheCollectionTakesIsReturnedWholeTest()
        {
            var nodes = new[] { Node(10), Node(20) };
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, MakeTasks(6), Honest, TEXT, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(6));
            Assert.That(outcome.Results, Is.All.InstanceOf<string>());
            Assert.That(Makers(outcome), Has.Count.EqualTo(2));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task WithoutARuleANullAndAValueOfAnyTypeAreKeptAsBeforeTest()
        {
            var nodes = new[] { Node(10) };
            var tasks = MakeTasks(4);
            var warnings = new List<string>();

            IReadOnlyList<IWitVariable> Answers(WitGridTaskGroup group)
            {
                var answers = Honest(group);
                answers[0] = With(answers[0], null);
                answers[1] = With(answers[1], 42);
                return answers;
            }

            var noRule = await RunAsync(nodes, tasks, Answers, null, warnings);
            var anyValue = await RunAsync(nodes, tasks, Answers, GridValueRule.ANY, warnings);
            var objects = await RunAsync(nodes, tasks, Answers, GridValueRule.ForElementType(typeof(object)), warnings);
            var asBefore = await GridReassignment.DistributeAsync(
                nodes,
                tasks,
                (group, _) => Task.FromResult(Completed(Answers(group))),
                warnings.Add,
                CancellationToken.None);

            foreach (var outcome in new[] { noRule, anyValue, objects, asBefore })
            {
                Assert.That(outcome.Failed, Is.False);
                Assert.That(outcome.Results, Has.Count.EqualTo(4));
                Assert.That(outcome.Results[0], Is.Null);
                Assert.That(outcome.Results[1], Is.EqualTo(42));
            }

            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public async Task CallsWhoseNodesAnswerAsAskedReturnTheSameWithTheRuleAsTheyAlwaysDidTest()
        {
            var random = new Random(20261119);

            for (int round = 0; round < 300; round++)
            {
                int machines = random.Next(1, 7);
                var nodes = Enumerable.Range(0, machines).Select(_ => Node(0.1 + random.NextDouble() * 20)).ToArray();
                var failing = nodes.Where(_ => random.NextDouble() < 0.25).Select(node => node.NodeId).ToHashSet();
                bool copies = machines > 1 && random.NextDouble() < 0.4;

                int count = random.Next(1, 40);
                var tasks = Enumerable.Range(0, count).Select(index => MakeTask(index, copies ? index / 2 : null)).ToList();

                // What the call returned before the rule: every value of every batch that completed,
                // in the order the batches were taken.
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
                    TEXT,
                    CancellationToken.None);

                Assert.That(outcome.Results, Is.EqualTo(before), "the same values in the same order");
                Assert.That(Refusals(warnings), Is.Empty, "nothing an honest node returns is refused");
                Assert.That(warnings.Where(warning => warning.Contains("did not ask for")), Is.Empty, "nothing an honest node returns is left out");
            }
        }

        #endregion

        #region Left Out Tests

        [Test]
        public async Task ANullUnderAnAskedSlotIsLeftOutAndTheBatchStandsTest()
        {
            var liar = Node(20);
            var honest = Node(10);
            var warnings = new List<string>();

            var outcome = await RunAsync(new[] { liar, honest }, MakeTasks(6), group =>
            {
                var answers = Honest(group);
                if (group.Node.NodeId == liar.NodeId)
                    answers[0] = With(answers[0], null);

                return answers;
            }, TEXT, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(5), "one value is left out, the five others are the call's results");
            Assert.That(outcome.Results, Is.All.InstanceOf<string>());
            Assert.That(Makers(outcome), Has.Count.EqualTo(2), "the node's batch stands: its other values are kept");
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("1 value(s) the return collection would refuse"));
            Assert.That(warnings[0], Does.Contain("1 null where String was expected"));
            Assert.That(warnings[0], Does.Contain(liar.NodeId.ToString()));
        }

        [Test]
        public async Task AValueOfAnotherTypeUnderAnAskedSlotIsLeftOutAndTheBatchStandsTest()
        {
            var liar = Node(20);
            var honest = Node(10);
            var warnings = new List<string>();

            var outcome = await RunAsync(new[] { liar, honest }, MakeTasks(6), group =>
            {
                var answers = Honest(group);
                if (group.Node.NodeId == liar.NodeId)
                    answers[0] = With(answers[0], 42);

                return answers;
            }, TEXT, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(5));
            Assert.That(outcome.Results, Has.None.EqualTo(42));
            Assert.That(Makers(outcome), Has.Count.EqualTo(2));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("1 of type Int32 where String was expected"));
        }

        [Test]
        public async Task TheWarningSaysHowManyValuesWereRefusedAndWhyTest()
        {
            var nodes = new[] { Node(10) };
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, MakeTasks(6), group =>
            {
                var answers = Honest(group);
                answers[0] = With(answers[0], null);
                answers[2] = With(answers[2], 42);
                answers[3] = With(answers[3], null);
                answers[5] = With(answers[5], 4.5);
                return answers;
            }, TEXT, warnings);

            Assert.That(outcome.Results, Has.Count.EqualTo(2));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("4 value(s) the return collection would refuse"));
            Assert.That(warnings[0], Does.Contain("2 null, 1 of type Int32, 1 of type Double where String was expected"));
            Assert.That(warnings[0], Does.Contain("count as not answered"));
        }

        [Test]
        public async Task TheKeptValuesKeepTheOrderTheNodeReturnedThemInTest()
        {
            var nodes = new[] { Node(10) };
            List<object?> expected = [];

            var outcome = await RunAsync(nodes, MakeTasks(8), group =>
            {
                var answers = Honest(group);
                answers.Reverse();
                answers[1] = With(answers[1], null);
                answers[6] = With(answers[6], 42);
                expected = answers.Select(answer => answer.Value).Where(value => value is string).ToList();
                return answers;
            }, TEXT);

            Assert.That(outcome.Results, Has.Count.EqualTo(6));
            Assert.That(outcome.Results, Is.EqualTo(expected));
        }

        [Test]
        public async Task ARefusedValueUsesUpItsSlotTest()
        {
            // The first value under a slot is the task's answer. A node that sends a null and then a
            // value under the same slot has answered with the null: the second value is one too many.
            var nodes = new[] { Node(10) };
            var warnings = new List<string>();
            string refusedSlot = string.Empty;

            var outcome = await RunAsync(nodes, MakeTasks(3), group =>
            {
                var answers = Honest(group);
                refusedSlot = answers[0].Name;
                answers.Insert(0, With(answers[0], null));
                return answers;
            }, TEXT, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(2), "the task answered with a null has no value");
            Assert.That(outcome.Results.OfType<string>().Any(value => value.EndsWith($":{refusedSlot}")), Is.False);
            Assert.That(warnings, Has.Count.EqualTo(2));
            Assert.That(warnings.Count(warning => warning.Contains("1 value(s) its 3 task(s) did not ask for")), Is.EqualTo(1));
            Assert.That(Refusals(warnings), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task WhenEveryNodeReturnsOnlyValuesTheCollectionRefusesTheCallIsEmptyAndNotFailedTest()
        {
            var nodes = new[] { Node(10), Node(20) };
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, MakeTasks(5), group => Honest(group).Select(answer => With(answer, null)).ToList(), TEXT, warnings);

            Assert.That(outcome.Failed, Is.False, "the rule leaves values out; it never fails a batch");
            Assert.That(outcome.Results, Is.Empty);
            Assert.That(Refusals(warnings), Has.Count.EqualTo(2), "one warning for each node's batch");
        }

        [Test]
        public async Task ATaskWhoseValueWasRefusedIsNotSentAgainTest()
        {
            var liar = Node(20);
            var honest = Node(10);
            var tasks = MakeTasks(6);
            var ran = new List<string>();

            var outcome = await RunAsync(new[] { liar, honest }, tasks, group =>
            {
                ran.AddRange(group.Select(Slot));

                return group.Node.NodeId == liar.NodeId
                    ? Honest(group).Select(answer => With(answer, null)).ToList()
                    : Honest(group);
            }, TEXT);

            Assert.That(ran, Has.Count.EqualTo(6), "every task ran once");
            Assert.That(ran, Is.Unique);
            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(2), "the results come back short and the caller decides");
            Assert.That(Makers(outcome), Is.EqualTo(new[] { honest.NodeId.ToString("N") }));
        }

        #endregion

        #region Copies Tests

        [Test]
        public async Task ACopyRefusedOnOneMachineLeavesTheValueOfTheOtherTest()
        {
            var liar = Node(100);
            var other = Node(50);

            var outcome = await RunAsync(
                new[] { liar, other },
                new[] { MakeTask(0, replicaOf: 0), MakeTask(1, replicaOf: 0) },
                group => group.Node.NodeId == liar.NodeId
                    ? Honest(group).Select(answer => With(answer, 42)).ToList()
                    : Honest(group),
                TEXT);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(1), "two copies, one value the collection takes");
            Assert.That(Makers(outcome), Is.EqualTo(new[] { other.NodeId.ToString("N") }));
        }

        [Test]
        public async Task AMachineWhoseCopyWasRefusedStillHoldsTheItemTest()
        {
            // One machine answers its copy with a null, the other fails. The copy of the failed machine
            // has only the first machine left, and that one has run the item already: it gets no second
            // copy of it, whatever became of its first answer.
            var liar = Node(100);
            var failing = Node(50);
            var ran = new List<Guid>();
            var warnings = new List<string>();

            var outcome = await GridReassignment.DistributeAsync(
                new[] { liar, failing },
                new[] { MakeTask(0, replicaOf: 0), MakeTask(1, replicaOf: 0) },
                (group, _) =>
                {
                    ran.Add(group.Node.NodeId);

                    return Task.FromResult(group.Node.NodeId == failing.NodeId
                        ? Failed()
                        : Completed(Honest(group).Select(answer => With(answer, null)).ToList()));
                },
                warnings.Add,
                TEXT,
                CancellationToken.None);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Is.Empty, "the one answer was refused, and the other copy has no machine");
            Assert.That(ran.Count(id => id == liar.NodeId), Is.EqualTo(1), "the machine that has run the item gets no other copy of it");
            Assert.That(warnings.Count(warning => warning.Contains("was dropped")), Is.EqualTo(1));
            Assert.That(Refusals(warnings), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task NoRefusedValueReachesTheResultsAndEveryOtherValueDoesTest()
        {
            var random = new Random(1119);

            for (int round = 0; round < 300; round++)
            {
                int machines = random.Next(2, 7);
                var nodes = Enumerable.Range(0, machines).Select(_ => Node(0.1 + random.NextDouble() * 20)).ToArray();
                var liars = nodes.Where(_ => random.NextDouble() < 0.5).Select(node => node.NodeId).ToHashSet();

                int count = random.Next(1, 30);
                var tasks = Enumerable.Range(0, count).Select(index => MakeTask(index, index / 3)).ToList();

                var expected = new List<object?>();
                int refusedBatches = 0;
                var warnings = new List<string>();

                var outcome = await RunAsync(nodes, tasks, group =>
                {
                    var answers = Honest(group);

                    if (liars.Contains(group.Node.NodeId))
                    {
                        for (int index = 0; index < answers.Count; index++)
                        {
                            double dice = random.NextDouble();
                            if (dice < 0.3)
                                answers[index] = With(answers[index], null);
                            else if (dice < 0.6)
                                answers[index] = With(answers[index], random.Next());
                        }
                    }

                    if (answers.Any(answer => answer.Value is not string))
                        refusedBatches++;

                    expected.AddRange(answers.Select(answer => answer.Value).Where(value => value is string));
                    return answers;
                }, TEXT, warnings);

                Assert.That(outcome.Failed, Is.False);
                Assert.That(outcome.Results, Is.EqualTo(expected), "every value the collection takes, in the order it came, and no other");
                Assert.That(Refusals(warnings), Has.Count.EqualTo(refusedBatches), "one warning for each batch that had a value refused");
            }
        }

        #endregion

        #region Node Naming Tests

        [Test]
        public async Task TheWarningNamesTheNodeTheReturnedStatusNamesTest()
        {
            // Without copies the server may give a batch to another node than the planned one. The
            // status that comes back carries the id of the node that completed it.
            var planned = Node(10);
            Guid ran = Guid.NewGuid();
            var warnings = new List<string>();

            await GridReassignment.DistributeAsync(
                new[] { planned },
                MakeTasks(3),
                (group, _) =>
                {
                    var answers = Honest(group);
                    answers[0] = With(answers[0], null);
                    answers.Add(new WitVariableObject("anything", "surplus"));

                    var status = new WitProcessingStatus(ran, Guid.NewGuid()).Completed(TimeSpan.Zero);

                    return Task.FromResult(Completed(answers, status));
                },
                warnings.Add,
                TEXT,
                CancellationToken.None);

            Assert.That(warnings, Has.Count.EqualTo(2), "one for the value nobody asked for, one for the value refused");

            foreach (string warning in warnings)
            {
                Assert.That(warning, Does.StartWith($"Node {ran}, as the returned status names it"));
                Assert.That(warning, Does.Contain($"the batch was planned for node {planned.NodeId}"));
            }
        }

        [Test]
        public async Task TheWarningNamesThePlannedNodeWhenTheStatusNamesItTest()
        {
            var planned = Node(10);
            var warnings = new List<string>();

            await GridReassignment.DistributeAsync(
                new[] { planned },
                MakeTasks(2),
                (group, _) =>
                {
                    var answers = Honest(group);
                    answers[0] = With(answers[0], null);

                    var status = new WitProcessingStatus(group.Node.NodeId, Guid.NewGuid()).Completed(TimeSpan.Zero);

                    return Task.FromResult(Completed(answers, status));
                },
                warnings.Add,
                TEXT,
                CancellationToken.None);

            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.StartWith($"Node {planned.NodeId} returned 1 value(s)"));
        }

        [Test]
        public async Task TheWarningSaysTheNodeIsThePlannedOneWhenTheStatusNamesNoneTest()
        {
            var planned = Node(10);
            var warnings = new List<string>();

            foreach (IWitProcessingStatus? status in new IWitProcessingStatus?[] { null, new WitProcessingStatus(Guid.Empty, Guid.NewGuid()).Completed(TimeSpan.Zero) })
            {
                await GridReassignment.DistributeAsync(
                    new[] { planned },
                    MakeTasks(2),
                    (group, _) =>
                    {
                        var answers = Honest(group);
                        answers[0] = With(answers[0], null);

                        return Task.FromResult(Completed(answers, status));
                    },
                    warnings.Add,
                    TEXT,
                    CancellationToken.None);
            }

            Assert.That(warnings, Has.Count.EqualTo(2));

            foreach (string warning in warnings)
                Assert.That(warning, Does.StartWith($"The node that ran the batch planned for node {planned.NodeId} returned 1 value(s)"));
        }

        #endregion
    }
}
