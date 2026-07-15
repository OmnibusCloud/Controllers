using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// Host-side pipeline units — parsing, chunking, ceiling enforcement, report assembly —
/// with no sandbox dependency, so they run everywhere (not opt-in on the runtime download).
/// </summary>
[TestFixture]
public sealed class VerifyTasksetPipelineTests
{
    #region Parser Tests

    [Test]
    public void ParsesWellFormedJsonlTest()
    {
        const string jsonl = """
            {"index":0,"runtime":"python-3.14.6","entry":"main.py","sources":{"main.py":"print(1)"},"seed":7}
            {"runtime":"quickjs-0.15.1","sources":{"main.js":"console.log(1)"},"suite":[{"expected_stdout":"1\n","expected_exit":0}]}
            """;

        var result = VerifyTasksetParser.Parse(jsonl);

        Assert.That(result.Errors, Is.Empty);
        Assert.That(result.Tasks, Has.Count.EqualTo(2));
        Assert.That(result.Tasks[0].RandomSeed, Is.EqualTo(7));
        Assert.That(result.Tasks[0].EntryPoint, Is.EqualTo("main.py"));
        Assert.That(result.Tasks[1].TaskIndex, Is.EqualTo(1), "index defaults to line ordinal");
        Assert.That(result.Tasks[1].EntryPoint, Is.EqualTo("main.js"), "entry defaults to the sole source");
        Assert.That(result.Tasks[1].Suite!.Cases, Has.Count.EqualTo(1));
    }

    [Test]
    public void BlankLinesAreSkippedTest()
    {
        const string jsonl = "\n{\"runtime\":\"python-3.14.6\",\"sources\":{\"a.py\":\"pass\"}}\n\n";

        var result = VerifyTasksetParser.Parse(jsonl);

        Assert.That(result.Tasks, Has.Count.EqualTo(1));
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public void MalformedLinesAreCollectedNotThrownTest()
    {
        const string jsonl = """
            {"runtime":"python-3.14.6","sources":{"a.py":"pass"}}
            {not json}
            {"sources":{"a.py":"pass"}}
            {"runtime":"python-3.14.6","entry":"missing.py","sources":{"a.py":"pass"}}
            """;

        var result = VerifyTasksetParser.Parse(jsonl);

        Assert.That(result.Tasks, Has.Count.EqualTo(1));
        Assert.That(result.Errors, Has.Count.EqualTo(3));
        Assert.That(result.Errors[0], Does.Contain("line 2"));
        Assert.That(result.Errors[1], Does.Contain("missing").IgnoreCase.Or.Contain("runtime"));
        Assert.That(result.Errors[2], Does.Contain("entry"));
    }

    #endregion

    #region Planner Tests

    [Test]
    public void ChunksByRuntimeAffinityTest()
    {
        var tasks = new List<VerifyTaskData>
        {
            Task(0, "python-3.14.6"), Task(1, "quickjs-0.15.1"),
            Task(2, "python-3.14.6"), Task(3, "python-3.14.6")
        };

        var plan = VerifyTasksetPlanner.Plan(tasks, new VerifyOptionsData { BatchSize = 10 });

        Assert.That(plan.Batches, Has.Count.EqualTo(2), "one batch per runtime under a big batch size");
        var python = plan.Batches.Single(b => b.RuntimeId == "python-3.14.6");
        Assert.That(python.Tasks.Select(t => t.TaskIndex), Is.EqualTo(new[] { 0, 2, 3 }));
    }

    [Test]
    public void RespectsBatchSizeTest()
    {
        var tasks = Enumerable.Range(0, 7).Select(i => Task(i, "python-3.14.6")).ToList();

        var plan = VerifyTasksetPlanner.Plan(tasks, new VerifyOptionsData { BatchSize = 3 });

        Assert.That(plan.Batches, Has.Count.EqualTo(3));
        Assert.That(plan.Batches.Select(b => b.Tasks.Count), Is.EqualTo(new[] { 3, 3, 1 }));
    }

    [Test]
    public void AppliesDefaultLimitsAndClampsToCeilingsTest()
    {
        var task = Task(0, "python-3.14.6");
        task.Limits = new VerifyLimitsData { FuelBudget = VerifyLimitCeilings.MAX_FUEL_BUDGET * 10 }; // over ceiling

        var plan = VerifyTasksetPlanner.Plan([task],
            new VerifyOptionsData { DefaultLimits = new VerifyLimitsData { MemoryBytes = 128 * 1024 * 1024 } });

        var planned = plan.Batches.Single().Tasks.Single();
        Assert.That(planned.Limits!.FuelBudget, Is.EqualTo(VerifyLimitCeilings.MAX_FUEL_BUDGET), "over-ceiling fuel clamped");
        Assert.That(planned.Limits.MemoryBytes, Is.EqualTo(128 * 1024 * 1024), "default applied where task was unset");
        Assert.That(plan.Notes, Has.Some.Contains("clamped"));
    }

    #endregion

    #region Report Tests

    [Test]
    public void ReportAggregatesVerdictsAndReKeysByIndexTest()
    {
        var results = new List<VerifyResultData>
        {
            new() { TaskIndex = 2, Verdict = VerifyVerdict.Fail, FuelConsumed = 100 },
            new() { TaskIndex = 0, Verdict = VerifyVerdict.Pass, FuelConsumed = 50 },
            new() { TaskIndex = 1, Verdict = VerifyVerdict.Pass, FuelConsumed = 70 }
        };

        var bytes = VerifyReportWriter.Write(results);
        var summary = VerifyReportWriter.ReadSummary(bytes);

        Assert.That(summary.Total, Is.EqualTo(3));
        Assert.That(summary.Pass, Is.EqualTo(2));
        // results ordered by index: 0,1,2
        Assert.That(summary.Json.IndexOf("\"index\":0", StringComparison.Ordinal),
            Is.LessThan(summary.Json.IndexOf("\"index\":2", StringComparison.Ordinal)));
        Assert.That(summary.Json, Does.Contain("\"total_fuel\":220"));
        Assert.That(summary.Json, Does.Contain("\"Pass\":2"));
        Assert.That(summary.Json, Does.Contain("\"Fail\":1"));
    }

    #endregion

    #region Helpers

    private static VerifyTaskData Task(int index, string runtime)
    {
        return new VerifyTaskData
        {
            TaskIndex = index,
            RuntimeId = runtime,
            Sources = [new VerifySourceFileData { Name = "main", Content = "x" }],
            EntryPoint = "main"
        };
    }

    #endregion
}
