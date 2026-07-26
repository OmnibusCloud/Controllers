using System.Diagnostics;
using OutWit.Math.Simulation;
using OutWit.Math.Simulation.Numerics;
using OutWit.Engine.Data.Benchmark;

namespace OutWit.Controller.Simulation.Schwarz.Utils;

/// <summary>
/// The Schwarz.SolveSubdomain benchmark: the activity in
/// miniature — one Cholesky factorization + 20 back-substitutions of the
/// reference problem, deterministic and generated in-code. Reference size is
/// 40³ rather than 64³: CSparse's single-threaded factorization
/// measured ~5 minutes at 64³, violating the 5–15 s target by ~20× —
/// 40³ lands in the window while staying far out of L3.
/// </summary>
public static class SchwarzBenchmark
{
    #region Constants

    /// <summary>
    /// Nodes per axis of the scored run — 40³ = 64 000 unknowns. Part of the
    /// score's identity: change it and <see cref="UNIT"/> must change with it,
    /// or old and new numbers silently get compared.
    /// </summary>
    public const int REFERENCE_SIZE = 40;

    /// <summary>
    /// Back-substitutions timed after the factorization, chosen to mirror the
    /// activity's real shape: a node factorizes its subdomain once per job and
    /// then solves once per round, so a score dominated by the factorization
    /// would rank nodes on the wrong operation.
    /// </summary>
    public const int SOLVE_COUNT = 20;

    /// <summary>
    /// Comparability tag stamped on every result. Nodes are ranked against each
    /// other only within one unit string, so it names both the reference size
    /// and a version — bump the version whenever the timed work changes shape.
    /// </summary>
    public const string UNIT = "subdomain-solve@40^3-v1";

    private const int WARMUP_SIZE = 9;

    #endregion

    #region Functions

    /// <summary>
    /// Runs the benchmark: a throwaway pass at a tiny size to get the JIT out of
    /// the way, then the timed pass — assemble, factorize once, back-substitute
    /// <paramref name="solveCount"/> times. Assembly is inside the timed window
    /// on purpose; it is work the real activity also pays.
    /// </summary>
    /// <param name="gridSize">Nodes per axis; pass <see cref="REFERENCE_SIZE"/> for a score meant to be compared against other nodes.</param>
    /// <param name="solveCount">Back-substitutions to time; pass <see cref="SOLVE_COUNT"/> for a comparable score.</param>
    /// <returns>
    /// A result whose Rate is throughput in solves per second (higher is
    /// faster) tagged with <see cref="UNIT"/>, plus the wall clock, the
    /// iteration count, and a Custom bag holding the grid size, the unknown
    /// count and a checksum of the solution — the checksum is what makes two
    /// nodes' runs verifiable as the same computation, not just the same
    /// duration.
    /// </returns>
    public static WitBenchmarkResult Measure(int gridSize, int solveCount)
    {
        // Warm-up at a tiny size removes JIT noise from the timed pass.
        MeasureCore(WARMUP_SIZE, 2);
        return MeasureCore(gridSize, solveCount);
    }

    private static WitBenchmarkResult MeasureCore(int gridSize, int solveCount)
    {
        var model = SimulationBenchmarkProblem.CreateReference(gridSize);
        var system = FdOperatorAssembler.Assemble(model);

        var stopwatch = Stopwatch.StartNew();

        var factorization = new CholeskyFactorization(system.Matrix);
        var solution = system.Rhs;
        for (var i = 0; i < solveCount; i++)
            solution = factorization.Solve(system.Rhs);

        stopwatch.Stop();

        return new WitBenchmarkResult
        {
            Rate = solveCount / stopwatch.Elapsed.TotalSeconds,
            Unit = UNIT,
            Elapsed = stopwatch.Elapsed,
            Iterations = solveCount,
            Custom = new Dictionary<string, string>
            {
                ["gridSize"] = $"{gridSize}",
                ["unknowns"] = $"{system.UnknownCount}",
                ["checksum"] = $"{solution[0]:E9}"
            }
        };
    }

    #endregion
}
