using System.Globalization;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Controller.AI.Verify.Sandbox;
using OutWit.Engine.Data.Benchmark;

namespace OutWit.Controller.AI.Verify.Tests.Sandbox;

/// <summary>
/// The node benchmark: the reference workload scales with the degree, computes the pinned
/// answers, and scores as tasks per second of the median run with the run details attached.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VerifyBenchmarkTests : VerifySandboxTestBase
{
    #region Workload Tests

    [Test]
    public void ReferenceBatchesFillTheDegreeFourTimesOverTest()
    {
        var batches = VerifyBenchmark.ReferenceBatches(3);

        Assert.That(batches, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(batches[0].RuntimeId, Is.EqualTo(VerifyRuntimeCatalog.PYTHON_3_14_6));
            Assert.That(batches[1].RuntimeId, Is.EqualTo(VerifyRuntimeCatalog.QUICKJS_0_15_1));
            Assert.That(batches.Select(batch => batch.Tasks.Count), Is.All.EqualTo(3 * VerifyBenchmark.TASKS_PER_WORKER));
            Assert.That(batches.Select(batch => batch.MaxDegree), Is.All.EqualTo(3));
            Assert.That(batches.SelectMany(batch => batch.Tasks).Select(task => task.TaskIndex), Is.Unique);
            Assert.That(batches.SelectMany(batch => batch.Tasks).Select(task => task.Suite?.Cases.Count), Is.All.EqualTo(1));
        });
    }

    [Test]
    public void ReferenceTasksPassOnThisMachineTest()
    {
        foreach (var batch in VerifyBenchmark.ReferenceBatches(1))
        {
            var result = VerifyBatchExecutor.Execute(Sandbox, RuntimesRoot, batch);

            Assert.That(result.Results.Select(r => r.Verdict), Is.All.EqualTo(VerifyVerdict.Pass),
                $"{batch.RuntimeId}: {string.Join(" | ", result.Results.Select(r => r.Stderr.Trim()))}");
        }
    }

    #endregion

    #region Scoring Tests

    [Test]
    public void MeasureScoresTasksPerSecondAtTheDegreeTest()
    {
        var options = new WitBenchmarkOptions
        {
            MinDuration = TimeSpan.FromMilliseconds(100),
            WarmupIterations = 1,
            DegreeOfParallelism = 2
        };

        var result = VerifyBenchmark.Measure(Sandbox, RuntimesRoot, options, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Unit, Is.EqualTo(VerifyBenchmark.UNIT));
            Assert.That(result.Rate, Is.GreaterThan(0));
            Assert.That(result.Iterations, Is.InRange(VerifyBenchmark.MIN_RUNS, VerifyBenchmark.MAX_RUNS));
            Assert.That(result.Elapsed, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(result.Custom, Is.Not.Null);
            Assert.That(result.Custom!["degree"], Is.EqualTo("2"));
            Assert.That(result.Custom["tasks"], Is.EqualTo((2 * 2 * VerifyBenchmark.TASKS_PER_WORKER).ToString(CultureInfo.InvariantCulture)));
            Assert.That(result.Custom["runs_s"].Split(';'), Has.Length.EqualTo(result.Iterations));
            Assert.That(result.Custom.ContainsKey("median_s"), Is.True);
            Assert.That(result.Custom.ContainsKey("warmup_s"), Is.True);
            Assert.That(result.Custom["wasmtime"], Does.StartWith("44."));
        });
    }

    [Test]
    public void MedianOfAnEvenCountIsTheMiddleAverageTest()
    {
        var median = VerifyBenchmark.Median([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)]);

        Assert.That(median, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
    }

    [Test]
    public void RateIsTasksOverTheMedianRunTest()
    {
        var result = VerifyBenchmark.ToResult([TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(3)], TimeSpan.FromSeconds(1), 30);

        Assert.Multiple(() =>
        {
            Assert.That(result.Rate, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(result.Iterations, Is.EqualTo(3));
            Assert.That(result.Elapsed, Is.EqualTo(TimeSpan.FromSeconds(9)));
            Assert.That(result.Custom!["median_s"], Is.EqualTo("3.000"));
            Assert.That(result.Custom["warmup_s"], Is.EqualTo("1.000"));
        });
    }

    [Test]
    public void NoRunsIsAnErrorTest()
    {
        Assert.Throws<ArgumentException>(() => VerifyBenchmark.ToResult([], TimeSpan.Zero, 1));
    }

    #endregion
}
