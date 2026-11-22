using System.Text;
using System.Text.Json;
using OutWit.Controller.AI.Corpus.Json;
using OutWit.Controller.AI.Corpus.Manifests;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Quality;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Shards;

/// <summary>
/// Memory a manifest could make one record or one document cost. A record within the cap, with
/// the quality thresholds opened by the manifest (the length, the ratios), was judged in full: the
/// heap rose by 658 MiB where a shard is budgeted 256 MiB. And a manifest of unknown root keys
/// cost about six times its size, every key kept to refuse one that came twice.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ShardQualityRulesCeilingTests
{
    #region Record Tests

    [Test]
    public void ARecordWithinTheCapCostsNoMoreWhenAManifestOpensTheThresholdsTest()
    {
        // The audit's record: 7 MiB of words in one WET record, the length and the ratios opened.
        var text = new StringBuilder(7 << 20);
        var random = new Random(5);
        while (text.Length < (7 << 20) - 16)
            text.Append((char)('a' + random.Next(26))).Append(' ');
        var shard = WarcSynth.Shard([WarcSynth.ConversionRecord("https://a.example/long", text.ToString())]);
        text.Clear();
        var pipeline = new CorpusPipelineData { Version = "m3", IdentifyLanguage = false, CleanLines = false, Sign = false, Quality = Opened() };

        var before = GC.GetAllocatedBytesForCurrentThread();
        var product = new ShardProcessor(pipeline, null).Process(new MemoryStream(shard), CorpusSourceFormat.Wet, new MemoryStream(), new MemoryStream());
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine($"allocated {allocated} bytes");

        Assert.Multiple(() =>
        {
            Assert.That(product.Stats.Build(0, 0, 0, 0).Drops.Select(drop => drop.Name), Is.EqualTo(new[] { "max_words" }), "past the controller's own length, whatever the manifest says");
            Assert.That(allocated, Is.LessThan(ShardDegree.MEMORY_PER_SHARD), "within what a shard is budgeted (it was 1,246 MiB allocated)");
        });
    }

    [Test]
    public void NoThresholdRaisesTheLengthADocumentIsJudgedInFullAtTest()
    {
        var packOfItsOwn = QualityRules.Spaced with { MaxWords = 5_000_000 };
        var lettersOfItsOwn = QualityRules.Unspaced with { MaxWords = 50_000_000 };

        Assert.Multiple(() =>
        {
            Assert.That(ShardQualityRules.MAX_WORDS, Is.EqualTo(QualityRules.Spaced.MaxWords), "the default is the ceiling: no honest default moves");
            Assert.That(ShardQualityRules.MAX_LETTERS, Is.EqualTo(QualityRules.Unspaced.MaxWords));
            Assert.That(ShardQualityRules.Resolve(QualityRules.Spaced, new CorpusQualityData { MaxWords = 5_000_000 }).MaxWords, Is.EqualTo(100_000), "a manifest");
            Assert.That(ShardQualityRules.Resolve(packOfItsOwn, null).MaxWords, Is.EqualTo(100_000), "a language pack's rule data");
            Assert.That(ShardQualityRules.Resolve(packOfItsOwn, new CorpusQualityData { MinWords = 10 }).MaxWords, Is.EqualTo(100_000));
            Assert.That(ShardQualityRules.Resolve(lettersOfItsOwn, null).MaxWords, Is.EqualTo(1_000_000), "letters of a script without spacing");
            Assert.That(ShardQualityRules.Resolve(QualityRules.Spaced, new CorpusQualityData { MaxWords = 500 }).MaxWords, Is.EqualTo(500), "a manifest may lower it");
            Assert.That(ShardQualityRules.Resolve(QualityRules.Spaced, null), Is.SameAs(QualityRules.Spaced), "the defaults as they are");
        });
    }

    #endregion

    #region Manifest Tests

    [Test]
    public void AManifestOfUnknownRootKeysIsRefusedAtTheBoundTest()
    {
        // The audit's manifest: nothing but unknown keys, "k0000001":0 and so on.
        var json = new StringBuilder(14 << 20).Append('{');
        for (var index = 0; index < 1_000_000; index++)
            json.Append('"').Append('k').Append(index.ToString("D7", System.Globalization.CultureInfo.InvariantCulture)).Append("\":0,");
        json.Append("\"pipeline\":{}}");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.ToString()));
        json.Clear();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var manifest = CorpusManifestParser.Parse(stream);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TestContext.Out.WriteLine($"document {stream.Length} bytes, allocated {allocated} bytes: {string.Join(" | ", manifest.Errors)}");

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Errors, Has.Count.EqualTo(1));
            Assert.That(manifest.Errors[0], Does.Contain($"more than {CorpusJsonStream.MAX_ROOT_KEYS} keys"));
            Assert.That(allocated, Is.LessThan(stream.Length / 4), "a bounded reading of a bounded number of keys (it was about six times the document)");
        });
    }

    [Test]
    public void TheRootHoldsAsManyKeysAsTheBoundAndNoMoreTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Read(CorpusJsonStream.MAX_ROOT_KEYS), Throws.Nothing);
            Assert.That(() => Read(CorpusJsonStream.MAX_ROOT_KEYS + 1), Throws.InstanceOf<JsonException>());
            Assert.That(CorpusJsonStream.MAX_ROOT_KEYS, Is.EqualTo(64));
        });
    }

    #endregion

    #region Tools

    /// <summary>Every threshold a manifest can set, opened as wide as it goes.</summary>
    private static CorpusQualityData Opened()
    {
        return new CorpusQualityData
        {
            MinWords = 1,
            MaxWords = int.MaxValue,
            MinMeanWordLength = 0.001,
            MaxMeanWordLength = 1e9,
            MaxSymbolToWordRatio = 1e9,
            MaxBulletLineRatio = 1e9,
            MaxEllipsisLineRatio = 1e9,
            MinAlphabeticWordRatio = 0.000001,
            MaxDuplicateLineRatio = 1e9,
            MaxDuplicateLineCharRatio = 1e9
        };
    }

    private static void Read(int keys)
    {
        var json = "{" + string.Join(",", Enumerable.Range(0, keys).Select(index => $"\"k{index}\":0")) + "}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        CorpusJsonStream.Read(stream, "items", (_, _) => { }, (_, _) => { });
    }

    #endregion
}
