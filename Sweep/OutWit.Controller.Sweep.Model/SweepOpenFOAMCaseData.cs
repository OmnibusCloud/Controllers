using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;
using OutWit.Controller.OpenFOAM.Model;

namespace OutWit.Controller.Sweep.Model;

/// <summary>
/// One variant's own complete case in an OpenFOAM case set (cases prepared
/// elsewhere - a geometry each, meshed or meshing - no tokens): its name, its
/// tree as file references, its recipe, and its mesh size and solver class
/// for work estimation. Everything a case set shares - threads, responses,
/// artifacts - stays in the study's OpenFOAM block.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class SweepOpenFOAMCaseData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not SweepOpenFOAMCaseData data)
            return false;

        return VariantIndex.Is(data.VariantIndex)
               && Name.Is(data.Name)
               && BaseFiles.IsSequence(data.BaseFiles, tolerance)
               && Recipe.Check(data.Recipe)
               && CellCount.Is(data.CellCount)
               && SolverClass.Is(data.SolverClass);
    }

    public override SweepOpenFOAMCaseData Clone()
    {
        return new SweepOpenFOAMCaseData
        {
            VariantIndex = VariantIndex,
            Name = Name,
            BaseFiles = BaseFiles.Select(file => file.Clone()).ToList(),
            Recipe = Recipe?.Clone(),
            CellCount = CellCount,
            SolverClass = SolverClass
        };
    }

    public override string ToString()
    {
        return $"variant #{VariantIndex}: case '{Name}', {Recipe?.Application ?? "?"}, {CellCount} cells, {BaseFiles.Count} file(s)";
    }

    #endregion

    #region Properties

    /// <summary>The variant this case is.</summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>The case's name as the user knows it (its folder in the set); the variant's label.</summary>
    [MemoryPackOrder(1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The case tree, one reference per file; nothing is templated.</summary>
    [MemoryPackOrder(2)]
    public List<FoamFileRefData> BaseFiles { get; set; } = [];

    /// <summary>The case's own allow-listed recipe.</summary>
    [MemoryPackOrder(3)]
    public FoamRecipeData? Recipe { get; set; }

    /// <summary>Cell count of the case, for work estimation; 0 falls back to the study block's.</summary>
    [MemoryPackOrder(4)]
    public long CellCount { get; set; }

    /// <summary>Solver class of the case, for work estimation; empty falls back to the study block's.</summary>
    [MemoryPackOrder(5)]
    public string SolverClass { get; set; } = string.Empty;

    #endregion
}
