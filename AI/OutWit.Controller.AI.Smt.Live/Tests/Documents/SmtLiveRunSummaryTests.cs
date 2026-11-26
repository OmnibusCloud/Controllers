using OutWit.Controller.AI.Smt.Live.Documents;
using OutWit.Controller.AI.Smt.Live.Tests.Mock;

namespace OutWit.Controller.AI.Smt.Live.Tests.Documents;

/// <summary>
/// The summary of a run's documents on the fixture run: a row said loudly is an alarm whatever
/// the report counts, the report is held to the rows it was written from, every alarm has a
/// loud line, and the lines a lab reads for the run's guarantees are there.
/// </summary>
[TestFixture]
public sealed class SmtLiveRunSummaryTests
{
    #region Fields

    private string m_folder = null!;
    private StringWriter m_output = null!;

    #endregion

    #region Setup

    [SetUp]
    public void Setup()
    {
        m_folder = SmtLiveFixtures.CopyRun(SmtLiveFixtures.NewFolder());
        m_output = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        TestContext.Out.WriteLine(m_output.ToString());
        SmtLiveFixtures.Delete(m_folder);
        m_output.Dispose();
    }

    #endregion

    #region Alarm Tests

    [Test]
    public void TheFixtureRunIsNotAlarmingTest()
    {
        var alarms = Print();

        Assert.That(alarms, Is.Empty);
        Assert.That(m_output.ToString(), Does.Not.Contain(SmtLiveRunSummary.LOUD));
    }

    [Test]
    public void ARowThatDisagreesWithTheExpectedVerdictIsAnAlarmWhateverTheReportCountsTest()
    {
        SmtLiveFixtures.EditRow(Path(SmtLiveDocuments.RESULTS), 1, row => row["expected_agreement"] = "disagree");

        var alarms = Print();

        Assert.That(alarms, Has.Some.Contains("1 verdict(s) that disagree with the expected one"));
        Assert.That(m_output.ToString(), Does.Contain("!!! expected  : 1 verdict(s) disagree with the expected one"));
    }

    [Test]
    public void ARowWhoseModelTheEvaluatorCallsFailedIsAnAlarmWhateverTheReportCountsTest()
    {
        SmtLiveFixtures.EditRow(Path(SmtLiveDocuments.RESULTS), 0, row =>
        {
            row["validation"] = "failed";
            row["validation_reason"] = "assertion_false";
        });

        var alarms = Print();

        Assert.That(alarms, Has.Some.Contains("1 settled row(s) whose model the evaluator calls failed"));
    }

    [Test]
    public void AReportThatDoesNotCountTheRowsItWasWrittenFromIsAnAlarmTest()
    {
        SmtLiveFixtures.Edit(Path(SmtLiveDocuments.REPORT), root =>
        {
            root["statuses"]!["ok"] = 2;
            root["verdicts"]!["sat"] = 1;
        });

        var alarms = Print();

        Assert.That(alarms, Has.Some.Contains("the report and the results disagree"));
        Assert.That(m_output.ToString(), Does.Contain("!!! rows      : THE REPORT AND THE RESULTS DISAGREE").And.Contain("statuses ok: report 2, rows 3").And.Contain("verdicts sat: report 1, rows 2"));
    }

    [Test]
    public void AReportThatCountsOtherRowsThanTheResultsHoldIsAnAlarmTest()
    {
        SmtLiveFixtures.Edit(Path(SmtLiveDocuments.REPORT), root => root["problem_set"]!["rows"] = 5);

        var alarms = Print();

        Assert.That(alarms, Has.Some.Contains("the report and the results disagree"));
        Assert.That(m_output.ToString(), Does.Contain("rows: report 5, rows 4"));
    }

    [Test]
    public void ASamplingThatDidNotHappenIsSaidLoudlyTest()
    {
        SmtLiveFixtures.Edit(Path(SmtLiveDocuments.REPORT), root => root["run"]!["sampling"] = "short");

        var alarms = Print();

        Assert.That(alarms, Has.Some.Contains("(short)"));
        Assert.That(m_output.ToString(), Does.Contain("!!! sampling  :"));
    }

    [Test]
    public void APackageAnsweredInTheRepairRoundIsNotSaidLoudlyTest()
    {
        SmtLiveFixtures.Edit(Path(SmtLiveDocuments.INTEGRITY), root =>
        {
            root["fan_outs"]![0]!["unanswered"] = 1;
            root["fan_outs"]![1]!["packages"] = 1;
            root["fan_outs"]![1]!["complete"] = 1;
            root["summary"]!["repairs"] = 1;
        });

        var alarms = Print();

        Assert.That(alarms, Is.Empty, "every problem has its result");
        Assert.That(m_output.ToString(), Does.Not.Contain(SmtLiveRunSummary.LOUD));
        Assert.That(m_output.ToString(), Does.Contain("main: 1 package(s) of 3 problem(s), 1 asked for 2 answer(s), 1 complete, 1 unanswered"));
    }

    #endregion

    #region Guarantee Tests

    [Test]
    public void TheSummarySaysTheBindingAndTheCompleteEnumerationsBesideTheUnsatOnOneMachineTest()
    {
        SmtLiveFixtures.Edit(Path(SmtLiveDocuments.REPORT), root =>
        {
            root["complete_one_copy"]!["count"] = 1;
            root["complete_one_copy"]!["indices"] = new System.Text.Json.Nodes.JsonArray(2);
        });

        Print();

        Assert.That(m_output.ToString(), Does.Contain("1 complete enumeration(s) that found models (2)"));
        Assert.That(m_output.ToString(), Does.Contain("binding   : bound: 2 result(s) told the package they came back for, 0 not"));
    }

    #endregion

    #region Tools

    private IReadOnlyList<string> Print()
    {
        return new SmtLiveRunSummary(m_output).Print(Path(SmtLiveDocuments.REPORT), Path(SmtLiveDocuments.INTEGRITY), Path(SmtLiveDocuments.RESULTS));
    }

    private string Path(SmtLiveDocument document)
    {
        return System.IO.Path.Combine(m_folder, document.File);
    }

    #endregion
}
