using System.Diagnostics;
using System.Globalization;
using OutWit.Controller.OpenFOAM.Extraction;
using OutWit.Engine.Data.Benchmark;
using OutWit.Engine.Interfaces;

namespace OutWit.Controller.OpenFOAM.Runtime;

/// <summary>
/// Node benchmark of the Foam.Run activity: pitzDaily from the kit's own
/// tutorials (the same case on every node, forever), meshed once, then
/// simpleFoam for a fixed number of iterations, serially - the reference is
/// one core's throughput on the kit's own binaries, which is what a variant
/// of a serial case gets and what a parallel case gets per rank. Rate is
/// runs per second against a unit string nodes are ranked within; the Custom
/// bag carries the last pressure residual as the marker that two nodes'
/// scores measured the same computation.
/// </summary>
/// <remarks>
/// The first run is an untimed warm-up (freshly unpacked binaries: cold page
/// cache, an antivirus scan on Windows), and the rate comes from the median
/// of several timed runs, so one stall does not move it while a machine that
/// is busy most of the time is still rated as busy - the CalculiX rule.
/// </remarks>
public static class FoamBenchmark
{
    #region Constants

    /// <summary>Ranking scale id; nodes are compared only within one unit string.</summary>
    public const string UNIT = "foam-simple@pitzDaily50-v1";

    /// <summary>Iterations of the reference simpleFoam run.</summary>
    public const int ITERATIONS = 50;

    /// <summary>Untimed runs before measuring (the engine's WarmupIterations may ask for more).</summary>
    public const int WARMUP_RUNS = 1;

    /// <summary>Upper bound on warm-up runs, whatever the options ask for.</summary>
    public const int MAX_WARMUP_RUNS = 3;

    /// <summary>Timed runs at least; the median of three already ignores one stall.</summary>
    public const int MIN_RUNS = 3;

    /// <summary>Timed runs at most, reached only when the runs are shorter than the target.</summary>
    public const int MAX_RUNS = 5;

    /// <summary>Target of the timed runs when the engine's options give none.</summary>
    public static readonly TimeSpan FALLBACK_TARGET = TimeSpan.FromSeconds(3);

    /// <summary>Timed-run budget on a slow node: once exceeded, no further run starts.</summary>
    public static readonly TimeSpan SLOW_NODE_BUDGET = TimeSpan.FromSeconds(90);

    private const string TUTORIAL = "incompressible/simpleFoam/pitzDaily";

    private const string SOLVER = "simpleFoam";

    private const string SCRATCH_ROOT = "outwit-foam-benchmark";

    #endregion

    #region Functions

    /// <summary>
    /// Runs the reference case: mesh once, warm-up, then timed runs, and scores the median.
    /// </summary>
    /// <param name="kit">The kit.</param>
    /// <param name="options">Engine benchmark options (target duration, warm-up count) or null for the defaults.</param>
    /// <param name="cancellationToken">Kills the running process tree when signaled.</param>
    /// <returns>The measured score.</returns>
    /// <exception cref="InvalidOperationException">The kit has no pitzDaily, or a reference run did not finish cleanly.</exception>
    public static async Task<WitBenchmarkResult> MeasureAsync(FoamKit kit, IWitBenchmarkOptions? options = null, CancellationToken cancellationToken = default)
    {
        var target = options is { MinDuration.Ticks: > 0 } ? options.MinDuration : FALLBACK_TARGET;
        var warmupRuns = Math.Clamp(Math.Max(WARMUP_RUNS, options?.WarmupIterations ?? 0), WARMUP_RUNS, MAX_WARMUP_RUNS);

        var tutorials = kit.Environment.Get("FOAM_TUTORIALS", kit.Root)
                        ?? Path.Combine(kit.Environment.Get("WM_PROJECT_DIR", kit.Root)!, "tutorials");
        var source = Path.Combine(tutorials, TUTORIAL.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(source))
            throw new InvalidOperationException($"The kit carries no {TUTORIAL} under {tutorials}.");

        var scratch = Path.Combine(Path.GetTempPath(), SCRATCH_ROOT, Guid.NewGuid().ToString("N"));
        var caseDirectory = Path.Combine(scratch, "pitzDaily");
        Directory.CreateDirectory(Path.Combine(scratch, "home"));
        Directory.CreateDirectory(Path.Combine(scratch, "tmp"));

        try
        {
            CopyTree(source, caseDirectory);
            FixIterations(caseDirectory);
            var environment = kit.EnvironmentFor(scratch);

            await StepAsync(kit, "blockMesh", caseDirectory, environment, cancellationToken);

            var warmup = TimeSpan.Zero;
            for (var index = 0; index < warmupRuns; index++)
                warmup += await SolveAsync(kit, caseDirectory, environment, cancellationToken);

            var runs = new List<TimeSpan>();
            var timed = TimeSpan.Zero;
            while (runs.Count < MAX_RUNS)
            {
                var run = await SolveAsync(kit, caseDirectory, environment, cancellationToken);
                runs.Add(run);
                timed += run;

                if (runs.Count >= MIN_RUNS && timed >= target)
                    break;

                if (timed >= SLOW_NODE_BUDGET)
                    break;
            }

            var facts = FoamLogReader.Read(Path.Combine(caseDirectory, $"log.{SOLVER}"));
            var custom = new Dictionary<string, string>
            {
                ["iterations"] = ITERATIONS.ToString(CultureInfo.InvariantCulture),
                ["cells"] = facts.CellCount.ToString(CultureInfo.InvariantCulture),
                ["platform"] = kit.Platform
            };

            var pressure = facts.FinalResiduals.FirstOrDefault(residual => residual.Key == "p");
            if (pressure.Key != null)
                custom["checksum"] = pressure.Value.ToString("E6", CultureInfo.InvariantCulture);

            return ToResult(runs, warmup, custom);
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch
            {
                // Scratch cleanup is best-effort; the OS temp reaper covers stragglers.
            }
        }
    }

