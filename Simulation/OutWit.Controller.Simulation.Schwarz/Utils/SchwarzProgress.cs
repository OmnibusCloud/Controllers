using System.Globalization;
using OutWit.Math.Simulation.Model.Schwarz;

namespace OutWit.Controller.Simulation.Schwarz.Utils;

using Math = System.Math; // must be inside the namespace: OutWit.Math.* shadows System.Math

/// <summary>
/// Whole-job progress of a Schwarz solve, derived from the round state alone — no clocks and no
/// per-job memory — so the same state always reads as the same progress.
///
/// The round loop dominates a solve, and its length is not known up front: it ends when the
/// residual falls to <c>Eps × InitialResidual</c>. Restricted Additive Schwarz contracts the
/// residual by a near-constant factor per round, so the logarithmic distance already travelled,
/// <c>f = ln(InitialResidual / best residual) / ln(1 / Eps)</c>, tracks the fraction of rounds done.
/// With <c>k</c> rounds done that estimates the total as <c>k / f</c>; the final pass is one more
/// wave, counted as one round, so progress is <c>k / (k / f + 1) = k·f / (k + f)</c>.
/// Using the best residual seen keeps the value monotone even if a round bumps the residual.
///
/// The first round also factorizes every subdomain and can take many rounds' worth of time; that
/// cost depends on the mesh and cannot be read from the state, so it is not modelled here.
/// </summary>
public static class SchwarzProgress
{
    #region Functions

    /// <summary>
    /// Progress once <see cref="SchwarzRoundData.Round"/> rounds are done.
    /// </summary>
    /// <param name="state">The state returned by <c>Schwarz.Advance</c>.</param>
    /// <returns>A fraction in 0..1 that stays below 1 until the job has assembled its field.</returns>
    public static double AfterRound(SchwarzRoundData state)
    {
        var rounds = state.Round;
        if (rounds <= 0)
            return 0.0;

        if (IsConverged(state))
            return BeforeFinalPass(state);

        var travelled = ConvergedFraction(state);
        if (travelled <= 0.0)
            return 0.0;

        return rounds * travelled / (rounds + travelled);
    }

    /// <summary>
    /// Progress when the loop is over and the final pass starts — converged or out of budget.
    /// </summary>
    /// <param name="state">The last state of the loop.</param>
    /// <returns><c>k / (k + 1)</c> for <c>k</c> rounds done.</returns>
    public static double BeforeFinalPass(SchwarzRoundData state)
    {
        var rounds = Math.Max(state.Round, 0);
        return (double)rounds / (rounds + 1);
    }

    /// <summary>
    /// How far the residual has travelled towards the convergence target on a logarithmic scale.
    /// </summary>
    /// <param name="state">A state with at least one round done.</param>
    /// <returns>0 before the first residual is known, 1 at the target, clamped to 0..1.</returns>
    public static double ConvergedFraction(SchwarzRoundData state)
    {
        if (state.Round <= 0)
            return 0.0;

        var best = state.History.Count > 0
            ? Math.Min(state.Residual, state.History.Min())
            : state.Residual;

        if (best <= 0.0 || state.InitialResidual <= 0.0)
            return 1.0;

        if (state.Eps >= 1.0)
            return 1.0;

        if (state.Eps <= 0.0)
            return 0.0;

        var travelled = Math.Log(state.InitialResidual / best) / Math.Log(1.0 / state.Eps);
        return double.IsNaN(travelled) ? 0.0 : Math.Clamp(travelled, 0.0, 1.0);
    }

    /// <summary>
    /// The convergence rule of <c>Schwarz.IsConverged</c>, restated for progress.
    /// </summary>
    /// <param name="state">The state to test.</param>
    /// <returns><c>true</c> when the residual is at or below the target.</returns>
    public static bool IsConverged(SchwarzRoundData state)
    {
        return state.Round > 0 && state.Residual <= state.Eps * state.InitialResidual;
    }

    #endregion

    #region Stages

    /// <summary>Stage text while the first round factorizes and solves every subdomain.</summary>
    /// <param name="plan">The decomposition.</param>
    /// <returns>A short English description.</returns>
    public static string DescribeFirstRound(SchwarzPlanData plan)
    {
        return string.Create(CultureInfo.InvariantCulture, $"round 1: factorizing {plan.Parts} subdomains");
    }

    /// <summary>Stage text after a round, with the residual and the target it is heading for.</summary>
    /// <param name="state">The state returned by <c>Schwarz.Advance</c>.</param>
    /// <returns>A short English description.</returns>
    public static string DescribeRound(SchwarzRoundData state)
    {
        return IsConverged(state)
            ? string.Create(CultureInfo.InvariantCulture, $"round {state.Round}: converged, residual {state.Residual:0.00E+00}")
            : string.Create(CultureInfo.InvariantCulture, $"round {state.Round}: residual {state.Residual:0.00E+00}, target {state.Eps * state.InitialResidual:0.00E+00}");
    }

    /// <summary>Stage text while the final pass collects the owned field slices.</summary>
    /// <param name="plan">The decomposition.</param>
    /// <returns>A short English description.</returns>
    public static string DescribeFinalPass(SchwarzPlanData plan)
    {
        return string.Create(CultureInfo.InvariantCulture, $"final pass: collecting the field from {plan.Parts} subdomains");
    }

    /// <summary>Stage text once the global field is assembled.</summary>
    /// <returns>A short English description.</returns>
    public static string DescribeAssembled()
    {
        return "field assembled";
    }

    #endregion
}
