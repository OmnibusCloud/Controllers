using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// A case as a study runs it: the base case tree as file references, the
/// allow-listed recipe, the rank policy, the response request, the artifact
/// policy, and the scalars work estimation reads. The same for every variant
/// of a sweep - a variant adds only its token values (<see cref="FoamTaskData"/>)
/// - so the base files travel once per node and the study carries the case
/// once.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamCaseData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamCaseData data)
            return false;

        return BaseFiles.IsSequence(data.BaseFiles, tolerance)
               && Recipe.Check(data.Recipe)
               && Threads.Is(data.Threads)
               && Extraction.Check(data.Extraction)
               && ArtifactPolicy.Check(data.ArtifactPolicy)
               && CellCount.Is(data.CellCount)
               && SolverClass.Is(data.SolverClass)
               && TimeBudgetSeconds.Is(data.TimeBudgetSeconds, tolerance);
    }

    public override FoamCaseData Clone()
    {
        return new FoamCaseData
        {
            BaseFiles = BaseFiles.Select(file => file.Clone()).ToList(),
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
        return $"case: {Recipe?.Application ?? "?"}, {CellCount} cells, {BaseFiles.Count} file(s)";
    }

    #endregion

    #region Properties

    /// <summary>The base case tree, one reference per file.</summary>
    [MemoryPackOrder(0)]
    public List<FoamFileRefData> BaseFiles { get; set; } = [];

    /// <summary>The steps to run; null is refused (there is no default recipe).</summary>
    [MemoryPackOrder(1)]
    public FoamRecipeData? Recipe { get; set; }

    /// <summary>MPI ranks for the parallel steps; 0 = the node's cores, capped by the controller.</summary>
    [MemoryPackOrder(2)]
    public int Threads { get; set; }

    /// <summary>Responses to extract after the run; null = the convergence facts only.</summary>
    [MemoryPackOrder(3)]
    public FoamExtractionRequestData? Extraction { get; set; }

    /// <summary>What of the finished case to zip and upload; null = nothing.</summary>
    [MemoryPackOrder(4)]
    public FoamArtifactPolicyData? ArtifactPolicy { get; set; }

    /// <summary>Cell count as the initiator knows it (after refinement where it can tell), for work estimation.</summary>
    [MemoryPackOrder(5)]
    public long CellCount { get; set; }

    /// <summary>Solver class for the work estimate (<c>incompressible-steady</c>, <c>multiphase-transient</c>, ...).</summary>
    [MemoryPackOrder(6)]
    public string SolverClass { get; set; } = string.Empty;

    /// <summary>The initiator's estimate of one run's wall time in seconds; 0 = unknown. An estimate, never a limit.</summary>
    [MemoryPackOrder(7)]
    public double TimeBudgetSeconds { get; set; }

    #endregion
}
