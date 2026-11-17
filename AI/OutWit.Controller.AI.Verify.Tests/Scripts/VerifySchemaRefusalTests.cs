using System.Text;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tests.Mock;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Scripts;

/// <summary>
/// A taskset the schema does not allow, through the engine, end to end (audit finding V:M-4):
/// the preflight says what is wrong by name before anything is submitted, and the run is
/// refused where it is split with the same words, before any node is asked. The audit's own
/// input: a program that prints 1, a case that was meant to expect 2, and a misspelt key. It
/// used to be well formed to the preflight and a pass in the report.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifySchemaRefusalTests : VerifyScriptTestBase
{
    #region Constants

    private const string MISSPELT = """{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"suite":[{"expected_stdoutt":"2\n"}]}""";
    private const string REFUSAL = "unknown field 'suite[0].expected_stdoutt'";

    #endregion

    #region Tests

    [Test]
    public async Task M4AMisspeltExpectationIsRefusedAtThePreflightAndAtTheSplitTest()
    {
        var taskset = await UploadAsync(MISSPELT + "\n", "taskset.jsonl");

        var preflight = Engine.Compile(LoadScript("VerifyPreflight.wit"));
        var asked = await Engine.ScheduleAndWaitAsync(preflight, taskset, new VerifyOptionsData());
        Assert.That(asked.Result, Is.EqualTo(WitProcessingResult.Completed), asked.Message);
        var answer = (VerifyPreflightData)preflight.Variables["result"].Value!;

        var run = Engine.Compile(LoadScript("VerifyRun.wit"));
        var status = await Engine.ScheduleAndWaitAsync(run, taskset, new VerifyOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(answer.WellFormed, Is.False, "the preflight says so beforehand");
            Assert.That(answer.Messages, Is.EqualTo(new[] { "line 1: " + REFUSAL }));

            Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed), "the run is refused, not passed");
            Assert.That(status.Message, Does.Contain("line 1: " + REFUSAL), "by the name of what is wrong");
            Assert.That(run.Variables["report"].Value, Is.Null.Or.EqualTo(Guid.Empty), "there is no report to read a pass from");
            VerifyLeakScan.AssertClean(status.Message ?? "", "the message of the refused taskset");
        });
    }

    [Test]
    public async Task M4ACheckerWithAMisspeltRuleFailsTheJobByNameTest()
    {
        // With the rule dropped, "exit code 0" was the whole check and every candidate passed.
        var generators = await UploadAsync("""{"runtime":"python-3.14.6","sources":{"m.py":"print(1)"}}""" + "\n", "generators.jsonl");
        var checker = await UploadAsync("""{"runtime":"python-3.14.6","sources":{"c.py":"print('MISMATCH')"},"expected_stdoutt":"OK\n"}""", "checker.json");

        var status = await Engine.ScheduleAndWaitAsync(Engine.Compile(LoadScript("VerifyGenerateAndCheck.wit")), generators, checker, new VerifyOptionsData());

        Assert.Multiple(() =>
        {
            Assert.That(status.Result, Is.Not.EqualTo(WitProcessingResult.Completed));
            Assert.That(status.Message, Does.Contain("unknown field 'expected_stdoutt'"));
            VerifyLeakScan.AssertClean(status.Message ?? "", "the message of the refused checker");
        });
    }

    [Test]
    public async Task L7APreflightOfThousandsOfBadLinesAnswersInBoundedWordsTest()
    {
        var lines = new StringBuilder();
        for (var index = 0; index < 5000; index++)
            lines.Append(index % 2 == 0 ? "not json " : MISSPELT).Append('\n');
        var taskset = await UploadAsync(lines.ToString(), "taskset.jsonl");

        var preflight = Engine.Compile(LoadScript("VerifyPreflight.wit"));
        var asked = await Engine.ScheduleAndWaitAsync(preflight, taskset, new VerifyOptionsData());
        Assert.That(asked.Result, Is.EqualTo(WitProcessingResult.Completed), asked.Message);
        var answer = (VerifyPreflightData)preflight.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(answer.WellFormed, Is.False);
            Assert.That(answer.TaskCount, Is.Zero);
            Assert.That(answer.Messages, Has.Count.LessThanOrEqualTo(OutWit.Controller.AI.Verify.Tasksets.VerifyPreflightReport.MaxMessages));
            Assert.That(answer.Messages.Max(message => message.Length), Is.LessThanOrEqualTo(OutWit.Controller.AI.Verify.Tasksets.VerifyTasksetLimits.MAX_MESSAGE_LENGTH));
            Assert.That(answer.Messages[^1], Does.StartWith("and ").And.EndWith(" more malformed line(s) not shown"));
        });
    }

    #endregion
}
