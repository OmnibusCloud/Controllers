using System.Diagnostics;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Model.Numerics;
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

    public const int REFERENCE_SIZE = 40;

    public const int SOLVE_COUNT = 20;

    public const string UNIT = "subdomain-solve@40^3-v1";

    private const int WARMUP_SIZE = 9;

    #endregion

    #region Functions

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
