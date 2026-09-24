using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// One step of a recipe: an allow-listed OpenFOAM utility or solver with its
/// arguments, serial or parallel. A parallel step runs under the kit's MPI
/// launcher with <c>-parallel</c> appended; the node writes the decomposition
/// dictionary itself. There is no free-form command: a step the allow-list
/// does not know is refused by name before anything runs.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamStepData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamStepData step)
            return false;

        return Utility.Is(step.Utility)
               && Arguments.Is(step.Arguments)
               && Parallel.Is(step.Parallel);
    }

    public override FoamStepData Clone()
    {
        return new FoamStepData
        {
            Utility = Utility,
            Arguments = Arguments.ToList(),
            Parallel = Parallel
        };
    }

    public override string ToString()
    {
        return $"{(Parallel ? "parallel " : "")}{Utility} {string.Join(' ', Arguments)}".TrimEnd();
    }

    #endregion

    #region Properties

    /// <summary>The executable's name as it stands in the kit (<c>blockMesh</c>, <c>simpleFoam</c>).</summary>
    [MemoryPackOrder(0)]
    public string Utility { get; set; } = string.Empty;

    /// <summary>Arguments, one token each; validated against the allow-list's grammar on the node.</summary>
    [MemoryPackOrder(1)]
    public List<string> Arguments { get; set; } = [];

    /// <summary>True to run under MPI on the decomposed case.</summary>
    [MemoryPackOrder(2)]
    public bool Parallel { get; set; }

    #endregion
}
