using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// One variant's run as a self-contained work item - the single argument a
/// Grid.ForEach transformer receives: the case (shared by every variant of a
/// study, so its base files travel once per node) and the variant's token
/// values, applied to the case's templated files on the node.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamTaskData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamTaskData task)
            return false;

        return VariantIndex.Is(task.VariantIndex)
               && Case.Check(task.Case)
               && Substitutions.IsSequence(task.Substitutions, tolerance);
    }

    public override FoamTaskData Clone()
    {
        return new FoamTaskData
        {
            VariantIndex = VariantIndex,
            Case = Case?.Clone(),
            Substitutions = Substitutions.Select(substitution => substitution.Clone()).ToList()
        };
    }

    public override string ToString()
    {
        return $"variant #{VariantIndex}: {Case?.ToString() ?? "no case"}, {Substitutions.Count} value(s)";
    }

    #endregion

    #region Properties

    /// <summary>
    /// Source-table index of the variant. Mandatory in every result mapping:
    /// Grid.ForEach returns results in completion order, never source order.
    /// </summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>The case to run; null is refused on the node.</summary>
    [MemoryPackOrder(1)]
    public FoamCaseData? Case { get; set; }

    /// <summary>The variant's token values, applied to the case's templated files.</summary>
    [MemoryPackOrder(2)]
    public List<FoamTokenValueData> Substitutions { get; set; } = [];

    #endregion
}
