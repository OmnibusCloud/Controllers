using MemoryPack;
using OutWit.Common.Abstract;
using OutWit.Common.Collections;
using OutWit.Common.Values;

namespace OutWit.Controller.OpenFOAM.Model;

/// <summary>
/// One variant's outcome. A failed step, a diverged solve or a refused case
/// is DATA here, not a task failure - the orchestration records the variant
/// and moves on; only infrastructure errors (blob transfer, a missing kit)
/// fail the activity.
/// </summary>
[MemoryPackable]
// Explicit MemoryPackOrder pins the wire layout to the declaration order - append new members at the END only (default MemoryPack mode rejects payloads with unknown members).
public sealed partial class FoamResultData : ModelBase
{
    #region Model Base

    public override bool Is(ModelBase modelBase, double tolerance = DEFAULT_TOLERANCE)
    {
        if (modelBase is not FoamResultData result)
            return false;

        return VariantIndex.Is(result.VariantIndex)
               && ExitCode.Is(result.ExitCode)
               && FailedStep.Is(result.FailedStep)
               && Rejections.Is(result.Rejections)
               && Steps.IsSequence(result.Steps, tolerance)
               && TotalSeconds.Is(result.TotalSeconds, tolerance)
               && Converged.Is(result.Converged)
               && Iterations.Is(result.Iterations)
               && FinalTime.Is(result.FinalTime, tolerance)
               && FinalResiduals.IsSequence(result.FinalResiduals, tolerance)
               && CellCount.Is(result.CellCount)
               && CheckMeshVerdict.Is(result.CheckMeshVerdict)
               && WarningCount.Is(result.WarningCount)
               && ResponseRow.Check(result.ResponseRow)
               && ArtifactBlobId.Is(result.ArtifactBlobId)
               && ArtifactBytes.Is(result.ArtifactBytes)
               && LogTail.Is(result.LogTail);
    }

    public override FoamResultData Clone()
    {
        return new FoamResultData
        {
            VariantIndex = VariantIndex,
            ExitCode = ExitCode,
            FailedStep = FailedStep,
            Rejections = Rejections.ToList(),
            Steps = Steps.Select(step => step.Clone()).ToList(),
            TotalSeconds = TotalSeconds,
            Converged = Converged,
            Iterations = Iterations,
            FinalTime = FinalTime,
            FinalResiduals = FinalResiduals.Select(residual => residual.Clone()).ToList(),
            CellCount = CellCount,
            CheckMeshVerdict = CheckMeshVerdict,
            WarningCount = WarningCount,
            ResponseRow = ResponseRow?.Clone(),
            ArtifactBlobId = ArtifactBlobId,
            ArtifactBytes = ArtifactBytes,
            LogTail = LogTail
        };
    }

    public override string ToString()
    {
        return Rejections.Count > 0
            ? $"variant #{VariantIndex}: refused ({Rejections.Count} finding(s))"
            : $"variant #{VariantIndex}: exit {ExitCode}, {TotalSeconds:F1} s, {(Converged ? "converged" : "not converged")} at {FinalTime}";
    }

    #endregion

    #region Properties

    /// <summary>Source-table index of the variant this result belongs to.</summary>
    [MemoryPackOrder(0)]
    public int VariantIndex { get; set; }

    /// <summary>Exit code of the failed step, or 0 when every step succeeded.</summary>
    [MemoryPackOrder(1)]
    public int ExitCode { get; set; }

    /// <summary>The utility of the step that failed; null when none did.</summary>
    [MemoryPackOrder(2)]
    public string? FailedStep { get; set; }

    /// <summary>Why the case was refused before anything ran (file and line where known); empty when it ran.</summary>
    [MemoryPackOrder(3)]
    public List<string> Rejections { get; set; } = [];

    /// <summary>Every step that ran, in order, with its exit code and wall time.</summary>
    [MemoryPackOrder(4)]
    public List<FoamStepOutcomeData> Steps { get; set; } = [];

    /// <summary>Wall time of the whole run in seconds, materialisation to upload.</summary>
    [MemoryPackOrder(5)]
    public double TotalSeconds { get; set; }

    /// <summary>True when the solver reported convergence (<c>residualControl</c> reached); false when it stopped at <c>endTime</c> or failed.</summary>
    [MemoryPackOrder(6)]
    public bool Converged { get; set; }

    /// <summary>Time steps or iterations the solver took.</summary>
    [MemoryPackOrder(7)]
    public int Iterations { get; set; }

    /// <summary>The last time value the solver reached.</summary>
    [MemoryPackOrder(8)]
    public double FinalTime { get; set; }

    /// <summary>The initial residual of every solved field at the last iteration (<c>residual.Ux</c>, ...).</summary>
    [MemoryPackOrder(9)]
    public List<FoamResponseValueData> FinalResiduals { get; set; } = [];

    /// <summary>Cell count as the node measured it (from <c>checkMesh</c> or the solver's mesh report); 0 when unknown.</summary>
    [MemoryPackOrder(10)]
    public long CellCount { get; set; }

    /// <summary>The <c>checkMesh</c> verdict when meshing ran (<c>ok</c>, <c>failed N checks</c>); null otherwise.</summary>
    [MemoryPackOrder(11)]
    public string? CheckMeshVerdict { get; set; }

    /// <summary>Number of FOAM warnings across the step logs.</summary>
    [MemoryPackOrder(12)]
    public int WarningCount { get; set; }

    /// <summary>Responses extracted on the node; null when the run failed before the post step.</summary>
    [MemoryPackOrder(13)]
    public FoamResponseRowData? ResponseRow { get; set; }

    /// <summary>Blob id of the uploaded artifact zip; null when the policy asked for nothing or the run failed.</summary>
    [MemoryPackOrder(14)]
    public Guid? ArtifactBlobId { get; set; }

    /// <summary>Size of the artifact zip in bytes; 0 when there is none.</summary>
    [MemoryPackOrder(15)]
    public long ArtifactBytes { get; set; }

    /// <summary>Last lines of the failing step's log - the error tail of a failed variant; null on success.</summary>
    [MemoryPackOrder(16)]
    public string? LogTail { get; set; }

    #endregion
}
