using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// One variant's run as a self-contained work item - the single argument a
/// Grid.ForEach transformer receives. The base case travels as file
/// references (the same blobs for every variant of a sweep, so a node fetches
/// them once), the variant as token substitutions; cell count and solver class
/// ride as explicit scalars so work estimation never opens a file.
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
               && BaseFiles.IsSequence(task.BaseFiles, tolerance)
               && Substitutions.IsSequence(task.Substitutions, tolerance)
               && Recipe.Check(task.Recipe)
               && Threads.Is(task.Threads)
               && Extraction.Check(task.Extraction)
               && ArtifactPolicy.Check(task.ArtifactPolicy)
               && CellCount.Is(task.CellCount)
               && SolverClass.Is(task.SolverClass)
               && TimeBudgetSeconds.Is(task.TimeBudgetSeconds, tolerance);
    }

    public override FoamTaskData Clone()
    {
        return new FoamTaskData
        {
            VariantIndex = VariantIndex,
            BaseFiles = BaseFiles.Select(file => file.Clone()).ToList(),
            Substitutions = Substitutions.Select(substitution => substitution.Clone()).ToList(),
            Recipe = Recipe?.Clone(),
            Threads = Threads,
            Extraction = Extraction?.Clone(),
            ArtifactPolicy = ArtifactPolicy?.Clone(),
            CellCount = CellCount,
            SolverClass = SolverClass,
            TimeBudgetSeconds = TimeBudgetSeconds
        };
    }

    public override string ToString()
    {
        return $"variant #{VariantIndex}: {Recipe?.Application ?? "?"}, {CellCount} cells, {BaseFiles.Count} file(s)";
    }

    #endregion

    #region Properties

    /// <summary>
    /// Source-table index of the variant. Mandatory in every result mapping:
    /// Grid.ForEach returns results in completion order, never source order.
    /// </summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>The base case tree, one reference per file.</summary>
    [MemoryPackOrder(1)]
    public List<FoamFileRefData> BaseFiles { get; set; } = [];

    /// <summary>The variant's token values, applied to the templated files.</summary>
    [MemoryPackOrder(2)]
    public List<FoamTokenValueData> Substitutions { get; set; } = [];

    /// <summary>The steps to run; null is refused on the node (there is no default recipe).</summary>
    [MemoryPackOrder(3)]
    public FoamRecipeData? Recipe { get; set; }

    /// <summary>MPI ranks for the parallel steps; 0 = the node's cores, capped by the controller.</summary>
    [MemoryPackOrder(4)]
    public int Threads { get; set; }

    /// <summary>Responses to extract after the run; null = the convergence facts only.</summary>
    [MemoryPackOrder(5)]
    public FoamExtractionRequestData? Extraction { get; set; }

    /// <summary>What of the finished case to zip and upload; null = nothing.</summary>
    [MemoryPackOrder(6)]
    public FoamArtifactPolicyData? ArtifactPolicy { get; set; }

    /// <summary>Cell count as the initiator knows it (after refinement where it can tell), for work estimation.</summary>
    [MemoryPackOrder(7)]
    public long CellCount { get; set; }

    /// <summary>Solver class for the work estimate (<c>incompressible-steady</c>, <c>multiphase-transient</c>, ...).</summary>
    [MemoryPackOrder(8)]
    public string SolverClass { get; set; } = string.Empty;

    /// <summary>The initiator's estimate of the run's wall time in seconds; 0 = unknown.</summary>
    [MemoryPackOrder(9)]
    public double TimeBudgetSeconds { get; set; }

    #endregion
}
