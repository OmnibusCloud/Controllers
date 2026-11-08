using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Tests.Manifests;

/// <summary>The sampler: a function of the index and the rate, roughly the rate's share, cloned, reversed.</summary>
[TestFixture]
public sealed class CorpusSamplerTests
{
    [Test]
    public void RateZeroAndNullOptionsSampleNothingTest()
    {
        var tasks = Tasks(100);

        Assert.Multiple(() =>
        {
            Assert.That(CorpusSampler.Sample(tasks, null), Is.Empty);
            Assert.That(CorpusSampler.Sample(tasks, new CorpusOptionsData { SamplingRate = 0 }), Is.Empty);
            Assert.That(CorpusSampler.Sample(tasks, new CorpusOptionsData { SamplingRate = -1 }), Is.Empty);
        });
    }

    [Test]
    public void RateOneSamplesEverythingReversedAsClonesTest()
    {
        var tasks = Tasks(5);
        var replicas = CorpusSampler.Sample(tasks, new CorpusOptionsData { SamplingRate = 1 });

        Assert.Multiple(() =>
        {
            Assert.That(replicas.Select(t => t.TaskIndex), Is.EqualTo(new[] { 4, 3, 2, 1, 0 }));
            Assert.That(replicas.All(replica => !ReferenceEquals(replica, tasks[replica.TaskIndex])), Is.True);
            Assert.That(replicas.All(replica => replica.Is(tasks[replica.TaskIndex])), Is.True);
        });
    }

    [Test]
    public void ShareIsRoughlyTheRateAndDeterministicTest()
    {
        var tasks = Tasks(10_000);
        var first = CorpusSampler.Sample(tasks, new CorpusOptionsData { SamplingRate = 0.1 });
        var second = CorpusSampler.Sample(tasks, new CorpusOptionsData { SamplingRate = 0.1 });

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Count.InRange(850, 1150));
            Assert.That(first.Select(t => t.TaskIndex), Is.EqualTo(second.Select(t => t.TaskIndex)));
            Assert.That(first.Select(t => t.TaskIndex), Is.Ordered.Descending);
        });
    }

    [Test]
    public void SelectionIsMonotonicInTheRateTest()
    {
        var at5 = Enumerable.Range(0, 1000).Where(i => CorpusSampler.IsSelected(i, 0.05)).ToHashSet();
        var at20 = Enumerable.Range(0, 1000).Where(i => CorpusSampler.IsSelected(i, 0.20)).ToHashSet();

        Assert.That(at5.IsSubsetOf(at20), Is.True, "a shard sampled at a low rate is sampled at any higher rate");
    }

    private static List<CorpusTaskData> Tasks(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new CorpusTaskData { TaskIndex = i, Source = new CorpusSourceData { Url = $"https://h/{i}.warc.gz" }, Pipeline = new CorpusPipelineData { Version = "v" } })
            .ToList();
    }
}
