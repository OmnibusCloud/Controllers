using System.Diagnostics;
using OutWit.Math.Simulation;
using OutWit.Math.Simulation.Numerics;
using OutWit.Engine.Data.Benchmark;

namespace OutWit.Controller.Simulation.Parareal.Utils;

/// <summary>
/// The Parareal.Propagate benchmark: one
/// Crank–Nicolson factorization + 20 CN steps of the reference problem.
/// Reference size is 40³ for the same measured reason as Schwarz (64³
/// factorization ~5 min single-threaded — far beyond the 5–15 s target).
/// </summary>
public static class PararealBenchmark
{
    #region Constants

    /// <summary>
    /// Nodes per axis of the scored run — 40³ = 64 000 unknowns. Part of the
    /// score's identity: change it and <see cref="UNIT"/> must change with it,
    /// or old and new numbers silently get compared.
    /// </summary>
    public const int REFERENCE_SIZE = 40;

    /// <summary>
    /// Crank–Nicolson steps timed after the factorization, chosen to mirror the
    /// activity's real shape: a node factorizes the stepper once and then walks
    /// many steps per slab, so a score dominated by the factorization would rank
    /// nodes on the wrong operation.
    /// </summary>
    public const int STEP_COUNT = 20;

    /// <summary>
    /// Comparability tag stamped on every result. Nodes are ranked against each
    /// other only within one unit string, so it names both the reference size
    /// and a version — bump the version whenever the timed work changes shape.
    /// Deliberately distinct from the Schwarz unit: a subdomain solve and a slab
    /// step are different work and must never be ranked on one scale.
    /// </summary>
    public const string UNIT = "slab-step@40^3-v1";

    private const double REFERENCE_TIME_STEP = 0.001;

    private const int WARMUP_SIZE = 9;

    #endregion

    #region Functions

    /// <summary>
    /// Runs the benchmark: a throwaway pass at a tiny size to get the JIT out of
    /// the way, then the timed pass — assemble, build the Crank–Nicolson stepper
    /// (which factorizes once) and walk <paramref name="stepCount"/> steps from a
    /// zero field. Assembly and factorization sit inside the timed window on
    /// purpose; they are work the real activity also pays.
    /// </summary>
    /// <param name="gridSize">Nodes per axis; pass <see cref="REFERENCE_SIZE"/> for a score meant to be compared against other nodes.</param>
    /// <param name="stepCount">Time steps to walk; pass <see cref="STEP_COUNT"/> for a comparable score.</param>
    /// <returns>
    /// A result whose Rate is throughput in steps per second (higher is faster)
    /// tagged with <see cref="UNIT"/>, plus the wall clock, the iteration count,
    /// and a Custom bag holding the grid size, the unknown count and a checksum
    /// of the final field — the checksum is what makes two nodes' runs
    /// verifiable as the same computation, not just the same duration.
    /// </returns>
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
