using System.Diagnostics;
using System.Globalization;
using OutWit.Controller.AI.Verify.Model;
using OutWit.Controller.AI.Verify.Runtimes;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Verify.Sandbox;

/// <summary>
/// The node benchmark of <c>Verify.ExecuteBatch</c>: a fixed reference workload — one CPU-bound
/// Python program and one QuickJS program, each replicated to fill the node's degree four times
/// over — run as the two batches the planner would make: a warm-up first (module compilation),
/// then timed runs until the target duration. The rate is tasks per second of the median run at
/// the node's degree: the parallel throughput is the honest rate, because a batch is what a node
/// receives. The reference programs carry their expected output, so a node that computes wrong
/// cannot score. The same shape as CalculiX's <c>CcxBenchmark</c>.
/// </summary>
public static class VerifyBenchmark
{
    #region Constants

    /// <summary>Bumped with every change to the reference workload's cost (v2: the bytecode stdlib layer).</summary>
    public const string UNIT = "verify-task@mixed-v2";

    /// <summary>Reference tasks per worker thread, per runtime.</summary>
    public const int TASKS_PER_WORKER = 4;

    public const int WARMUP_RUNS = 1;

    public const int MAX_WARMUP_RUNS = 2;

    public const int MIN_RUNS = 3;

    public const int MAX_RUNS = 5;

    public static readonly TimeSpan FALLBACK_TARGET = TimeSpan.FromSeconds(1.5);

    public static readonly TimeSpan SLOW_NODE_BUDGET = TimeSpan.FromSeconds(60);

    private const string PYTHON_ENTRY = "bench.py";
    private const string PYTHON_PROGRAM = "s=0\nfor i in range(500000): s+=i*i%7\nprint(s)\n";
    private const string PYTHON_EXPECTED = "999999\n";

    private const string JS_ENTRY = "bench.js";
    private const string JS_PROGRAM = "let s=0;for(let i=0;i<3000000;i++)s+=i*i%7;console.log(s);\n";
    private const string JS_EXPECTED = "5999999\n";

    private const int STDERR_EXCERPT = 200;

    #endregion

    #region Functions

