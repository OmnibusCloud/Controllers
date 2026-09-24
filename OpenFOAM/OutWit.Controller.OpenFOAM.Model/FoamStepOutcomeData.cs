using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// How one step of the recipe ended: which utility, on how many ranks, its
/// exit code and its wall time. Meshing and solving are told apart by these.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamStepOutcomeData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamStepOutcomeData step)
            return false;

        return Utility.Is(step.Utility)
               && Ranks.Is(step.Ranks)
               && ExitCode.Is(step.ExitCode)
               && Seconds.Is(step.Seconds, tolerance);
    }

    public override FoamStepOutcomeData Clone()
    {
        return new FoamStepOutcomeData
        {
            Utility = Utility,
            Ranks = Ranks,
            ExitCode = ExitCode,
            Seconds = Seconds
        };
    }

    public override string ToString()
    {
        return $"{Utility}{(Ranks > 1 ? $" x{Ranks}" : "")}: exit {ExitCode}, {Seconds:F1} s";
    }

    #endregion

    #region Properties

    /// <summary>The executable's name.</summary>
    [MemoryPackOrder(0)]
    public string Utility { get; set; } = string.Empty;

    /// <summary>MPI ranks the step ran on; 1 for a serial step.</summary>
    [MemoryPackOrder(1)]
    public int Ranks { get; set; }

    /// <summary>Process exit code; 0 = success.</summary>
    [MemoryPackOrder(2)]
    public int ExitCode { get; set; }

    /// <summary>Wall time in seconds.</summary>
    [MemoryPackOrder(3)]
    public double Seconds { get; set; }

    #endregion
}
