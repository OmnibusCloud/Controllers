using System.Diagnostics;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Controller.AI.Corpus.Tests.Mock;

namespace OutWit.Controller.AI.Corpus.Tests.Load;

/// <summary>
/// Load on the pipeline (Category=Load): a synthetic WET shard of tens of thousands of
/// documents streams through the processor in bounded memory, twice to the same hash; and
/// several shards run on one process at once, as a node with CanRunInParallelOnClient does.
/// Not part of the regular run.
/// </summary>
[TestFixture]
[Category("Load")]
[NonParallelizable]
public sealed class CorpusShardLoadTests
{
    #region Constants

    private const int DOCUMENTS = 40_000;
    private const int WORDS_PER_DOCUMENT = 300;

    #endregion

    #region Fields

    private string m_directory = null!;
    private string m_shardPath = null!;
    private LanguageModel m_model = null!;

    #endregion

    #region Setup

    [OneTimeSetUp]
    public void Setup()
    {
        m_directory = Path.Combine(Path.GetTempPath(), $"witai_corpus_load_{Guid.NewGuid():N}");
        Directory.CreateDirectory(m_directory);
        m_shardPath = Path.Combine(m_directory, "synthetic.warc.wet.gz");

        var clock = Stopwatch.StartNew();
        WarcSynth.WriteWetShard(m_shardPath, DOCUMENTS, WORDS_PER_DOCUMENT, seed: 42);
        TestContext.Out.WriteLine($"shard: {DOCUMENTS} documents, {new FileInfo(m_shardPath).Length / (1024.0 * 1024.0):F1} MB gzip, written in {clock.Elapsed}");

        m_model = LanguageModel.Load(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "language-model-cc-2026-43-16.bin"));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (Directory.Exists(m_directory))
            Directory.Delete(m_directory, recursive: true);
    }

    #endregion

    #region Tests

    [Test]
    public void LargeShardStreamsInBoundedMemoryToTheSameHashTwiceTest()
    {
        var (first, firstTime, firstGrowth) = Run("first");
        var (second, secondTime, _) = Run("second");

        TestContext.Out.WriteLine($"first: {firstTime}, {DOCUMENTS / firstTime.TotalSeconds:F0} docs/s, {first.Stats.DocumentsOut} out, managed heap growth {firstGrowth / (1024.0 * 1024.0):F0} MB; second: {secondTime}");

        Assert.Multiple(() =>
        {
            Assert.That(first.Stats.RecordsIn, Is.EqualTo(DOCUMENTS));
            Assert.That(first.Stats.DocumentsOut, Is.GreaterThan(DOCUMENTS * 9 / 10), "synthetic English passes");
            Assert.That(first.ContentSha256, Is.EqualTo(second.ContentSha256));
            Assert.That(firstGrowth, Is.LessThan(512L * 1024 * 1024), "the shard streams: one record in memory at a time, not the shard");
        });
    }

    [Test]
    public void SeveralShardsOnOneProcessAgreeWithTheSingleRunTest()
    {
        var degree = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
        var (single, _, _) = Run("single");

        var clock = Stopwatch.StartNew();
        var hashes = new string[degree];
        Parallel.For(0, degree, i =>
        {
            var processor = new ShardProcessor(new CorpusPipelineData { Version = "load-v1" }, m_model);
            using var shard = File.OpenRead(m_shardPath);
            using var text = new FileStream(Path.Combine(m_directory, $"parallel-{i}.zst"), FileMode.Create);
            hashes[i] = processor.Process(shard, CorpusSourceFormat.Wet, text, Stream.Null).ContentSha256;
        });

        TestContext.Out.WriteLine($"{degree} shards in parallel: {clock.Elapsed}");
        Assert.That(hashes, Is.All.EqualTo(single.ContentSha256));
    }

    #endregion

    #region Tools

    private (ShardProduct Product, TimeSpan Elapsed, long HeapGrowth) Run(string name)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var peak = before;

        var processor = new ShardProcessor(new CorpusPipelineData { Version = "load-v1" }, m_model);
        using var shard = File.OpenRead(m_shardPath);
        using var text = new FileStream(Path.Combine(m_directory, $"{name}.zst"), FileMode.Create);
        using var signatures = new FileStream(Path.Combine(m_directory, $"{name}.sig"), FileMode.Create);

        using var sampler = new Timer(_ => peak = Math.Max(peak, GC.GetTotalMemory(false)), null, 0, 50);
        var clock = Stopwatch.StartNew();
        var product = processor.Process(shard, CorpusSourceFormat.Wet, text, signatures);
        clock.Stop();

        return (product, clock.Elapsed, peak - before);
    }

    #endregion
}
