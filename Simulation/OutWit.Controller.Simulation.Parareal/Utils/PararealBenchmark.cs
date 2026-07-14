using System.Diagnostics;
using OutWit.Controller.Simulation.Model;
using OutWit.Controller.Simulation.Model.Numerics;
using OutWit.Engine.Data.Benchmark;

namespace OutWit.Controller.Simulation.Parareal.Utils;

/// <summary>
/// The Parareal.Propagate benchmark (deep-dive B-1..B-6 applied verbatim): one
/// Crank–Nicolson factorization + 20 CN steps of the reference problem.
/// Reference size is 40³ for the same measured reason as Schwarz (64³
/// factorization ~5 min single-threaded — far beyond the 5–15 s B-5 target).
/// </summary>
public static class PararealBenchmark
{
    #region Constants

    public const int REFERENCE_SIZE = 40;

    public const int STEP_COUNT = 20;

    public const string UNIT = "slab-step@40^3-v1";

    private const double REFERENCE_TIME_STEP = 0.001;

    private const int WARMUP_SIZE = 9;

    #endregion

    #region Functions

    public static WitBenchmarkResult Measure(int gridSize, int stepCount)
    {
        MeasureCore(WARMUP_SIZE, 2);
        return MeasureCore(gridSize, stepCount);
    }

    private static WitBenchmarkResult MeasureCore(int gridSize, int stepCount)
    {
        var model = SimulationBenchmarkProblem.CreateReference(gridSize);
        var problem = FdOperatorAssembler.BuildProblem(model);

        var stopwatch = Stopwatch.StartNew();

        var stepper = new FdTransientStepper(problem, REFERENCE_TIME_STEP, theta: 0.5);
        var unknowns = new double[stepper.UnknownCount];
        unknowns = stepper.Step(unknowns, stepCount);

        stopwatch.Stop();

        return new WitBenchmarkResult
        {
            Rate = stepCount / stopwatch.Elapsed.TotalSeconds,
            Unit = UNIT,
            Elapsed = stopwatch.Elapsed,
            Iterations = stepCount,
            Custom = new Dictionary<string, string>
            {
                ["gridSize"] = $"{gridSize}",
                ["unknowns"] = $"{stepper.UnknownCount}",
                ["checksum"] = $"{unknowns[0]:E9}"
            }
        };
    }

    #endregion
}
