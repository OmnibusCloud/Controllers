using System.Diagnostics;
using System.Globalization;
using OutWit.Controller.AI.Smt.Delivery;
using OutWit.Controller.AI.Smt.Model;
using OutWit.Controller.AI.Smt.Solver;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.AI.Smt.Sandbox;

/// <summary>
/// The node benchmark of <c>Smt.SolvePackage</c>, a rate for the allocator: the reference set of
/// thirty small problems (<see cref="SmtBenchmarkProblems"/>), the same on every node, solved by
/// the pool at the node's degree as a package is, the set given enough times that each session
/// gets about <see cref="PROBLEMS_PER_WORKER"/> problems. A warm-up run first (the module is
/// compiled before it, outside any timing), then timed runs until the target duration; the rate is
/// problems a second of the median run. Every problem is held to the outcome and the fuel it has on
/// every machine, so a node that computes wrong cannot score, and the rate depends on nothing of the
/// machine but its speed and its degree. The same shape as Verify's benchmark.
/// </summary>
internal static class SmtBenchmark
{
    #region Constants

    /// <summary>The unit of the rate: problems of the reference set a second. Moves with every change of the set's cost.</summary>
    public const string UNIT = "smt-problem@ref-v1";

    /// <summary>The key of the degree the benchmark ran at, among its custom values (the pool's totals read it).</summary>
    public const string DEGREE_KEY = "degree";

    /// <summary>Problems of the set a session gets in one run, about.</summary>
    public const int PROBLEMS_PER_WORKER = 4;

    /// <summary>Runs discarded before timing.</summary>
    public const int WARMUP_RUNS = 1;

    /// <summary>Most warm-up runs before timing starts regardless of their duration.</summary>
    public const int MAX_WARMUP_RUNS = 2;

    /// <summary>Fewest timed runs.</summary>
    public const int MIN_RUNS = 3;

    /// <summary>Most timed runs.</summary>
    public const int MAX_RUNS = 5;

    /// <summary>Timed duration to aim for when the engine passes no minimum.</summary>
    public static readonly TimeSpan FALLBACK_TARGET = TimeSpan.FromSeconds(1.5);

    /// <summary>Wall-clock budget past which a slow node stops after its minimum runs.</summary>
    public static readonly TimeSpan SLOW_NODE_BUDGET = TimeSpan.FromSeconds(60);

    #endregion

    #region Functions

    /// <summary>
    /// Measures this node in the process's sandbox.
    /// </summary>
    /// <param name="options">Engine benchmark options; a degree of parallelism above 1 overrides the machine's (see <see cref="ResolveDegree"/>).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Rate in problems a second, with the run's details in <c>Custom</c>.</returns>
    /// <exception cref="InvalidOperationException">This process has no sandbox, not one session fits in the memory, or a problem of the set came to another outcome or another fuel.</exception>
    public static WitBenchmarkResult Measure(IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        var sandbox = SmtSandboxHost.Get(out var unavailable) ?? throw new InvalidOperationException(unavailable ?? "this process has no sandbox for the solver");
        return Measure(sandbox, ResolveDegree(options), options, cancellationToken);
    }

