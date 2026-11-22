using System.Diagnostics;
using System.Text;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tests.Scripts;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Tests.Load;

/// <summary>
/// Load through the engine (Category=Load): thousands of tasks through the bundled VerifyRun
/// on the in-process nodes with sampling on, the whole distributed shape under one roof. The
/// number this prints is a floor for the fleet, not a measure of it.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Load")]
public sealed class VerifyEngineLoadTests : VerifyScriptTestBase
{
    #region Constants

    private const int TASKS = 3000;
    private const int BATCH_SIZE = 64;
    private const double SAMPLING = 0.05;

    #endregion

    #region Tests

    [Test]
    public async Task ThousandsOfTasksThroughVerifyRunWithSamplingTest()
    {
        var taskset = new StringBuilder(TASKS * 120);
        for (var i = 0; i < TASKS; i++)
        {
            var runtime = i % 4 == 0 ? "quickjs-0.15.1" : "python-3.14.6";
            var source = i % 4 == 0 ? $"console.log({i} * 3)" : $"print({i} * 3)";
            taskset.Append("{\"index\":").Append(i).Append(",\"runtime\":\"").Append(runtime).Append("\",\"sources\":{\"m\":\"").Append(source)
                .Append("\"},\"suite\":[{\"expected_stdout\":\"").Append(i * 3).Append("\\n\"}]}\n");
        }

        var blobId = await UploadAsync(taskset.ToString(), "taskset.jsonl");
        var job = Engine.Compile(LoadScript("VerifyRun.wit"));

        var stopwatch = Stopwatch.StartNew();
        var status = await Engine.ScheduleAndWaitAsync(job, blobId, new VerifyOptionsData { BatchSize = BATCH_SIZE, SamplingRate = SAMPLING });
        stopwatch.Stop();

        Assert.That(status.Result, Is.EqualTo(WitProcessingResult.Completed), status.ToString());

        using var report = await ReadJsonAsync((Guid)job.Variables["report"].Value!);
        using var integrity = await ReadJsonAsync((Guid)job.Variables["integrity"].Value!);
        var summary = report.RootElement.GetProperty("summary");
        var sampled = integrity.RootElement.GetProperty("summary");

        TestContext.Out.WriteLine(
            $"{TASKS} tasks in batches of {BATCH_SIZE} with sampling {SAMPLING}: {stopwatch.Elapsed.TotalSeconds:F1} s, {TASKS / stopwatch.Elapsed.TotalSeconds:F1} tasks/s end to end; " +
            $"sampled {sampled.GetProperty("sampled").GetInt32()}, compared {sampled.GetProperty("compared").GetInt32()}, mismatches {sampled.GetProperty("mismatches").GetInt32()}");

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("total").GetInt32(), Is.EqualTo(TASKS));
            Assert.That(summary.GetProperty("pass").GetInt32(), Is.EqualTo(TASKS));
            Assert.That(sampled.GetProperty("sampled").GetInt32(), Is.InRange(TASKS * SAMPLING * 0.6, TASKS * SAMPLING * 1.4));
            Assert.That(sampled.GetProperty("mismatches").GetInt32(), Is.Zero);
        });
    }

    #endregion
}
