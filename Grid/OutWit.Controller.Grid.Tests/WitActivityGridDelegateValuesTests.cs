using System;
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
    /// What Grid.Delegate takes of a node's answer. The value is the one under the slot the node was
    /// asked for; anything else is left out with a warning. A typed return variable that does not take
    /// the value keeps the value it had, and a warning says so: nothing fails for it. An answer as
    /// asked sets the variable exactly as it always did, a null included.
    /// </summary>
    [TestFixture]
    public class WitActivityGridDelegateValuesTests
    {
        #region Constants

        private const string RESULT = "result";

        private const string NOT_ASKED = "did not ask for";

        private const string NOT_TAKEN = "did not take";

        #endregion

        #region Fields

        private MockNodesManager m_nodes = null!;

        private MockLogger m_logger = null!;

        #endregion

        #region Initialization

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            var controllersPath = TestControllers.FindPath()
                                  ?? throw new DirectoryNotFoundException("@Controllers directory not found");

            m_nodes = new MockNodesManager();
            m_logger = new MockLogger();

            WitEngineSdk.Instance.Reload(Guid.NewGuid(), m_nodes, false, m_logger, controllersPath);
        }

        [SetUp]
        public void Setup()
        {
            m_nodes.Batches.Clear();
            m_nodes.Answers = null;
            m_logger.Clear();
        }

        #endregion

        #region Tools

        /// <summary>
        /// Runs a job whose variable holds a value already and is then given the delegated activity's.
        /// </summary>
        /// <param name="declaration">The declaration of the variable with its first value.</param>
        /// <param name="transformer">The delegated activity.</param>
        private static (IWitProcessingStatus Status, object? Value) Run(string declaration, string transformer = "Int(10)")
        {
            string script = $$"""
                              Job:TestJob()
                              {
                                  {{declaration}}
                                  {{RESULT}} = Grid.Delegate => {{transformer}};
                              }
                              """;

            var job = WitEngineSdk.Instance.Compile(script);
            var task = WitEngineSdk.Instance.ScheduleProcessing(job);
            var finished = new AutoResetEvent(false);

            task.ProcessingFinished += (_, __) => { finished.Set(); };
            task.Run();

            Assert.That(finished.WaitOne(TimeSpan.FromSeconds(60)), Is.True, "the job did not finish");

            return (task.Status!, job.Variables[RESULT].Value);
        }

        private static IWitVariable Named(string name, object? value)
        {
            return new WitVariableObject(name, value);
        }

        #endregion

        #region As Before Tests

        [Test]
        public void AnAnswerAsAskedSetsTheVariableAsBeforeTest()
        {
            var (status, value) = Run($"Int:{RESULT} = Int(5);");

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(value, Is.EqualTo(10));
            Assert.That(m_nodes.Batches.Single().Requests.Single().ReturnVariables, Is.EqualTo(new[] { "return0" }));
            Assert.That(m_logger.WarningsWith(NOT_ASKED), Is.Empty);
            Assert.That(m_logger.WarningsWith(NOT_TAKEN), Is.Empty);
        }

        [Test]
        public void AnAnswerAsAskedSetsAVariableOfObjectsAsBeforeTest()
        {
            var (status, value) = Run($"Object:{RESULT} = Object(\"before\");");

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(value, Is.EqualTo(10));
            Assert.That(m_logger.WarningsWith(NOT_ASKED), Is.Empty);
            Assert.That(m_logger.WarningsWith(NOT_TAKEN), Is.Empty);
        }

        [Test]
        public void ADelegatedActivityThatReturnsNothingLeavesTheVariableAsItWasTest()
        {
            var (status, value) = Run($"Int:{RESULT} = Int(5);", "Trace(\"hello\")");

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(value, Is.EqualTo(5));
            Assert.That(m_nodes.Batches.Single().Requests.Single().ReturnVariables, Is.Empty);
            Assert.That(m_logger.WarningsWith(NOT_ASKED), Is.Empty);
            Assert.That(m_logger.WarningsWith(NOT_TAKEN), Is.Empty);
        }

        [Test]
        public void ANullUnderTheAskedSlotClearsAVariableThatTakesNullAsBeforeTest()
        {
            // The delegated activity may return null. A variable that can hold null is set to it, as
            // it always was, and nothing is reported.
            m_nodes.Answers = (_, answers) => answers.Select(answer => Named(answer.Name, null)).ToList();

            var (status, value) = Run($"Object:{RESULT} = Object(\"before\");");

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(value, Is.Null);
            Assert.That(m_logger.WarningsWith(NOT_ASKED), Is.Empty);
            Assert.That(m_logger.WarningsWith(NOT_TAKEN), Is.Empty);
        }

        [Test]
        public void ANullUnderTheAskedSlotLeavesAVariableOfAValueTypeAsItWasAsBeforeTest()
        {
            // A variable of a value type cannot hold null: it kept its value before, without a word.
            // It still keeps it; the one thing new is the warning.
            m_nodes.Answers = (_, answers) => answers.Select(answer => Named(answer.Name, null)).ToList();

            var (status, value) = Run($"Int:{RESULT} = Int(5);");

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(value, Is.EqualTo(5));
            Assert.That(m_logger.WarningsWith(NOT_TAKEN), Has.Count.EqualTo(1));
            Assert.That(m_logger.WarningsWith(NOT_TAKEN)[0], Does.Contain($"the return variable '{RESULT}' did not take (null where Int32 was expected)"));
        }

        #endregion

        #region Left Out Tests

        [Test]
        public void AValueUnderAnotherNameIsLeftOutTest()
        {
            // Up to 1.3.0 the first value a node returned was the result, whatever its name.
            m_nodes.Answers = (_, _) => new[] { Named("anything", 99) };

            var (status, value) = Run($"Int:{RESULT} = Int(5);");

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(value, Is.EqualTo(5), "nothing came under the slot the node was asked for");
            Assert.That(m_logger.WarningsWith(NOT_ASKED), Has.Count.EqualTo(1));
            Assert.That(m_logger.WarningsWith(NOT_ASKED)[0], Does.Contain("1 value(s) its 1 task(s) did not ask for"));
            Assert.That(m_logger.WarningsWith(NOT_TAKEN), Is.Empty);
        }

        [Test]
        public void TheValueUnderTheAskedSlotIsTheResultWhateverComesBeforeItTest()
        {
            m_nodes.Answers = (_, answers) => new[] { Named("anything", 99) }.Concat(answers).Append(Named(answers[0].Name, 77)).ToList();

            var (status, value) = Run($"Int:{RESULT} = Int(5);");

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(value, Is.EqualTo(10), "the first value under the asked slot");
            Assert.That(m_logger.WarningsWith(NOT_ASKED), Has.Count.EqualTo(1));
            Assert.That(m_logger.WarningsWith(NOT_ASKED)[0], Does.Contain("2 value(s) its 1 task(s) did not ask for"));
        }

        #endregion

        #region Not Taken Tests

        [Test]
        public void AValueOfAnotherTypeLeavesTheVariableAsItWasAndIsReportedTest()
        {
            // The typed variable refuses the value and keeps its own. The engine reports the value as
            // set all the same, so up to 1.3.0 nothing said that the script went on with the old one.
            m_nodes.Answers = (_, answers) => answers.Select(answer => Named(answer.Name, "text")).ToList();

            var (status, value) = Run($"Int:{RESULT} = Int(5);");

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), "a value that is not taken does not fail the job");
            Assert.That(value, Is.EqualTo(5));
            Assert.That(m_logger.WarningsWith(NOT_TAKEN), Has.Count.EqualTo(1));
            Assert.That(m_logger.WarningsWith(NOT_TAKEN)[0], Does.Contain($"the return variable '{RESULT}' did not take (of type String where Int32 was expected)"));
            Assert.That(m_logger.WarningsWith(NOT_TAKEN)[0], Does.Contain("the variable keeps the value it had"));
        }

        [Test]
        public void AValueOfAnyTypeIsTakenByAVariableOfObjectsAsBeforeTest()
        {
            m_nodes.Answers = (_, answers) => answers.Select(answer => Named(answer.Name, "text")).ToList();

            var (status, value) = Run($"Object:{RESULT} = Object(\"before\");");

            Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.Message);
            Assert.That(value, Is.EqualTo("text"));
            Assert.That(m_logger.WarningsWith(NOT_TAKEN), Is.Empty);
        }

        #endregion
    }
}
