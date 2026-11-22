using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OutWit.Controller.Grid.Model;
using OutWit.Controller.Grid.Tests.Mock;
using OutWit.Controller.Grid.Utils;
using OutWit.Engine.Data.Variables;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.Grid.Tests
{
    /// <summary>
    /// What a slot is. A task asks its node for one value, under the return reference Grid set on the
    /// task's own activity. Up to 1.3.0 every variable a task carried whose name started with "return"
    /// passed for a return variable: a script or iteration variable named so was asked for with every
    /// task and was good for one more value of every task in the results. A variable of the script is
    /// not a slot, whatever its name. Every test runs for a task as Grid builds it and for a task built
    /// by hand around an activity that names no slot.
    /// </summary>
    [TestFixture]
    public class GridReassignmentSlotsTests
    {
        #region Constants

        private const string SURPLUS = "surplus";

        private const string SCRIPT_VALUE = "script:returnCode";

        #endregion

        #region Tools

        /// <summary>
        /// What a node returns, as the engine's node does it: the variables of each task's pool under
        /// the names the task asked for.
        /// </summary>
        private static List<IWitVariable> Honest(WitGridTaskGroup group)
        {
            return AnswersOf(group, GridTestTasks.IsSlot);
        }

        /// <summary>
        /// What a node returned when it was asked as up to 1.3.0: the slot, and every variable of the
        /// script whose name starts like one.
        /// </summary>
        private static List<IWitVariable> AskedAsUpTo130(WitGridTaskGroup group)
        {
            return AnswersOf(group, GridTestTasks.WasAskedUpTo130);
        }

        private static List<IWitVariable> AnswersOf(WitGridTaskGroup group, Func<WitGridTask, string, bool> asked)
        {
            var answers = new List<IWitVariable>();

            foreach (var task in group)
            {
                foreach (var variable in task.Variables.Where(variable => asked(task, variable.Name)))
                    answers.Add(new WitVariableObject(variable.Name, variable.Value ?? Answer(group, variable.Name)));
            }

            return answers;
        }

        private static string Answer(WitGridTaskGroup group, string name)
        {
            return $"{group.Node.NodeId:N}:{name}";
        }

        private static Task<GridReassignment.DistributionOutcome> RunAsync(
            IReadOnlyList<IWitEngineActivityNode> nodes, IReadOnlyList<WitGridTask> tasks,
            Func<WitGridTaskGroup, IReadOnlyList<IWitVariable>> answers, List<string>? warnings = null)
        {
            return GridReassignment.DistributeAsync(
                nodes,
                tasks,
                (group, _) => Task.FromResult(new GridReassignment.GroupProcessResult(true, null, answers(group))),
                warnings == null ? null : warnings.Add,
                CancellationToken.None);
        }

        private static List<string> SlotAnswers(GridReassignment.DistributionOutcome outcome)
        {
            return outcome.Results.OfType<string>().Where(value => value.Contains(":return")).ToList();
        }

        #endregion

        #region Asked Tests

        [TestCase(true)]
        [TestCase(false)]
        public async Task ATaskThatCarriesScriptVariablesNamedLikeReturnVariablesIsAnsweredWithItsSlotAloneTest(bool built)
        {
            var nodes = new[] { GridTestTasks.Node(10), GridTestTasks.Node(20) };
            var tasks = Enumerable.Range(0, 6).Select(index => GridTestTasks.Either(built, index, null, "returnCode", "returned.Item1")).ToList();
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, tasks, Honest, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(6), "six tasks, six values");
            Assert.That(SlotAnswers(outcome).Select(value => value.Split(':')[1]).OrderBy(name => name, StringComparer.Ordinal),
                Is.EqualTo(Enumerable.Range(0, 6).Select(GridTestTasks.ReturnName)));
            Assert.That(warnings, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ANodeThatAnswersAsItWasAskedUpTo130HasTheScriptVariableLeftOutTest(bool built)
        {
            // Five slots and the script's variable five times was the result of this call up to 1.3.0.
            var nodes = new[] { GridTestTasks.Node(10) };
            var tasks = Enumerable.Range(0, 5).Select(index => GridTestTasks.Either(built, index, null, "returnCode")).ToList();
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, tasks, AskedAsUpTo130, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(5), "five slots: the script's variable is no value of a task");
            Assert.That(outcome.Results, Has.None.EqualTo(SCRIPT_VALUE));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("5 value(s) its 5 task(s) did not ask for"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task AValueUnderAScriptVariableIsLeftOutHoweverManyTasksCarryItTest(bool built)
        {
            var nodes = new[] { GridTestTasks.Node(10) };
            var tasks = Enumerable.Range(0, 3).Select(index => GridTestTasks.Either(built, index, null, "returnCode")).ToList();
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, tasks, group =>
            {
                var answers = Honest(group);
                answers.Insert(0, new WitVariableObject("returnCode", SURPLUS));
                answers.Add(new WitVariableObject("returnCode", SURPLUS));
                return answers;
            }, warnings);

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(3), "three tasks, three values");
            Assert.That(outcome.Results, Has.None.EqualTo(SURPLUS));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("2 value(s)"));
        }

        [Test]
        public async Task TheSlotIsTheReturnReferenceOfTheActivityAndNotAVariableNamedLikeASlotTest()
        {
            // The script has a variable whose name is a slot name of its own. For a task as Grid builds
            // it the activity's return reference says which of the two the task asked for.
            var nodes = new[] { GridTestTasks.Node(10) };
            var tasks = Enumerable.Range(0, 4).Select(index => GridTestTasks.Built(index, null, "return77")).ToList();
            var warnings = new List<string>();

            var outcome = await RunAsync(nodes, tasks, group =>
            {
                var answers = Honest(group);
                answers.Add(new WitVariableObject("return77", SURPLUS));
                return answers;
            }, warnings);

            Assert.That(outcome.Results, Has.Count.EqualTo(4));
            Assert.That(outcome.Results, Has.None.EqualTo(SURPLUS));
            Assert.That(warnings, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ATaskWithoutASlotHasNoValueWhateverItsVariablesAreNamedTest()
        {
            // A transformer that returns nothing has no slot. Up to 1.3.0 a script variable named like
            // a return variable stood in for one, and came back as a result of every task.
            var nodes = new[] { GridTestTasks.Node(10) };
            var tasks = Enumerable.Range(0, 3).Select(index => GridTestTasks.WithoutSlot(index, "returnCode")).ToList();
            var warnings = new List<string>();

            var asked = await RunAsync(nodes, tasks, Honest, warnings);
            var asUpTo130 = await RunAsync(nodes, tasks, AskedAsUpTo130, warnings);

            Assert.That(asked.Results, Is.Empty);
            Assert.That(asUpTo130.Results, Is.Empty);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("3 value(s) its 3 task(s) did not ask for"));
        }

        #endregion

        #region Copies Tests

        [TestCase(true)]
        [TestCase(false)]
        public async Task ACopyAnsweredUnderAScriptVariableAsWellGivesItsMachineNoSecondValueTest(bool built)
        {
            // One item, two copies, two machines, and a script variable named like a return variable.
            // Up to 1.3.0 the variable was a second slot of every task, so one machine could place two
            // values of its own among the copies of an item.
            var liar = GridTestTasks.Node(100);
            var other = GridTestTasks.Node(50);

            var outcome = await RunAsync(
                new[] { liar, other },
                new[] { GridTestTasks.Either(built, 0, 0, "returnCode"), GridTestTasks.Either(built, 1, 0, "returnCode") },
                group =>
                {
                    var answers = Honest(group);
                    if (group.Node.NodeId == liar.NodeId)
                        answers.Add(new WitVariableObject("returnCode", Answer(group, GridTestTasks.ReturnName(9))));

                    return answers;
                });

            var makers = SlotAnswers(outcome).Select(value => value.Split(':')[0]).ToList();

            Assert.That(outcome.Failed, Is.False);
            Assert.That(outcome.Results, Has.Count.EqualTo(2), "two copies, two values");
            Assert.That(makers, Is.Unique, "one value from each machine");
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task WithScriptVariablesNamedLikeSlotsNoMachineHasMoreValuesThanTasksTest(bool built)
        {
            var random = new Random(1119);

            for (int round = 0; round < 200; round++)
            {
                int machines = random.Next(2, 7);
                var nodes = Enumerable.Range(0, machines).Select(_ => GridTestTasks.Node(0.1 + random.NextDouble() * 20)).ToArray();
                var liars = nodes.Where(_ => random.NextDouble() < 0.5).Select(node => node.NodeId).ToHashSet();

                int count = random.Next(1, 30);
                var tasks = Enumerable.Range(0, count).Select(index => GridTestTasks.Either(built, index, index / 3, "returnCode", "returned.Item1")).ToList();

                var held = new Dictionary<string, int>();

                var outcome = await RunAsync(nodes, tasks, group =>
                {
                    string maker = group.Node.NodeId.ToString("N");
                    held[maker] = held.GetValueOrDefault(maker) + group.Count;

                    var answers = Honest(group);
                    if (!liars.Contains(group.Node.NodeId))
                        return answers;

                    // Every forged value says it is this node's own, under a script variable's name.
                    int extra = random.Next(1, 6);
                    for (int index = 0; index < extra; index++)
                    {
                        string name = random.NextDouble() < 0.5 ? "returnCode" : "returned.Item1";
                        answers.Insert(random.Next(answers.Count + 1), new WitVariableObject(name, Answer(group, GridTestTasks.ReturnName(count + index))));
                    }

                    return answers;
                });

                int ran = held.Values.Sum();

                Assert.That(outcome.Failed, Is.False);
                Assert.That(outcome.Results, Has.Count.EqualTo(ran), "one value for each task that ran");

                foreach (var machine in SlotAnswers(outcome).GroupBy(value => value.Split(':')[0]))
                    Assert.That(machine.Count(), Is.LessThanOrEqualTo(held[machine.Key]), "a machine with more values than tasks");
            }
        }

        #endregion
    }
}
