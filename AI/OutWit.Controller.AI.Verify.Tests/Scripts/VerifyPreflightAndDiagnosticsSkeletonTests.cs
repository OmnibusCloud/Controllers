using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Scripts;

/// <summary>
/// The two single-activity scripts through the engine: VerifyPreflight tells a lab what is
/// wrong with a taskset before anything runs, VerifyRuntimeDiagnostics tells an operator what
/// the host can run.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyPreflightAndDiagnosticsSkeletonTests : VerifyScriptTestBase
{
    #region Fields

    private string m_preflightScript = null!;
    private string m_diagnosticsScript = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_preflightScript = LoadScript("VerifyPreflight.wit");
        m_diagnosticsScript = LoadScript("VerifyRuntimeDiagnostics.wit");
    }

    #endregion

    #region Tests

    [Test]
    public async Task PreflightReportsCountsUnknownRuntimesAndMalformedLinesTest()
    {
        var taskset =
            """{"index":0,"runtime":"python-3.14.6","sources":{"m.py":"print(1)"},"limits":{"fuel":9000000000000}}""" + "\n" +
            """{"index":1,"runtime":"cobol-1959","sources":{"m.cob":"DISPLAY 1"}}""" + "\n" +
            """{"index":2,"runtime":"python-3.14.6"}""" + "\n";
        var blobId = await UploadAsync(taskset, "taskset.jsonl");

        var job = Engine.Compile(m_preflightScript);
        var status = await Engine.ScheduleAndWaitAsync(job, blobId, new VerifyOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
        var report = (VerifyPreflightData)job.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(report.WellFormed, Is.False);
            Assert.That(report.TaskCount, Is.EqualTo(2));
            Assert.That(report.BatchCount, Is.EqualTo(2), "one batch per runtime");
            Assert.That(report.RuntimeIds, Is.EquivalentTo(new[] { "python-3.14.6", "cobol-1959" }));
            Assert.That(report.UnknownRuntimeIds, Is.EqualTo(new[] { "cobol-1959" }));
            Assert.That(report.Messages, Has.Some.StartWith("line 3:"), "the malformed line");
            Assert.That(report.Messages, Has.Some.Contains("cobol-1959"));
            Assert.That(report.Messages, Has.Some.Contains("clamped"), "the over-ceiling fuel");
            Assert.That(report.EstimatedInputBytes, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task PreflightOfAnEmptyTasksetSaysSoTest()
    {
        var blobId = await UploadAsync("\n\n", "taskset.jsonl");

        var job = Engine.Compile(m_preflightScript);
        var status = await Engine.ScheduleAndWaitAsync(job, blobId, new VerifyOptionsData());

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
        var report = (VerifyPreflightData)job.Variables["result"].Value!;

        Assert.Multiple(() =>
        {
            Assert.That(report.WellFormed, Is.False);
            Assert.That(report.TaskCount, Is.Zero);
            Assert.That(report.Messages, Has.Some.Contains("empty"));
        });
    }

    [Test]
    public async Task RuntimeDiagnosticsListTheCatalogWithAvailabilityTest()
    {
        var job = Engine.Compile(m_diagnosticsScript);
        var status = await Engine.ScheduleAndWaitAsync(job);

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed));
        var diagnostics = (VerifyRuntimeDiagnosticsData)job.Variables["result"].Value!;
        var byId = diagnostics.Runtimes.ToDictionary(r => r.RuntimeId);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.RuntimesRootFound, Is.True);
            Assert.That(diagnostics.WasmtimeVersion, Does.StartWith("44."));
            Assert.That(diagnostics.RuntimeTarget, Does.Match("^(windows|linux|macos)-"));
            Assert.That(byId.Keys, Is.EquivalentTo(VerifyRuntimeCatalog.KnownIds));
            Assert.That(byId.Values.All(r => r.Available), Is.True, string.Join(", ", byId.Values.Where(r => !r.Available).Select(r => r.RuntimeId)));
            Assert.That(byId[VerifyRuntimeCatalog.WASI].Sha256, Is.Empty, "task-supplied: nothing to pin");
            Assert.That(byId[VerifyRuntimeCatalog.PYTHON_3_14_6].Sha256, Has.Length.EqualTo(64));
        });
    }

    #endregion
}