    /// <summary>
    /// Measures this node: warm-up runs, then timed runs until the options' minimum duration
    /// (at least <see cref="MIN_RUNS"/>, at most <see cref="MAX_RUNS"/>, within the slow-node budget).
    /// </summary>
    /// <param name="sandbox">The node's sandbox.</param>
    /// <param name="runtimesRoot">The language runtime root.</param>
    /// <param name="options">Engine benchmark options; a degree of parallelism above 1 overrides the machine's (see <see cref="ResolveDegree"/>).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Rate in tasks per second with the run details in <c>Custom</c>.</returns>
    /// <exception cref="InvalidOperationException">A reference task did not pass (a runtime is missing or the sandbox computes wrong).</exception>
    public static WitBenchmarkResult Measure(VerifyWasmSandbox sandbox, string runtimesRoot, IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        var target = options is { MinDuration.Ticks: > 0 } ? options.MinDuration : FALLBACK_TARGET;
        var warmupRuns = Math.Clamp(Math.Max(WARMUP_RUNS, options?.WarmupIterations ?? 0), WARMUP_RUNS, MAX_WARMUP_RUNS);
        var degree = ResolveDegree(options);

        var batches = ReferenceBatches(degree);
        var taskCount = batches.Sum(batch => batch.Tasks.Count);

        var warmup = TimeSpan.Zero;
        for (var index = 0; index < warmupRuns; index++)
            warmup += RunOnce(sandbox, runtimesRoot, batches, cancellationToken);

        var runs = new List<TimeSpan>();
        var timed = TimeSpan.Zero;
        while (runs.Count < MAX_RUNS)
        {
            var run = RunOnce(sandbox, runtimesRoot, batches, cancellationToken);
            runs.Add(run);
            timed += run;

            if (runs.Count >= MIN_RUNS && timed >= target)
                break;

            if (timed >= SLOW_NODE_BUDGET)
                break;
        }

        var custom = new Dictionary<string, string>
        {
            ["degree"] = degree.ToString(CultureInfo.InvariantCulture),
            ["cores"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["tasks"] = taskCount.ToString(CultureInfo.InvariantCulture),
            ["runtime_target"] = VerifyRuntimeDiagnosticsBuilder.RuntimeTarget(),
            ["wasmtime"] = typeof(Wasmtime.Engine).Assembly.GetName().Version?.ToString() ?? "unknown"
        };

        return ToResult(runs, warmup, taskCount, custom);
    }

    /// <summary>
    /// The degree the benchmark runs at: the machine's batch degree (cores, memory, the default
    /// cap) — the throughput a batch actually gets — unless the options name a degree above 1.
    /// The engine's default options carry <c>DegreeOfParallelism = 1</c>, which here means
    /// "unspecified": a single-sandbox rate would rank a 12-thread desktop next to a 2-vCPU VM.
    /// </summary>
    /// <param name="options">Engine benchmark options, or null.</param>
    /// <returns>The degree, at least 1.</returns>
    public static int ResolveDegree(IWitBenchmarkOptions? options)
    {
        return options?.DegreeOfParallelism is > 1
            ? options.DegreeOfParallelism.Value
            : VerifySandboxDegree.ForCurrentMachine(VerifySandboxDefaults.DEFAULT_MEMORY_BYTES);
    }

    /// <summary>
    /// The reference workload for a degree: a Python batch and a QuickJS batch of
    /// <c>degree × TASKS_PER_WORKER</c> tasks each, every task with its expected output.
    /// </summary>
    /// <param name="degree">The degree the batches will run at (also their MaxDegree).</param>
    /// <returns>Two batches, Python first.</returns>
    public static IReadOnlyList<VerifyTaskBatchData> ReferenceBatches(int degree)
    {
        var workers = Math.Max(1, degree);
        var count = workers * TASKS_PER_WORKER;

        return
        [
            Batch(VerifyRuntimeCatalog.PYTHON_3_14_6, PYTHON_ENTRY, PYTHON_PROGRAM, PYTHON_EXPECTED, count, workers, 0),
            Batch(VerifyRuntimeCatalog.QUICKJS_0_15_1, JS_ENTRY, JS_PROGRAM, JS_EXPECTED, count, workers, count)
        ];
    }

    /// <summary>
    /// Scores timed runs: the rate is <paramref name="taskCount"/> over the median run.
    /// </summary>
    /// <param name="runs">Timed runs, at least one.</param>
    /// <param name="warmup">Total warm-up time.</param>
    /// <param name="taskCount">Tasks per run.</param>
    /// <param name="custom">Extra facts to carry in the result.</param>
    /// <returns>The engine benchmark result.</returns>
    /// <exception cref="ArgumentException">No runs, or runs without measurable time.</exception>
    public static WitBenchmarkResult ToResult(IReadOnlyList<TimeSpan> runs, TimeSpan warmup, int taskCount, IReadOnlyDictionary<string, string>? custom = null)
    {
        if (runs.Count == 0)
            throw new ArgumentException("the benchmark needs at least one timed run", nameof(runs));

        var median = Median(runs);
        if (median <= TimeSpan.Zero)
            throw new ArgumentException("the timed runs took no measurable time", nameof(runs));

        var bag = custom?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<string, string>();
        bag["median_s"] = Seconds(median);
        bag["runs_s"] = string.Join(";", runs.Select(Seconds));
        bag["warmup_s"] = Seconds(warmup);

        return new WitBenchmarkResult
        {
            Rate = taskCount / median.TotalSeconds,
            Unit = UNIT,
            Elapsed = runs.Aggregate(TimeSpan.Zero, (sum, run) => sum + run),
            Iterations = runs.Count,
            Custom = bag
        };
    }

    /// <summary>
    /// The median of the runs (the mean of the two middle values for an even count).
    /// </summary>
    /// <param name="runs">Timed runs, at least one.</param>
    /// <returns>The median.</returns>
    public static TimeSpan Median(IReadOnlyList<TimeSpan> runs)
    {
        var sorted = runs.OrderBy(run => run).ToArray();
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1
            ? sorted[middle]
            : TimeSpan.FromTicks((sorted[middle - 1].Ticks + sorted[middle].Ticks) / 2);
    }

    private static TimeSpan RunOnce(VerifyWasmSandbox sandbox, string runtimesRoot, IReadOnlyList<VerifyTaskBatchData> batches, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        foreach (var batch in batches)
        {
            var result = VerifyBatchExecutor.Execute(sandbox, runtimesRoot, batch);
            var wrong = result.Results.FirstOrDefault(r => r.Verdict != VerifyVerdict.Pass);
            if (wrong != null)
            {
                var excerpt = wrong.Stderr.Length > STDERR_EXCERPT ? wrong.Stderr[..STDERR_EXCERPT] : wrong.Stderr;
                throw new InvalidOperationException($"reference task {wrong.TaskIndex} ({batch.RuntimeId}) ended {wrong.Verdict}: {excerpt.Trim()}");
            }
        }

        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static VerifyTaskBatchData Batch(string runtimeId, string entry, string program, string expected, int count, int degree, int firstIndex)
    {
        var tasks = new List<VerifyTaskData>(count);
        for (var offset = 0; offset < count; offset++)
        {
            tasks.Add(new VerifyTaskData
            {
                TaskIndex = firstIndex + offset,
                RuntimeId = runtimeId,
                EntryPoint = entry,
                Sources = [new VerifySourceFileData { Name = entry, Content = program }],
                Suite = new VerifySuiteData
                {
                    Cases = [new VerifySuiteCaseData { ExpectedStdout = expected, ExpectedExitCode = 0 }]
                }
            });
        }

        return new VerifyTaskBatchData
        {
            RuntimeId = runtimeId,
            Tasks = tasks,
            MaxDegree = degree
        };
    }

    private static string Seconds(TimeSpan span)
    {
        return span.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
    }

    #endregion
}
