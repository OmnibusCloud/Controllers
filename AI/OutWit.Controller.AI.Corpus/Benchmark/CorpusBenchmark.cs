using System.Diagnostics;
using System.Globalization;
using OutWit.Controller.AI.Corpus.Model;
using OutWit.Controller.AI.Corpus.Shards;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Corpus.Benchmark;

/// <summary>
/// The node benchmark of <c>Corpus.ProcessShard</c>: the reference shard
/// (<see cref="CorpusBenchmarkShard"/>) processed by one worker per slot of the node's degree at
/// once, the way a node runs the shards of a batch; a warm-up first, then timed runs. The rate is
/// in megabytes of real Common Crawl WET per second at that degree — the unit the adapter's work
/// estimate uses (a shard's size in megabytes) — so the allocator's makespans are seconds. The
/// reference product carries its expected hash: a node that computes a different product cannot
/// score. The language stage is off (the model arrives with a job, not with the controller); the
/// calibration constant folds its share back in.
/// </summary>
public static class CorpusBenchmark
{
    #region Constants

    /// <summary>Bumped with every change to the reference workload or its calibration.</summary>
    public const string UNIT = "corpus-wet-mb@synthetic-v1";

    /// <summary>
    /// SHA-256 of the reference product (the uncompressed JSON lines of the documents that pass).
    /// Pinned by a test; a change to the generator or to any stage moves it, and then
    /// <see cref="UNIT"/> moves too.
    /// </summary>
    public const string EXPECTED_CONTENT_SHA256 = "63e868cac3b633c130a9175f75bc57203dca1b60ae870c734bb1bd4c46b398aa";

    /// <summary>
    /// Megabytes of real Common Crawl WET (gzip, full pipeline with the language stage) that cost
    /// the same CPU as one reference shard without it. Measured single-threaded on the golden
    /// WET fixture against the reference shard; see the calibration test (2026-11-08, Ryzen 9
    /// 5950X, three runs 0.377-0.406: the reference shard ~285 ms without the language stage,
    /// the golden WET fixture ~0.75 s per gzip megabyte with it).
    /// </summary>
    public const double REAL_MB_PER_REFERENCE_SHARD = 0.39;

    /// <summary>Reference runs discarded before timing.</summary>
    public const int WARMUP_RUNS = 1;

    /// <summary>Fewest timed runs.</summary>
    public const int MIN_RUNS = 3;

    /// <summary>Most timed runs.</summary>
    public const int MAX_RUNS = 5;

    /// <summary>Timed duration to aim for when the engine passes no minimum.</summary>
    public static readonly TimeSpan FALLBACK_TARGET = TimeSpan.FromSeconds(1.5);

    /// <summary>Wall-clock budget past which a slow node stops after its minimum runs.</summary>
    public static readonly TimeSpan SLOW_NODE_BUDGET = TimeSpan.FromSeconds(60);

    /// <summary>The pipeline the reference shard runs through.</summary>
    public static readonly CorpusPipelineData PIPELINE = new() { Version = "benchmark", IdentifyLanguage = false, FilterQuality = true, Sign = true };

    #endregion

    #region Functions

    /// <summary>Measures this node.</summary>
    /// <param name="options">Engine benchmark options; a degree above 1 overrides the machine's.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The rate in real-WET megabytes per second, the run details in <c>Custom</c>.</returns>
    /// <exception cref="InvalidOperationException">The reference product's hash is wrong.</exception>
    public static WitBenchmarkResult Measure(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        var target = options is { MinDuration.Ticks: > 0 } ? options.MinDuration : FALLBACK_TARGET;
        var degree = ResolveDegree(options);
        var shard = CorpusBenchmarkShard.Bytes;

        var warmup = TimeSpan.Zero;
        for (var index = 0; index < WARMUP_RUNS; index++)
            warmup += RunOnce(shard, degree, cancellationToken);

        var runs = new List<TimeSpan>();
        var timed = TimeSpan.Zero;
        while (runs.Count < MAX_RUNS)
        {
            var run = RunOnce(shard, degree, cancellationToken);
            runs.Add(run);
            timed += run;

            if (runs.Count >= MIN_RUNS && timed >= target)
                break;
            if (timed >= SLOW_NODE_BUDGET)
                break;
        }

        var median = Median(runs);
        return new WitBenchmarkResult
        {
            Rate = degree * REAL_MB_PER_REFERENCE_SHARD / median.TotalSeconds,
            Unit = UNIT,
            Elapsed = timed,
            Iterations = runs.Count,
            Custom = new Dictionary<string, string>
            {
                ["degree"] = degree.ToString(CultureInfo.InvariantCulture),
                ["cores"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
                ["documents"] = (degree * CorpusBenchmarkShard.DOCUMENTS).ToString(CultureInfo.InvariantCulture),
                ["median_s"] = Seconds(median),
                ["runs_s"] = string.Join(";", runs.Select(Seconds)),
                ["warmup_s"] = Seconds(warmup)
            }
        };
    }

    /// <summary>The degree the benchmark runs at: the node's shard degree unless the options name one above 1 (the engine's default 1 means "unspecified").</summary>
    /// <param name="options">Engine benchmark options, or null.</param>
    /// <returns>At least 1.</returns>
    public static int ResolveDegree(IWitBenchmarkOptions? options)
    {
        return options?.DegreeOfParallelism is > 1 ? options.DegreeOfParallelism.Value : ShardDegree.ForCurrentMachine();
    }

    /// <summary>Processes the reference shard once and returns its product hash.</summary>
    /// <param name="shard">The reference shard bytes.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The product's content hash.</returns>
    public static string ProcessReference(byte[] shard, CancellationToken cancellationToken = default)
    {
        var processor = new ShardProcessor(PIPELINE, null);
        using var input = new MemoryStream(shard, writable: false);
        return processor.Process(input, CorpusSourceFormat.Wet, Stream.Null, Stream.Null, cancellationToken).ContentSha256;
    }

    /// <summary>The median of the runs.</summary>
    /// <param name="runs">At least one run.</param>
    /// <returns>The median (the mean of the middle two for an even count).</returns>
    public static TimeSpan Median(IReadOnlyList<TimeSpan> runs)
    {
        if (runs.Count == 0)
            throw new ArgumentException("the benchmark needs at least one timed run", nameof(runs));

        var sorted = runs.OrderBy(run => run).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : TimeSpan.FromTicks((sorted[middle - 1].Ticks + sorted[middle].Ticks) / 2);
    }

    private static TimeSpan RunOnce(byte[] shard, int degree, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var failures = new string?[degree];
        var threads = new Thread[degree];
        var clock = Stopwatch.StartNew();
        for (var worker = 0; worker < degree; worker++)
        {
            var slot = worker;
            threads[slot] = new Thread(() =>
            {
                try
                {
                    var hash = ProcessReference(shard, cancellationToken);
                    if (!string.Equals(hash, EXPECTED_CONTENT_SHA256, StringComparison.Ordinal))
                        failures[slot] = $"the reference product hashes to {hash}, not {EXPECTED_CONTENT_SHA256}";
                }
                catch (Exception exception)
                {
                    failures[slot] = exception.Message;
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = $"corpus-benchmark-{slot}"
            };
            threads[slot].Start();
        }

        foreach (var thread in threads)
            thread.Join();
        clock.Stop();

        cancellationToken.ThrowIfCancellationRequested();
        var failure = failures.FirstOrDefault(message => message != null);
        if (failure != null)
            throw new InvalidOperationException(failure);

        return clock.Elapsed;
    }

    private static string Seconds(TimeSpan span)
    {
        return span.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
    }

    #endregion
}
