using System.Globalization;
using OutWit.Math.Simulation.Model.Parareal;

namespace OutWit.Controller.Simulation.Parareal.Utils;

using Math = System.Math; // must be inside the namespace: OutWit.Math.* shadows System.Math

/// <summary>
/// Whole-job progress of a parareal solve, derived from the plan and the iteration state alone —
/// no clocks and no per-job memory — so the same state always reads as the same progress.
///
/// A solve is a sequence of waves: <c>K</c> iteration waves, then one snapshot wave. The waves run
/// their slabs in parallel, so each takes roughly the time of one slab, except the first iteration
/// (every node also factorizes the fine propagator) and the snapshot wave (every slab is recomputed
/// with snapshots on); both weigh about two ordinary waves. Progress is the weight of the waves done
/// over the weight of all waves.
///
/// <c>K</c> is not known up front. It is at most <c>Slabs + 1</c>: each iteration makes one more slab
/// exact, and the iteration after the last one changes nothing. Once two corrections are known, their
/// ratio projects when the correction reaches <c>Eps × Scale</c>, which can bring the estimate down.
/// The value reported after iteration <c>k</c> is the best estimate over iterations <c>1..k</c>, which
/// keeps it monotone when a projection is revised upwards.
/// </summary>
public static class PararealProgress
{
    #region Constants

    /// <summary>Weight of the first iteration wave, in ordinary waves (fine factorization on every node).</summary>
    public const double FIRST_WAVE_WEIGHT = 2.0;

    /// <summary>Weight of the snapshot wave, in ordinary waves (every slab recomputed with snapshots on).</summary>
    public const double SNAPSHOT_WAVE_WEIGHT = 2.0;

    #endregion

    #region Functions

    /// <summary>
    /// Progress once <see cref="PararealStateData.Round"/> iterations are done.
    /// </summary>
    /// <param name="plan">The slicing plan.</param>
    /// <param name="state">The state returned by <c>Parareal.Correct</c>.</param>
    /// <returns>A fraction in 0..1 that stays below 1 until the timeline is collected.</returns>
    public static double AfterIteration(PararealPlanData plan, PararealStateData state)
    {
        var iterations = Math.Min(state.Round, state.History.Count);
        var best = 0.0;

        for (var done = 1; done <= iterations; done++)
            best = Math.Max(best, Fraction(done, EstimateTotal(plan, state, done)));

        return best;
    }

    /// <summary>
    /// Progress when the loop is over and the snapshot wave starts — converged or out of budget.
    /// </summary>
    /// <param name="state">The last state of the loop.</param>
    /// <returns>The weight of the iteration waves done over that plus the snapshot wave.</returns>
    public static double BeforeSnapshotPass(PararealStateData state)
    {
        var iterations = Math.Max(state.Round, 0);
        return Fraction(iterations, iterations);
    }

    /// <summary>
    /// The convergence rule of <c>Parareal.IsConverged</c>, restated for progress.
    /// </summary>
    /// <param name="state">The state to test.</param>
    /// <returns><c>true</c> when the correction is at or below the target.</returns>
    public static bool IsConverged(PararealStateData state)
    {
        return state.Round > 0 && state.CorrectionNorm <= state.Eps * state.Scale;
    }

    private static int EstimateTotal(PararealPlanData plan, PararealStateData state, int done)
    {
        var correction = state.History[done - 1];
        var target = state.Eps * state.Scale;
        var ceiling = Math.Max(plan.Slabs + 1, done + 1);

        if (correction <= target)
            return done;

        if (done < 2 || target <= 0.0)
            return ceiling;

        var previous = state.History[done - 2];
        var ratio = previous > 0.0 ? correction / previous : double.NaN;
        if (!(ratio > 0.0 && ratio < 1.0))
            return ceiling;

        var remaining = Math.Ceiling(Math.Log(target / correction) / Math.Log(ratio));
        if (double.IsNaN(remaining))
            return ceiling;

        return (int)Math.Clamp(done + remaining, done + 1, ceiling);
    }

    private static double Fraction(int done, int total)
    {
        var doneWeight = WavesWeight(done);
        var totalWeight = WavesWeight(total) + SNAPSHOT_WAVE_WEIGHT;
        return Math.Clamp(doneWeight / totalWeight, 0.0, 1.0);
    }

    private static double WavesWeight(int iterations)
    {
        return iterations <= 0 ? 0.0 : FIRST_WAVE_WEIGHT + (iterations - 1);
    }

    #endregion

    #region Stages

    /// <summary>Stage text while the first iteration propagates every slab.</summary>
    /// <param name="plan">The slicing plan.</param>
    /// <returns>A short English description.</returns>
    public static string DescribeFirstIteration(PararealPlanData plan)
    {
        return string.Create(CultureInfo.InvariantCulture, $"iteration 1: propagating {plan.Slabs} time slabs");
    }

    /// <summary>Stage text after an iteration, with the correction and the target it is heading for.</summary>
    /// <param name="state">The state returned by <c>Parareal.Correct</c>.</param>
    /// <returns>A short English description.</returns>
    public static string DescribeIteration(PararealStateData state)
    {
        return IsConverged(state)
            ? string.Create(CultureInfo.InvariantCulture, $"iteration {state.Round}: converged, correction {state.CorrectionNorm:0.00E+00}")
            : string.Create(CultureInfo.InvariantCulture, $"iteration {state.Round}: correction {state.CorrectionNorm:0.00E+00}, target {state.Eps * state.Scale:0.00E+00}");
    }

    /// <summary>Stage text while the snapshot wave recomputes the slabs from the converged states.</summary>
    /// <param name="plan">The slicing plan.</param>
    /// <returns>A short English description.</returns>
    public static string DescribeSnapshotPass(PararealPlanData plan)
    {
        return string.Create(CultureInfo.InvariantCulture, $"snapshot pass: recomputing {plan.Slabs} time slabs");
    }

    /// <summary>Stage text once the timeline is collected.</summary>
    /// <returns>A short English description.</returns>
    public static string DescribeCollected()
    {
        return "timeline collected";
    }

    #endregion
}
