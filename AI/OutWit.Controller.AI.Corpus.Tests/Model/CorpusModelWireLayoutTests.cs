using System.Reflection;
using MemoryPack;
using OutWit.Controller.AI.Corpus.Model;

namespace OutWit.Controller.AI.Corpus.Tests.Model;

/// <summary>
/// Freezes the wire layout of every MemoryPack DTO (the repo's append-only wire discipline):
/// member counts and contiguous [MemoryPackOrder] from 0. A failing case means a member was
/// inserted, reordered or removed — a wire break for a deployed fleet. New members go at the
/// END with the next order value; then the expected count here moves up.
/// </summary>
[TestFixture]
public sealed class CorpusModelWireLayoutTests
{
    [Test]
    public void PinListCoversAllMemoryPackableDtosTest()
    {
        var pinned = new[]
        {
            typeof(CorpusCountData), typeof(CorpusSourceData), typeof(CorpusQualityData), typeof(CorpusPipelineData),
            typeof(CorpusTaskData), typeof(CorpusShardStatsData), typeof(CorpusResultData), typeof(CorpusOptionsData),
            typeof(CorpusPreflightData)
        };

        var actual = typeof(CorpusTaskData).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().Name == "MemoryPackableAttribute"))
            .ToArray();

        Assert.That(actual, Is.EquivalentTo(pinned), "a MemoryPackable DTO exists that is not wire-layout-pinned (or a pinned one was removed)");
    }

    [TestCase(typeof(CorpusCountData), 2)]
    [TestCase(typeof(CorpusSourceData), 4)]
    [TestCase(typeof(CorpusQualityData), 10)]
    [TestCase(typeof(CorpusPipelineData), 8)]
    [TestCase(typeof(CorpusTaskData), 5)]
    [TestCase(typeof(CorpusShardStatsData), 9)]
    [TestCase(typeof(CorpusResultData), 11)]
    [TestCase(typeof(CorpusOptionsData), 4)]
    [TestCase(typeof(CorpusPreflightData), 6)]
    public void WireLayoutIsPinnedTest(Type type, int expectedMemberCount)
    {
        var ordered = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (property.Name, Order: property.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order))
            .Where(member => member.Order != null)
            .OrderBy(member => member.Order)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ordered, Has.Count.EqualTo(expectedMemberCount), $"{type.Name}: member count changed");
            Assert.That(ordered.Select(member => member.Order), Is.EqualTo(Enumerable.Range(0, expectedMemberCount)), $"{type.Name}: orders must be contiguous from 0");
        });
    }

    [Test]
    public void TaskRoundTripsThroughMemoryPackAndIsTest()
    {
        var task = new CorpusTaskData
        {
            TaskIndex = 7,
            Source = new CorpusSourceData { Url = "https://data.commoncrawl.org/crawl-data/x.warc.wet.gz", Format = CorpusSourceFormat.Wet, SizeBytes = 123 },
            Pipeline = new CorpusPipelineData { Version = "cc-text-v1", LanguageModelSha256 = "abc", TargetLanguages = ["eng", "deu"], Quality = new CorpusQualityData { MinWords = 50 } },
            AllowedOrigins = ["data.commoncrawl.org"],
            MaxShardBytes = 1L << 30
        };

        var bytes = MemoryPackSerializer.Serialize(task);
        var back = MemoryPackSerializer.Deserialize<CorpusTaskData>(bytes)!;

        Assert.Multiple(() =>
        {
            Assert.That(back.Is(task), Is.True);
            Assert.That(task.Clone().Is(task), Is.True);
            Assert.That(back.Pipeline!.TargetLanguages, Is.EqualTo(new[] { "eng", "deu" }));
            Assert.That(ReferenceEquals(task.Clone().Pipeline, task.Pipeline), Is.False);
        });
    }
}
