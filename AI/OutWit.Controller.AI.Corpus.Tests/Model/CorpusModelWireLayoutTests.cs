using System.Reflection;
using System.Text.Json;
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
    [TestCase(typeof(CorpusPipelineData), 9)]
    [TestCase(typeof(CorpusTaskData), 5)]
    [TestCase(typeof(CorpusShardStatsData), 10)]
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
    public void PublishedLayoutBaselineMatchesTheModelVersionTest()
    {
        // The baseline says which member counts the published Model version carries. A count that
        // differs from it means a wire change: update the baseline AND bump the Model's <Version>
        // in the same commit, then publish the Model before the controller depends on it.
        var root = SolutionRoot();
        if (root == null)
            Assert.Ignore("Solution root not found");

        using var baseline = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "AI", "OutWit.Controller.AI.Corpus.Tests", "Model", "wire-layout.baseline.json")));
        var expectedVersion = baseline.RootElement.GetProperty("modelVersion").GetString();
        var csproj = File.ReadAllText(Path.Combine(root, "AI", "OutWit.Controller.AI.Corpus.Model", "OutWit.Controller.AI.Corpus.Model.csproj"));
        var actualVersion = System.Text.RegularExpressions.Regex.Match(csproj, @"<Version>([^<]+)</Version>").Groups[1].Value;

        var counts = typeof(CorpusTaskData).Assembly
            .GetTypes()
            .Where(type => type.GetCustomAttributes(inherit: false).Any(attribute => attribute.GetType().Name == "MemoryPackableAttribute"))
            .ToDictionary(type => type.Name, type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Count(property => property.GetCustomAttribute<MemoryPackOrderAttribute>() != null));
        var expectedCounts = baseline.RootElement.GetProperty("members").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetInt32());

        Assert.Multiple(() =>
        {
            Assert.That(counts, Is.EquivalentTo(expectedCounts), "a DTO's wire layout differs from the published baseline: update wire-layout.baseline.json and bump the Model <Version> in the same commit");
            Assert.That(actualVersion, Is.EqualTo(expectedVersion), "the Model csproj version and the baseline's modelVersion must agree");
        });
    }

    private static string? SolutionRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "OutWit.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
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
