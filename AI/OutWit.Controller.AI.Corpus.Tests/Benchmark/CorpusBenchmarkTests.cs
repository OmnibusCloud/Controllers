using OutWit.Controller.AI.Corpus.Tests.Mock;
using System.Diagnostics;
using System.Text;
using OutWit.Controller.AI.Corpus.Benchmark;
using OutWit.Controller.AI.Corpus.Language;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;

namespace OutWit.Controller.AI.Corpus.Tests.Benchmark;

/// <summary>
/// The node benchmark: the reference shard is the same bytes and the same product on every run
/// (the pinned hash is what a node is checked against), its documents exercise the drop paths,
/// the measurement scores at the requested degree, and the degree follows cores and memory.
/// </summary>
[TestFixture]
public sealed class CorpusBenchmarkTests
{
    #region Reference Tests

    [Test]
    public void ReferenceProductMatchesThePinnedHashTest()
    {
        Assert.That(CorpusBenchmark.ProcessReference(CorpusBenchmarkShard.Bytes), Is.EqualTo(CorpusBenchmark.EXPECTED_CONTENT_SHA256),
            "the generator or a stage changed: pin the new hash in CorpusBenchmark and bump its UNIT");
    }

    [Test]
    public void ReferenceDocumentsAreDeterministicAndMixedTest()
    {
        var first = CorpusBenchmarkShard.Documents();
        var second = CorpusBenchmarkShard.Documents();

        var processor = new ShardProcessor(CorpusBenchmark.PIPELINE, null);
        using var input = new MemoryStream(CorpusBenchmarkShard.Bytes);
        var product = processor.Process(input, CorpusSourceFormat.Wet, Stream.Null, Stream.Null);
        var stats = product.Stats.Build(0, 0, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Has.Count.EqualTo(CorpusBenchmarkShard.DOCUMENTS));
            Assert.That(stats.RecordsIn, Is.EqualTo(CorpusBenchmarkShard.DOCUMENTS));
            Assert.That(stats.DocumentsOut, Is.InRange(CorpusBenchmarkShard.DOCUMENTS * 7 / 10, CorpusBenchmarkShard.DOCUMENTS * 8 / 10), "the regular documents pass");
            Assert.That(stats.Drops.Select(d => d.Name), Does.Contain("min_words"), "the short documents drop");
            Assert.That(stats.Drops.Sum(d => d.Count), Is.EqualTo(CorpusBenchmarkShard.DOCUMENTS - stats.DocumentsOut));
            Assert.That(CorpusBenchmarkShard.TextBytes, Is.GreaterThan(500_000));
        });
    }

    #endregion

    #region Measure Tests

    [Test]
    public void MeasureScoresAtTheRequestedDegreeTest()
    {
        var result = CorpusBenchmark.Measure(new BenchmarkOptions(TimeSpan.FromMilliseconds(1), 2), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Unit, Is.EqualTo(CorpusBenchmark.UNIT));
            Assert.That(result.Rate, Is.GreaterThan(0));
            Assert.That(result.Iterations, Is.InRange(CorpusBenchmark.MIN_RUNS, CorpusBenchmark.MAX_RUNS));
            Assert.That(result.Custom!["degree"], Is.EqualTo("2"));
            Assert.That(result.Custom["documents"], Is.EqualTo((2 * CorpusBenchmarkShard.DOCUMENTS).ToString()));
        });
    }

    [Test]
    public void DefaultDegreeIsTheMachinesTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusBenchmark.ResolveDegree(null), Is.EqualTo(ShardDegree.ForCurrentMachine()));
            Assert.That(CorpusBenchmark.ResolveDegree(new BenchmarkOptions(TimeSpan.Zero, 1)), Is.EqualTo(ShardDegree.ForCurrentMachine()), "the engine's default degree 1 means unspecified");
            Assert.That(CorpusBenchmark.ResolveDegree(new BenchmarkOptions(TimeSpan.Zero, 3)), Is.EqualTo(3));
        });
    }

    [Test]
    public void CancelledMeasurementThrowsTest()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => CorpusBenchmark.Measure(new BenchmarkOptions(TimeSpan.Zero, 1), cancelled.Token));
    }

    [Test]
    public void MedianTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CorpusBenchmark.Median([TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)]), Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.That(CorpusBenchmark.Median([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)]), Is.EqualTo(TimeSpan.FromSeconds(2)));
            Assert.Throws<ArgumentException>(() => CorpusBenchmark.Median([]));
        });
    }

    #endregion

    #region Degree Tests

    [TestCase(4, 8L * 1024 * 1024 * 1024, 4)]
    [TestCase(32, 64L * 1024 * 1024 * 1024, 32)]
    [TestCase(16, 1L * 1024 * 1024 * 1024, 2)]
    [TestCase(64, 16L * 1024 * 1024 * 1024, 32)]
    [TestCase(8, 100L * 1024 * 1024, 1)]
    [TestCase(0, 0, 1)]
    [TestCase(6, 0, 6)]
    public void DegreeFollowsCoresAndMemoryTest(int cores, long memory, int expected)
    {
        Assert.That(ShardDegree.For(cores, memory), Is.EqualTo(expected));
    }

    [TestCase(64, 16L * 1024 * 1024 * 1024)]
    [TestCase(8, 8L * 1024 * 1024 * 1024)]
    [TestCase(4, 4L * 1024 * 1024 * 1024)]
    [TestCase(96, 512L * 1024 * 1024 * 1024)]
    public void L3ShardsAndPdfsShareOneMemoryBudgetTest(int cores, long memory)
    {
        // The audit's reading: 64 cores with 16 GB gave 64 shards (16 GB) and 8 PDFs (8 GB), each
        // counted against the whole. Now shards have half and PDFs a quarter.
        const long pdfMemory = 1L << 30;
        var shards = ShardDegree.For(cores, memory) * ShardDegree.MEMORY_PER_SHARD;
        var pdfs = ShardDegree.PdfSlots(pdfMemory, memory, cores) * pdfMemory;

        Assert.Multiple(() =>
        {
            Assert.That(shards, Is.LessThanOrEqualTo(memory / 2));
            Assert.That(pdfs, Is.LessThanOrEqualTo(Math.Max(pdfMemory, memory / 4)), "one PDF runs even where a quarter is less than its ceiling");
            Assert.That(shards + pdfs, Is.LessThanOrEqualTo(memory), "the two budgets together fit the machine");
            Assert.That(OutWit.Controller.AI.Corpus.Pdf.PdfSandbox.Slots(pdfMemory, memory, cores), Is.EqualTo(ShardDegree.PdfSlots(pdfMemory, memory, cores)), "one budget, asked in one place");
        });
    }

    #endregion

    #region Calibration

    /// <summary>
    /// Prints the numbers behind <see cref="CorpusBenchmark.REAL_MB_PER_REFERENCE_SHARD"/>: the
    /// single-threaded time of the reference shard (no language stage) and of the head of a
    /// real WET file through the full pipeline, per gzip megabyte (the head is fetched by range:
    /// see <see cref="CorpusCrawlHeads"/>). Run by hand on an idle machine.
    /// </summary>
    [Test]
    [Explicit("Calibration: run by hand on an idle machine and copy the constant")]
    public void PrintCalibrationTest()
    {
        var model = LanguageModel.Load(CorpusFixtures.PathOf(CorpusFixtures.PACK));
        var golden = File.ReadAllBytes(CorpusFixtures.PathOf(CorpusFixtures.CRAWL_WET));
        var reference = CorpusBenchmarkShard.Bytes;

        TimeSpan Time(Action action, int repeats)
        {
            action();
            var runs = new List<TimeSpan>();
            for (var i = 0; i < repeats; i++)
            {
                var clock = Stopwatch.StartNew();
                action();
                runs.Add(clock.Elapsed);
            }

            return CorpusBenchmark.Median(runs);
        }

        var referenceTime = Time(() => CorpusBenchmark.ProcessReference(reference), 7);
        var goldenTime = Time(() =>
        {
            var processor = new ShardProcessor(new CorpusPipelineData { Version = "calibration" }, model);
            using var input = new MemoryStream(golden);
            processor.Process(input, CorpusSourceFormat.Wet, Stream.Null, Stream.Null);
        }, 7);

        var goldenMb = golden.Length / (1024.0 * 1024.0);
        var secondsPerRealMb = goldenTime.TotalSeconds / goldenMb;
        var constant = referenceTime.TotalSeconds / secondsPerRealMb;

        var report = new StringBuilder()
            .AppendLine($"reference shard: {reference.Length / 1024.0:F0} KB gzip, {CorpusBenchmarkShard.TextBytes / 1024.0:F0} KB text, {referenceTime.TotalMilliseconds:F0} ms (no language stage)")
            .AppendLine($"real WET head:   {goldenMb:F2} MB gzip, {goldenTime.TotalMilliseconds:F0} ms (full pipeline) = {secondsPerRealMb:F3} s per real MB")
            .AppendLine($"REAL_MB_PER_REFERENCE_SHARD = {constant:F3}")
            .AppendLine($"reference hash: {CorpusBenchmark.ProcessReference(reference)}");
        TestContext.Out.WriteLine(report.ToString());
    }

    /// <summary>Prints this machine's benchmark at its own degree, and at degree 1 and 4 for comparison with small nodes.</summary>
    [Test]
    [Explicit("Prints this machine's score; run by hand")]
    public void PrintMachineBenchmarkTest()
    {
        foreach (var degree in new int?[] { null, 1, 4 })
        {
            var result = CorpusBenchmark.Measure(degree == null ? null : new BenchmarkOptions(TimeSpan.FromSeconds(2), degree), CancellationToken.None);
            TestContext.Out.WriteLine($"degree {result.Custom!["degree"],2}: {result.Rate:F2} {result.Unit} (median {result.Custom["median_s"]} s, runs {result.Custom["runs_s"]})");
        }
    }

    #endregion

    #region Types

    private sealed class BenchmarkOptions(TimeSpan minDuration, int? degreeOfParallelism) : OutWit.Engine.Interfaces.IWitBenchmarkOptions
    {
        public TimeSpan MinDuration { get; } = minDuration;

        public int? DegreeOfParallelism { get; } = degreeOfParallelism;

        public int WarmupIterations => 0;

        public string? DatasetPath => null;

        public bool Diagnostics => false;

        public object Clone()
        {
            return new BenchmarkOptions(MinDuration, DegreeOfParallelism);
        }
    }

    #endregion
}
