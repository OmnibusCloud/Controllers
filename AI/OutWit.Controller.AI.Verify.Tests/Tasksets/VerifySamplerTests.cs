using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Tasksets;

namespace OutWit.Controller.AI.Verify.Tests.Tasksets;

/// <summary>
/// The sample is a function of the task index and the rate: nothing at 0, everything at 1,
/// about the rate in between, the same tasks every time, re-planned with their limits.
/// </summary>
[TestFixture]
public sealed class VerifySamplerTests
{
    [Test]
    public void RateZeroSamplesNothingAndRateOneEverythingTest()
    {
        var batches = Batches(20);

        Assert.Multiple(() =>
        {
            Assert.That(VerifySampler.Sample(batches, new VerifyOptionsData { SamplingRate = 0 }), Is.Empty);
            Assert.That(VerifySampler.Sample(batches, null), Is.Empty);
            Assert.That(VerifySampler.Sample(batches, new VerifyOptionsData { SamplingRate = 1 }).Sum(b => b.Tasks.Count), Is.EqualTo(20));
            Assert.That(VerifySampler.Sample(batches, new VerifyOptionsData { SamplingRate = 7 }).Sum(b => b.Tasks.Count), Is.EqualTo(20), "clamped");
        });
    }

    [Test]
    public void SampleIsAboutTheRateAndReproducibleTest()
    {
        var selected = Enumerable.Range(0, 10_000).Count(index => VerifySampler.IsSelected(index, 0.1));
        var again = Enumerable.Range(0, 10_000).Where(index => VerifySampler.IsSelected(index, 0.1)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(selected, Is.InRange(850, 1150), "a tenth, give or take");
            Assert.That(again, Has.Count.EqualTo(selected));
            Assert.That(VerifySampler.IsSelected(42, 0.5), Is.EqualTo(VerifySampler.IsSelected(42, 0.5)));
        });
    }

    [Test]
    public void SampleGrowsWithTheRateTest()
    {
        // A task in the sample at a lower rate stays in it at a higher one (the hash is compared to the rate).
        var low = Enumerable.Range(0, 1000).Where(index => VerifySampler.IsSelected(index, 0.2)).ToHashSet();
        var high = Enumerable.Range(0, 1000).Where(index => VerifySampler.IsSelected(index, 0.6)).ToHashSet();

        Assert.That(low, Is.SubsetOf(high));
        Assert.That(high, Has.Count.GreaterThan(low.Count));
    }

    [Test]
    public void ReplicasKeepTheTasksLimitsAndRuntimeTest()
    {
        var batches = Batches(8);
        batches[0].Tasks[0].Limits = new VerifyLimitsData { FuelBudget = 123 };

        var replicas = VerifySampler.Sample(batches, new VerifyOptionsData { SamplingRate = 1, BatchSize = 3 });
        var first = replicas.SelectMany(b => b.Tasks).Single(t => t.TaskIndex == 0);

        Assert.Multiple(() =>
        {
            Assert.That(replicas, Has.Count.EqualTo(3), "8 tasks in batches of 3");
            Assert.That(replicas.All(b => b.RuntimeId == "python-3.14.6"), Is.True);
            Assert.That(first.Limits!.FuelBudget, Is.EqualTo(123));
            Assert.That(ReferenceEquals(first, batches[0].Tasks[0]), Is.False, "a clone, not the primary's object");
        });
    }

    private static List<VerifyTaskBatchData> Batches(int tasks)
    {
        var all = Enumerable.Range(0, tasks).Select(index => new VerifyTaskData
        {
            TaskIndex = index,
            RuntimeId = "python-3.14.6",
            Sources = [new VerifySourceFileData { Name = "m.py", Content = $"print({index})" }],
            EntryPoint = "m.py"
        }).ToList();

        return VerifyTasksetPlanner.Plan(all, new VerifyOptionsData { BatchSize = 4 }).Batches;
    }
}
