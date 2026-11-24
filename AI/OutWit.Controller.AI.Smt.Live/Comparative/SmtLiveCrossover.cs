using OutWit.Controller.AI.Smt.Model.Problems;

namespace OutWit.Controller.AI.Smt.Live.Comparative;

/// <summary>
/// The crossover of the comparative test: the size of a set above which the fleet finishes it
/// first. Each side's time is taken as a fixed cost and a cost a problem, the same for every
/// problem of a set like the one measured, both read from measurements only:
/// <list type="bullet">
/// <item>this machine: the fixed cost is its start-up (the tool's start to the sandbox ready: the
/// module's compile), measured in the local run; the cost a problem is the rest of its wall time
/// over its problems;</item>
/// <item>the fleet: the straight line through two fleet runs measured end to end, a small set's
/// (whose time is almost all fixed cost) and the compared set's; its slope is the cost a problem,
/// its value at no problem the fixed cost.</item>
/// </list>
/// With <c>T(n) = fixed + n x cost</c> on each side, the fleet finishes first where
/// <c>T_local(n) &gt; T_fleet(n)</c>. A crossover above the most rows a set may have
/// (<see cref="SmtProblemSetLimits.MAX_ROWS"/>) is a size no run can have: this machine finishes
/// first at every size.
/// </summary>
/// <param name="Kind">What the lines say.</param>
/// <param name="Problems">The crossover in problems, where there is one.</param>
/// <param name="LocalFixed">This machine's fixed cost, seconds.</param>
/// <param name="LocalPerProblem">This machine's cost a problem, seconds.</param>
/// <param name="FleetFixed">The fleet's fixed cost, seconds, when it could be read.</param>
/// <param name="FleetPerProblem">The fleet's cost a problem, seconds, when it could be read.</param>
/// <param name="Reason">Why there is no crossover, when <paramref name="Kind"/> is <see cref="SmtLiveCrossoverKind.NotComputed"/>.</param>
internal sealed record SmtLiveCrossover(SmtLiveCrossoverKind Kind, double? Problems, double LocalFixed, double LocalPerProblem, double? FleetFixed,
    double? FleetPerProblem, string Reason)
{
    #region Functions

    /// <summary>
    /// Draws the two lines and finds where they cross.
    /// </summary>
    /// <param name="localProblems">The problems the local run took.</param>
    /// <param name="localWall">Its wall time, seconds.</param>
    /// <param name="localStartup">Its start-up, seconds.</param>
    /// <param name="fleetProblems">The problems the fleet run of the same set took.</param>
    /// <param name="fleetWall">Its wall time, seconds.</param>
    /// <param name="small">A fleet run of a small set: its problems and its wall time; null when none was measured.</param>
    /// <returns>The crossover.</returns>
    public static SmtLiveCrossover Compute(double localProblems, double localWall, double localStartup, double fleetProblems, double fleetWall,
        (double Problems, double Wall)? small)
    {
        if (localProblems <= 0 || localWall <= localStartup)
            return NotComputed(0, 0, "the local run solved no problem after its start-up");

        var localFixed = localStartup;
        var localCost = (localWall - localStartup) / localProblems;

        if (small is not { } point)
            return NotComputed(localFixed, localCost, "no fixed cost of a fleet run was measured: run a small set on the fleet (run --out) and name its folder with --fixed");

        if (point.Problems >= fleetProblems)
            return NotComputed(localFixed, localCost, $"the small fleet run took {point.Problems:0} problem(s), not fewer than the compared run's {fleetProblems:0}");

        var fleetCost = (fleetWall - point.Wall) / (fleetProblems - point.Problems);
        if (!(fleetCost > 0))
            return NotComputed(localFixed, localCost, "the line through the two fleet runs does not rise: the larger run took no longer than the small one");

        var fleetFixed = point.Wall - fleetCost * point.Problems;
        if (fleetFixed < 0)
            return NotComputed(localFixed, localCost, "the line through the two fleet runs gives a fixed cost below zero: the small set's problems cost more each than the compared set's");

        // T_local(n) - T_fleet(n) = (localFixed - fleetFixed) + n (localCost - fleetCost); the fleet is first where it is above 0.
        var saved = localCost - fleetCost;
        var head = localFixed - fleetFixed;

        if (saved > 0)
        {
            var crossover = -head / saved;
            if (crossover <= 0)
                return new SmtLiveCrossover(SmtLiveCrossoverKind.FleetAlways, null, localFixed, localCost, fleetFixed, fleetCost, "");

            return crossover > SmtProblemSetLimits.MAX_ROWS
                ? new SmtLiveCrossover(SmtLiveCrossoverKind.LocalAlways, crossover, localFixed, localCost, fleetFixed, fleetCost, "")
                : new SmtLiveCrossover(SmtLiveCrossoverKind.FleetAbove, crossover, localFixed, localCost, fleetFixed, fleetCost, "");
        }

        if (saved < 0 && head > 0)
            return new SmtLiveCrossover(SmtLiveCrossoverKind.FleetBelow, head / -saved, localFixed, localCost, fleetFixed, fleetCost, "");

        return head > 0
            ? new SmtLiveCrossover(SmtLiveCrossoverKind.FleetAlways, null, localFixed, localCost, fleetFixed, fleetCost, "")
            : new SmtLiveCrossover(SmtLiveCrossoverKind.LocalAlways, null, localFixed, localCost, fleetFixed, fleetCost, "");
    }

    private static SmtLiveCrossover NotComputed(double localFixed, double localCost, string reason)
    {
        return new SmtLiveCrossover(SmtLiveCrossoverKind.NotComputed, null, localFixed, localCost, null, null, reason);
    }

    #endregion
}