    /// <summary>
    /// Measures a sandbox at a degree.
    /// </summary>
    /// <param name="sandbox">The sandbox.</param>
    /// <param name="degree">The sessions at once.</param>
    /// <param name="options">Engine benchmark options: the target duration and the warm-up.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Rate in problems a second, with the run's details in <c>Custom</c>.</returns>
    /// <exception cref="InvalidOperationException">A problem of the set came to another outcome or another fuel, or the degree is 0.</exception>
    public static WitBenchmarkResult Measure(SmtSandbox sandbox, int degree, IWitBenchmarkOptions? options, CancellationToken cancellationToken)
    {
        if (degree < 1)
            throw new InvalidOperationException("not one session fits in this machine's memory under the default options");

        var target = options is { MinDuration.Ticks: > 0 } ? options.MinDuration : FALLBACK_TARGET;
        var warmupRuns = Math.Clamp(Math.Max(WARMUP_RUNS, options?.WarmupIterations ?? 0), WARMUP_RUNS, MAX_WARMUP_RUNS);
        var rounds = Rounds(degree);
        var problems = rounds * SmtBenchmarkProblems.All.Count;

        var warmup = TimeSpan.Zero;
        for (var index = 0; index < warmupRuns; index++)
            warmup += RunOnce(sandbox, degree, rounds, cancellationToken);

        var runs = new List<TimeSpan>();
        var timed = TimeSpan.Zero;
        while (runs.Count < MAX_RUNS)
        {
            var run = RunOnce(sandbox, degree, rounds, cancellationToken);
            runs.Add(run);
            timed += run;

            if (runs.Count >= MIN_RUNS && timed >= target)
                break;

            if (timed >= SLOW_NODE_BUDGET)
                break;
        }

        var custom = new Dictionary<string, string>
        {
            [DEGREE_KEY] = degree.ToString(CultureInfo.InvariantCulture),
            ["cores"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["problems"] = problems.ToString(CultureInfo.InvariantCulture),
            ["rounds"] = rounds.ToString(CultureInfo.InvariantCulture),
            ["platform"] = SmtResultRows.Platform,
            ["compile_ms"] = sandbox.CompileMs.ToString(CultureInfo.InvariantCulture)
        };

        return ToResult(runs, warmup, problems, custom);
    }

    /// <summary>
    /// The degree the benchmark runs at: the degree a package under the default options gets on
    /// this machine (cores, memory, the cap), the throughput a package actually gets, unless the
    /// options name a degree above 1. The engine's default options carry a degree of 1, which here
    /// means "unspecified": a single-session rate would rank a 12-thread desktop next to a
    /// 2-vCPU machine.
    /// </summary>
    /// <param name="options">Engine benchmark options, or null.</param>
    /// <returns>The degree; 0 when not one session fits.</returns>
    public static int ResolveDegree(IWitBenchmarkOptions? options)
    {
        return options?.DegreeOfParallelism is > 1
            ? options.DegreeOfParallelism.Value
            : SmtSandboxDegree.ForCurrentMachine(SmtSandboxDegree.SessionBytes(new SmtOptionsData())).Degree;
    }

    /// <summary>How many times one run gives the set, so that each session gets about <see cref="PROBLEMS_PER_WORKER"/> problems.</summary>
    /// <param name="degree">The sessions at once.</param>
    /// <returns>At least 1.</returns>
    public static int Rounds(int degree)
    {
        var count = SmtBenchmarkProblems.All.Count;
        return Math.Max(1, (PROBLEMS_PER_WORKER * Math.Max(1, degree) + count - 1) / count);
    }

    /// <summary>
    /// Scores timed runs: the rate is <paramref name="problems"/> over the median run.
    /// </summary>
    /// <param name="runs">Timed runs, at least one.</param>
    /// <param name="warmup">Total warm-up time.</param>
    /// <param name="problems">Problems a run.</param>
    /// <param name="custom">Extra facts to carry in the result.</param>
    /// <returns>The engine benchmark result.</returns>
    /// <exception cref="ArgumentException">No runs, or runs without measurable time.</exception>
    public static WitBenchmarkResult ToResult(IReadOnlyList<TimeSpan> runs, TimeSpan warmup, int problems, IReadOnlyDictionary<string, string>? custom = null)
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
            Rate = problems / median.TotalSeconds,
            Unit = UNIT,
            Elapsed = runs.Aggregate(TimeSpan.Zero, (sum, run) => sum + run),
            Iterations = runs.Count,
            Custom = bag
        };
    }

    /// <summary>The median of the runs (the mean of the two middle values for an even count).</summary>
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

    /// <summary>
    /// An outcome in a few words: the status and its code, or for an answered query <c>ok</c>, the
    /// verdict, the reason when there is one, for an enumeration the count and <c>complete</c>,
    /// <c>limit</c> or <c>incomplete</c>, for a proof what it came to.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The words.</returns>
    public static string Words(SmtSolveOutcome outcome)
    {
        if (outcome.Status != SmtSolveStatus.Ok)
            return outcome.Code.Length > 0 ? $"{SmtSolveWords.Of(outcome.Status)} {outcome.Code}" : SmtSolveWords.Of(outcome.Status);

        var words = $"ok {SmtSolveWords.Of(outcome.Verdict)}";
        if (outcome.Reason != SmtUnknownReason.None)
            words += $" {SmtSolveWords.Of(outcome.Reason)}";

        if (outcome.Query == SmtQuery.Enumerate)
            words += $" {outcome.Count.ToString(CultureInfo.InvariantCulture)} {(outcome.Complete ? "complete" : outcome.LimitReached ? "limit" : "incomplete")}";

        return outcome.Proof == SmtProofResult.None ? words : $"{words} {SmtSolveWords.Of(outcome.Proof)}";
    }

    /// <summary>
    /// Solves the set once, in its rounds, at a degree, and holds every outcome to its words and its
    /// fuel.
    /// </summary>
    /// <param name="sandbox">The sandbox.</param>
    /// <param name="degree">The sessions at once.</param>
    /// <param name="rounds">How many times the set is given.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The run's wall time.</returns>
    /// <exception cref="InvalidOperationException">A problem came to another outcome or another fuel.</exception>
    public static TimeSpan RunOnce(SmtSandbox sandbox, int degree, int rounds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var set = SmtBenchmarkProblems.All;
        var options = new SmtOptionsData();
        var clock = Stopwatch.StartNew();

        SmtWorkerPool.Run(set.Count * rounds, degree, index =>
        {
            var reference = set[index % set.Count];
            var outcome = SmtSolver.Solve(reference.Problem, options, sandbox, null, cancellationToken);
            var words = Words(outcome);
            if (words != reference.Expected || outcome.Fuel != reference.Fuel)
                throw new InvalidOperationException($"reference problem {reference.Id} came to '{words}' with fuel {outcome.Fuel}, not '{reference.Expected}' with fuel {reference.Fuel}");
        }, cancellationToken);

        clock.Stop();
        return clock.Elapsed;
    }

    private static string Seconds(TimeSpan span)
    {
        return span.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
    }

    #endregion
}
