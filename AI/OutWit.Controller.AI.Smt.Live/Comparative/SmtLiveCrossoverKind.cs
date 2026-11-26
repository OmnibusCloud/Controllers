namespace OutWit.Controller.AI.Smt.Live.Comparative;

/// <summary>What the two lines of the comparative test say about which side finishes first (<see cref="SmtLiveCrossover"/>).</summary>
internal enum SmtLiveCrossoverKind
{
    /// <summary>The lines could not be drawn from what was measured; the reason says why.</summary>
    NotComputed,

    /// <summary>The fleet finishes first above the crossover, this machine below it.</summary>
    FleetAbove,

    /// <summary>The fleet finishes first below the crossover, this machine above it.</summary>
    FleetBelow,

    /// <summary>This machine finishes first at every size a set may have.</summary>
    LocalAlways,

    /// <summary>The fleet finishes first at every size a set may have.</summary>
    FleetAlways
}