    /// <summary>
    /// Scores the timed runs: rate = 1 / median run, elapsed = all timed runs,
    /// iterations = timed run count; the run times go into the Custom bag.
    /// </summary>
    /// <param name="runs">Wall time of every timed run (at least one).</param>
    /// <param name="warmup">Total wall time of the untimed warm-up runs.</param>
    /// <param name="custom">Extra metadata to carry; may be null.</param>
    /// <returns>The benchmark result in <see cref="UNIT"/>.</returns>
    /// <exception cref="ArgumentException">No run, or a run that took no measurable time.</exception>
    public static WitBenchmarkResult ToResult(IReadOnlyList<TimeSpan> runs, TimeSpan warmup, IReadOnlyDictionary<string, string>? custom = null)
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
            Rate = 1.0 / median.TotalSeconds,
            Unit = UNIT,
            Elapsed = runs.Aggregate(TimeSpan.Zero, (sum, run) => sum + run),
            Iterations = runs.Count,
            Custom = bag
        };
    }

    /// <summary>
    /// The median of the run times (the mean of the two middle ones for an even count).
    /// </summary>
    /// <param name="runs">Run times, at least one.</param>
    /// <returns>The median.</returns>
    public static TimeSpan Median(IReadOnlyList<TimeSpan> runs)
    {
        var sorted = runs.OrderBy(run => run).ToArray();
        var middle = sorted.Length / 2;

        return sorted.Length % 2 == 1
            ? sorted[middle]
            : TimeSpan.FromTicks((sorted[middle - 1].Ticks + sorted[middle].Ticks) / 2);
    }

    private static async Task<TimeSpan> SolveAsync(FoamKit kit, string caseDirectory, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        // Every run starts from the mesh and the initial fields alone, like a real run.
        foreach (var directory in FoamArtifactPacker.TimeDirectories(caseDirectory, Model.FoamArtifactTimes.All))
        {
            if (Path.GetFileName(directory) != "0")
                Directory.Delete(directory, recursive: true);
        }

        var stopwatch = Stopwatch.StartNew();
        await StepAsync(kit, SOLVER, caseDirectory, environment, cancellationToken);
        stopwatch.Stop();

        return stopwatch.Elapsed;
    }

    private static async Task StepAsync(FoamKit kit, string utility, string caseDirectory, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
    {
        var outcome = await FoamProcessRunner.RunAsync(
            kit.ExecutablePath(utility), [], caseDirectory, environment, Path.Combine(caseDirectory, $"log.{utility}"), cancellationToken);

        if (outcome.ExitCode != 0)
            throw new InvalidOperationException($"Reference {utility} exited {outcome.ExitCode}: {outcome.LogTail}");
    }

    private static void FixIterations(string caseDirectory)
    {
        // pitzDaily converges in about 280 iterations; the benchmark stops
        // at a fixed count so that every node does the same work. The
        // convergence controls are removed so no node stops early either.
        var controlDict = Path.Combine(caseDirectory, "system", "controlDict");
        var text = File.ReadAllText(controlDict);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^(\s*endTime\s+)\S+;", $"${{1}}{ITERATIONS};");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^(\s*writeInterval\s+)\S+;", $"${{1}}{ITERATIONS};");
        File.WriteAllText(controlDict, text);

        var fvSolution = Path.Combine(caseDirectory, "system", "fvSolution");
        var solution = File.ReadAllText(fvSolution);
        solution = System.Text.RegularExpressions.Regex.Replace(solution, @"residualControl\s*\{[^}]*\}", string.Empty);
        File.WriteAllText(fvSolution, solution);

        var orig = Path.Combine(caseDirectory, "0.orig");
        if (Directory.Exists(orig) && !Directory.Exists(Path.Combine(caseDirectory, "0")))
            CopyTree(orig, Path.Combine(caseDirectory, "0"));
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            File.Copy(file, destination, overwrite: true);
            File.SetAttributes(destination, FileAttributes.Normal);
        }
    }

    private static string Seconds(TimeSpan value)
    {
        return value.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);
    }

    #endregion
}
